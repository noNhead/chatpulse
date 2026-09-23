using System.Threading.Channels;
using System.Windows.Threading;
using ChatPulse.Chat;
using ChatPulse.Config;
using ChatPulse.Stats;

namespace ChatPulse.Core;

/// <summary>
/// Owns the chat connection, the stats engine and the snapshot the UI renders.
/// <para>
/// Concurrency contract: the engine is only ever touched while holding <see cref="_engineLock"/>,
/// snapshots are built on a background task, and the UI thread only ever receives a finished
/// immutable <see cref="Snapshot"/>.
/// </para>
/// </summary>
public sealed class AppState : Observable, IDisposable
{
    private readonly StatsEngine _engine = new();

    // Plain object rather than System.Threading.Lock: that type is .NET 9+, this targets net8.0.
    private readonly object _engineLock = new();
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// Everything that mutates the engine goes through this one channel, resets included — a reset
    /// dispatched separately could otherwise land after messages produced later and wipe them.
    /// Chat can burst past the UI refresh rate (and the demo feed replays 15 minutes at once), so
    /// the buffer is generous and drops the oldest rather than stalling the socket reader.
    /// </summary>
    private readonly Channel<Ingest> _inbox = Channel.CreateBounded<Ingest>(
        new BoundedChannelOptions(32_768) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private CancellationTokenSource? _connection;
    private string _activeChannel = string.Empty;

    private Snapshot _snapshot = Snapshot.Empty;
    private Settings _settings = Settings.Load();
    private ConnectionState _connectionState = new ConnectionState.Idle();
    private string _channelInput = string.Empty;
    private string? _selectedLogin;
    private string _search = string.Empty;
    private bool _overlayVisible;
    private bool _settingsOpen;
    private string _lastLog = string.Empty;

    public AppState(CliArgs cli, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _channelInput = cli.Channel ?? _settings.Channel;
        _overlayVisible = cli.Overlay;

        // Before any source starts. The ingest path trims the per-chatter message log as messages
        // arrive, so a config applied only at snapshot time would let a backfill burst — the demo
        // feed replays 15 minutes at once — get trimmed with stale defaults.
        SyncEngineConfig();

        _ = Task.Run(ConsumeAsync);
        _ = Task.Run(PublishSnapshotsAsync);

        if (cli.Demo) StartDemo(cli.DemoSeed);
        else if (cli.AutoConnect && !string.IsNullOrWhiteSpace(_channelInput)) Connect(_channelInput);
    }

    public Snapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }

    public Settings Settings { get => _settings; private set => Set(ref _settings, value); }

    public ConnectionState ConnectionState { get => _connectionState; private set => Set(ref _connectionState, value); }

    public string ChannelInput { get => _channelInput; set => Set(ref _channelInput, value); }

    public string? SelectedLogin { get => _selectedLogin; set => Set(ref _selectedLogin, value); }

    public string Search { get => _search; set => Set(ref _search, value); }

    public bool OverlayVisible { get => _overlayVisible; set => Set(ref _overlayVisible, value); }

    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }

    public string LastLog { get => _lastLog; private set => Set(ref _lastLog, value); }

    public bool IsDemo => ConnectionState is ConnectionState.Demo;

    // --- pipeline -------------------------------------------------------------------------

    private async Task ConsumeAsync()
    {
        await foreach (var item in _inbox.Reader.ReadAllAsync(_lifetime.Token))
        {
            switch (item)
            {
                case Ingest.Reset:
                    lock (_engineLock) _engine.Reset();
                    break;

                case Ingest.Event(ChatEvent.Message(var message)):
                    lock (_engineLock) _engine.Add(message);
                    break;

                case Ingest.Event(ChatEvent.Status(var state)):
                    Post(() => ConnectionState = state);
                    break;

                case Ingest.Event(ChatEvent.Log(var text)):
                    var stamped = $"{DateTime.Now:HH:mm:ss}  {text}";
                    Post(() => LastLog = stamped);
                    break;
            }
        }
    }

    private async Task PublishSnapshotsAsync()
    {
        var interval = Settings.RefreshMs;
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));

        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(_lifetime.Token)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (Settings.RefreshMs != interval)
            {
                interval = Settings.RefreshMs;
                timer.Dispose();
                timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            }

            var connection = ConnectionState;
            var selected = SelectedLogin;
            Snapshot snapshot;
            lock (_engineLock)
            {
                snapshot = _engine.BuildSnapshot(
                    connection, _activeChannel, selected, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }

            Post(() => Snapshot = snapshot);
        }

        timer.Dispose();
    }

    private void Post(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }

    /// <summary>Builds a snapshot synchronously. Used by the screenshot harness.</summary>
    public Snapshot SnapshotNow(string? selectedLogin)
    {
        lock (_engineLock)
        {
            return _engine.BuildSnapshot(
                ConnectionState, _activeChannel, selectedLogin, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    // --- connection -----------------------------------------------------------------------

    public void Connect(string rawChannel)
    {
        var channel = TwitchChatSource.Normalise(rawChannel);
        if (string.IsNullOrWhiteSpace(channel))
        {
            ConnectionState = new ConnectionState.Failed("Enter a channel name first");
            return;
        }

        if (!TwitchChatSource.IsValidChannel(channel))
        {
            ConnectionState = new ConnectionState.Failed("Not a Twitch channel name — letters, digits and _ only");
            return;
        }

        StartSource(new TwitchChatSource(channel), channel, resetStats: channel != _activeChannel);
    }

    public void StartDemo(int seed = 1337)
    {
        var source = new DemoChatSource(seed: seed);
        ChannelInput = source.Channel;
        StartSource(source, source.Channel, resetStats: true);
    }

    public void Disconnect()
    {
        _connection?.Cancel();
        _connection = null;
        ConnectionState = new ConnectionState.Idle();
        LastLog = $"{DateTime.Now:HH:mm:ss}  Disconnected";
    }

    public void ToggleConnection()
    {
        if (ConnectionState.IsLive
            || ConnectionState is ConnectionState.Connecting or ConnectionState.Reconnecting)
        {
            Disconnect();
        }
        else
        {
            Connect(ChannelInput);
        }
    }

    private void StartSource(IChatSource source, string channel, bool resetStats)
    {
        _connection?.Cancel();
        _activeChannel = channel;
        SelectedLogin = null;
        if (resetStats) _inbox.Writer.TryWrite(new Ingest.Reset());

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _connection = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await source.RunAsync(
                    async e => await _inbox.Writer.WriteAsync(new Ingest.Event(e), cts.Token),
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown of a previous connection.
            }
        }, cts.Token);

        UpdateSettings(s => s with { Channel = channel });
    }

    public void ResetStats()
    {
        SelectedLogin = null;
        _inbox.Writer.TryWrite(new Ingest.Reset());
        LastLog = $"{DateTime.Now:HH:mm:ss}  Statistics reset";
    }

    // --- settings -------------------------------------------------------------------------

    public void UpdateSettings(Func<Settings, Settings> transform)
    {
        var next = transform(Settings);
        if (next == Settings) return;
        Settings = next;
        SyncEngineConfig();
        next.Save();
    }

    /// <summary>Pushes the retention knobs into the engine so the ingest path sees them at once.</summary>
    private void SyncEngineConfig()
    {
        lock (_engineLock) _engine.Config = Settings.ToEngineConfig();
    }

    public void Select(string? login) => SelectedLogin = SelectedLogin == login ? null : login;

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private abstract record Ingest
    {
        public sealed record Event(ChatEvent Value) : Ingest;

        public sealed record Reset : Ingest;
    }
}
