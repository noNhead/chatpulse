using System.Collections.ObjectModel;
using System.Windows.Media;
using ChatPulse.Chat;
using ChatPulse.Core;
using ChatPulse.Stats;

namespace ChatPulse.Ui;

/// <summary>
/// One leaderboard row, reused in place.
/// <para>
/// The collection is positional — <c>Rows[i]</c> is always "rank i+1" and its properties are
/// overwritten on each tick. That means four snapshots a second produce zero collection-change
/// events and WPF only repaints the handful of realised rows whose text actually differs, instead
/// of rebuilding item containers.
/// </para>
/// </summary>
public sealed class ChatterRowVm : Observable
{
    private string _login = string.Empty;
    private string _rank = string.Empty;
    private Brush _rankBrush = BrushCache.Gray;
    private string _displayName = string.Empty;
    private string _initial = "?";
    private Brush _userBrush = BrushCache.Gray;
    private Brush _avatarBrush = BrushCache.Gray;
    private Color _sparkColor = Colors.MediumPurple;
    private float[] _spark = [];
    private double _barFraction;
    private string _rateText = "0";
    private string _countedText = "0";
    private string _uniqueText = "0%";
    private Brush _uniqueBrush = BrushCache.Gray;
    private string _lastText = string.Empty;
    private bool _isMod;
    private bool _isVip;
    private bool _isSelected;
    private bool _isEven;

    public string Login { get => _login; private set => Set(ref _login, value); }
    public string Rank { get => _rank; private set => Set(ref _rank, value); }
    public Brush RankBrush { get => _rankBrush; private set => Set(ref _rankBrush, value); }
    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }
    public string Initial { get => _initial; private set => Set(ref _initial, value); }
    public Brush UserBrush { get => _userBrush; private set => Set(ref _userBrush, value); }
    public Brush AvatarBrush { get => _avatarBrush; private set => Set(ref _avatarBrush, value); }
    public Color SparkColor { get => _sparkColor; private set => Set(ref _sparkColor, value); }
    public float[] Spark { get => _spark; private set => Set(ref _spark, value); }
    public double BarFraction { get => _barFraction; private set => Set(ref _barFraction, value); }
    public string RateText { get => _rateText; private set => Set(ref _rateText, value); }
    public string CountedText { get => _countedText; private set => Set(ref _countedText, value); }
    public string UniqueText { get => _uniqueText; private set => Set(ref _uniqueText, value); }
    public Brush UniqueBrush { get => _uniqueBrush; private set => Set(ref _uniqueBrush, value); }
    public string LastText { get => _lastText; private set => Set(ref _lastText, value); }
    public bool IsMod { get => _isMod; private set => Set(ref _isMod, value); }
    public bool IsVip { get => _isVip; private set => Set(ref _isVip, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsEven { get => _isEven; private set => Set(ref _isEven, value); }

    public void Update(ChatterRow row, int index, double maxRate, long nowMs, string? selectedLogin)
    {
        Login = row.Login;
        Rank = (index + 1).ToString();
        RankBrush = BrushCache.Frozen(Format.RankColor(index));
        DisplayName = row.DisplayName;
        Initial = row.DisplayName.Length > 0 ? row.DisplayName[..1].ToUpperInvariant() : "?";

        var color = Format.UserColor(row.Login, row.ColorArgb);
        UserBrush = BrushCache.Frozen(color);
        AvatarBrush = BrushCache.Frozen(color);

        Spark = row.Spark;
        BarFraction = maxRate > 0 ? row.Rate / maxRate : 0;
        RateText = Format.Rate(row.Rate);
        CountedText = Format.Count(row.WindowMessages);
        UniqueText = Format.Percent(row.UniquePercent) + "%";
        UniqueBrush = BrushCache.Frozen(Format.UniqueColor(row.UniquePercent));
        LastText = Format.Ago(row.LastMessageMs, nowMs);
        IsMod = row.IsMod;
        IsVip = row.IsVip;
        IsSelected = row.Login == selectedLogin;
        IsEven = index % 2 == 0;
    }
}

/// <summary>Cache of frozen brushes: a new SolidColorBrush per property set per tick adds up.</summary>
internal static class BrushCache
{
    private static readonly Dictionary<Color, SolidColorBrush> Cache = new();

    public static SolidColorBrush Gray { get; } = Frozen(Color.FromRgb(0x8B, 0x8B, 0xA2));

    public static SolidColorBrush Frozen(Color color)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(color, out var brush)) return brush;
            brush = new SolidColorBrush(color);
            brush.Freeze();
            Cache[color] = brush;
            return brush;
        }
    }
}

public sealed class RepeatedVm(string text, int count, int peak) : Observable
{
    public string Text { get; } = text;
    public string CountText { get; } = "x" + count;
    public double Fraction { get; } = peak > 0 ? Math.Clamp((double)count / peak, 0.08, 1.0) : 0;
}

public sealed class RecentVm(long ts, string text)
{
    public string Time { get; } = Format.Clock(ts);
    public string Text { get; } = text;
}

/// <summary>Everything the window binds to. Rebuilt from an immutable snapshot on the UI thread.</summary>
public sealed class DashboardVm : Observable
{
    private readonly AppState _state;

    public DashboardVm(AppState state)
    {
        _state = state;
    }

    public ObservableCollection<ChatterRowVm> Rows { get; } = [];

    public ObservableCollection<RepeatedVm> Repeated { get; } = [];

    public ObservableCollection<RecentVm> Recent { get; } = [];

    private bool _settingsOpen;

    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }

    // --- header / status ---
    private string _statusLabel = "OFFLINE";
    private Brush _statusBrush = BrushCache.Gray;
    private bool _statusActive;
    private string _connectLabel = "Connect";
    private bool _connected;

    public string StatusLabel { get => _statusLabel; private set => Set(ref _statusLabel, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public bool StatusActive { get => _statusActive; private set => Set(ref _statusActive, value); }
    public string ConnectLabel { get => _connectLabel; private set => Set(ref _connectLabel, value); }
    public bool Connected { get => _connected; private set => Set(ref _connected, value); }

    // --- stat strip ---
    private string _chatRate = "0";
    private string _rateUnit = "msg/min";
    private string _rateUnitUpper = "MSG/MIN";
    private SortKey _sortKey = SortKey.Rate;
    private string _peakRate = "peak 0";
    private float[] _globalSpark = [];
    private string _totalMessages = "0";
    private string _windowMessages = "0 in window";
    private string _chatters = "0";
    private string _activeChatters = "0 active now";
    private string _originality = "0";
    private string _originalitySub = "distinct / counted";
    private Brush _originalityBrush = BrushCache.Gray;
    private string _session = "00:00";
    private string _sessionSub = string.Empty;
    private string _storageUsage = string.Empty;

    public string ChatRate { get => _chatRate; private set => Set(ref _chatRate, value); }
    public string RateUnit { get => _rateUnit; private set => Set(ref _rateUnit, value); }
    public string RateUnitUpper { get => _rateUnitUpper; private set => Set(ref _rateUnitUpper, value); }
    public SortKey SortKey { get => _sortKey; private set => Set(ref _sortKey, value); }
    public string PeakRate { get => _peakRate; private set => Set(ref _peakRate, value); }
    public float[] GlobalSpark { get => _globalSpark; private set => Set(ref _globalSpark, value); }
    public string TotalMessages { get => _totalMessages; private set => Set(ref _totalMessages, value); }
    public string WindowMessages { get => _windowMessages; private set => Set(ref _windowMessages, value); }
    public string Chatters { get => _chatters; private set => Set(ref _chatters, value); }
    public string ActiveChatters { get => _activeChatters; private set => Set(ref _activeChatters, value); }
    public string Originality { get => _originality; private set => Set(ref _originality, value); }
    public string OriginalitySub { get => _originalitySub; private set => Set(ref _originalitySub, value); }
    public Brush OriginalityBrush { get => _originalityBrush; private set => Set(ref _originalityBrush, value); }
    public string Session { get => _session; private set => Set(ref _session, value); }
    public string SessionSub { get => _sessionSub; private set => Set(ref _sessionSub, value); }

    /// <summary>What the retention settings are actually costing right now, shown in Settings.</summary>
    public string StorageUsage { get => _storageUsage; private set => Set(ref _storageUsage, value); }

    // --- leaderboard chrome ---
    private string _rowCount = "0";
    private string _footerMessage = "Not connected";
    private Brush _footerBrush = BrushCache.Gray;
    private string _footerHint = string.Empty;
    private bool _hasRows;
    private string _emptyTitle = "Not connected";
    private string _emptySubtitle = "Enter a channel above and hit Connect";

    public string RowCount { get => _rowCount; private set => Set(ref _rowCount, value); }
    public string FooterMessage { get => _footerMessage; private set => Set(ref _footerMessage, value); }
    public Brush FooterBrush { get => _footerBrush; private set => Set(ref _footerBrush, value); }
    public string FooterHint { get => _footerHint; private set => Set(ref _footerHint, value); }
    public bool HasRows { get => _hasRows; private set => Set(ref _hasRows, value); }
    public string EmptyTitle { get => _emptyTitle; private set => Set(ref _emptyTitle, value); }
    public string EmptySubtitle { get => _emptySubtitle; private set => Set(ref _emptySubtitle, value); }

    // --- detail panel ---
    private bool _hasDetail;
    private string _detailName = string.Empty;
    private string _detailInitial = "?";
    private Brush _detailBrush = BrushCache.Gray;
    private string _detailRank = string.Empty;
    private Brush _detailRankBrush = BrushCache.Gray;
    private string _detailFirstSeen = string.Empty;
    private bool _detailMod;
    private bool _detailVip;
    private bool _detailSub;
    private string _detailCounted = "0";
    private string _detailCountedSub = string.Empty;
    private string _detailUnique = "0";
    private string _detailUniqueSub = string.Empty;
    private Brush _detailUniqueBrush = BrushCache.Gray;
    private string _detailTotal = "0";
    private string _detailTotalSub = string.Empty;
    private double _detailUniquePercent;
    private string _detailUniquePercentText = "0";
    private Color _detailUniqueColor = Colors.Gray;
    private string _detailVerdict = string.Empty;
    private string _detailUniqueCount = "0";
    private string _detailRepeatedCount = "0";
    private string _detailRate = "0";
    private string _detailPeak = string.Empty;
    private float[] _detailActivity = [];
    private string _detailActivityScale = string.Empty;
    private bool _hasRepeated;
    private string _recentLabel = "RECENT MESSAGES";

    public bool HasDetail { get => _hasDetail; private set => Set(ref _hasDetail, value); }
    public string DetailName { get => _detailName; private set => Set(ref _detailName, value); }
    public string DetailInitial { get => _detailInitial; private set => Set(ref _detailInitial, value); }
    public Brush DetailBrush { get => _detailBrush; private set => Set(ref _detailBrush, value); }
    public string DetailRank { get => _detailRank; private set => Set(ref _detailRank, value); }
    public Brush DetailRankBrush { get => _detailRankBrush; private set => Set(ref _detailRankBrush, value); }
    public string DetailFirstSeen { get => _detailFirstSeen; private set => Set(ref _detailFirstSeen, value); }
    public bool DetailMod { get => _detailMod; private set => Set(ref _detailMod, value); }
    public bool DetailVip { get => _detailVip; private set => Set(ref _detailVip, value); }
    public bool DetailSub { get => _detailSub; private set => Set(ref _detailSub, value); }
    public string DetailCounted { get => _detailCounted; private set => Set(ref _detailCounted, value); }
    public string DetailCountedSub { get => _detailCountedSub; private set => Set(ref _detailCountedSub, value); }
    public string DetailUnique { get => _detailUnique; private set => Set(ref _detailUnique, value); }
    public string DetailUniqueSub { get => _detailUniqueSub; private set => Set(ref _detailUniqueSub, value); }
    public Brush DetailUniqueBrush { get => _detailUniqueBrush; private set => Set(ref _detailUniqueBrush, value); }
    public string DetailTotal { get => _detailTotal; private set => Set(ref _detailTotal, value); }
    public string DetailTotalSub { get => _detailTotalSub; private set => Set(ref _detailTotalSub, value); }
    public double DetailUniquePercent { get => _detailUniquePercent; private set => Set(ref _detailUniquePercent, value); }
    public string DetailUniquePercentText { get => _detailUniquePercentText; private set => Set(ref _detailUniquePercentText, value); }
    public Color DetailUniqueColor { get => _detailUniqueColor; private set => Set(ref _detailUniqueColor, value); }
    public string DetailVerdict { get => _detailVerdict; private set => Set(ref _detailVerdict, value); }
    public string DetailUniqueCount { get => _detailUniqueCount; private set => Set(ref _detailUniqueCount, value); }
    public string DetailRepeatedCount { get => _detailRepeatedCount; private set => Set(ref _detailRepeatedCount, value); }
    public string DetailRate { get => _detailRate; private set => Set(ref _detailRate, value); }
    public string DetailPeak { get => _detailPeak; private set => Set(ref _detailPeak, value); }
    public float[] DetailActivity { get => _detailActivity; private set => Set(ref _detailActivity, value); }
    public string DetailActivityScale { get => _detailActivityScale; private set => Set(ref _detailActivityScale, value); }
    public bool HasRepeated { get => _hasRepeated; private set => Set(ref _hasRepeated, value); }

    /// <summary>Carries the log depth, so the panel does not look like it only kept a handful.</summary>
    public string RecentLabel { get => _recentLabel; private set => Set(ref _recentLabel, value); }

    public void Apply(Snapshot snapshot, string search, string? selectedLogin, string lastLog)
    {
        ApplyHeader(snapshot);
        ApplyStatStrip(snapshot);
        ApplyRows(snapshot, search, selectedLogin);
        ApplyDetail(snapshot);
        ApplyFooter(snapshot, lastLog);
    }

    private void ApplyHeader(Snapshot snapshot)
    {
        var (label, color) = snapshot.Connection switch
        {
            ConnectionState.Idle => ("OFFLINE", Color.FromRgb(0x8B, 0x8B, 0xA2)),
            ConnectionState.Connecting => ("CONNECTING", Color.FromRgb(0xFF, 0xBE, 0x4B)),
            ConnectionState.Live => ("LIVE", Color.FromRgb(0xFF, 0x3B, 0x5C)),
            ConnectionState.Demo => ("DEMO FEED", Color.FromRgb(0x2B, 0xE0, 0xD0)),
            ConnectionState.Reconnecting r => ($"RETRY {r.Attempt}", Color.FromRgb(0xFF, 0xBE, 0x4B)),
            _ => ("ERROR", Color.FromRgb(0xFF, 0x6B, 0x7A)),
        };

        StatusLabel = label;
        StatusBrush = BrushCache.Frozen(color);
        StatusActive = snapshot.Connection.IsLive || snapshot.Connection is ConnectionState.Connecting;

        var busy = snapshot.Connection is ConnectionState.Connecting or ConnectionState.Reconnecting;
        Connected = snapshot.Connection.IsLive || busy;
        ConnectLabel = busy ? "Cancel" : Connected ? "Disconnect" : "Connect";
    }

    private void ApplyStatStrip(Snapshot snapshot)
    {
        var g = snapshot.Global;
        var unit = snapshot.Config.RateUnit;

        ChatRate = Format.Rate(g.ChatRate);
        RateUnit = unit.Suffix();
        RateUnitUpper = unit.Suffix().ToUpperInvariant();
        SortKey = snapshot.Config.SortKey;
        PeakRate = "peak " + Format.Rate(g.PeakChatRate);
        GlobalSpark = g.Spark;
        TotalMessages = Format.Count(g.TotalMessages);
        WindowMessages = Format.Count(g.WindowMessages) + " in window";
        Chatters = Format.Count(g.TrackedChatters);
        ActiveChatters = g.ActiveChatters + " active now";
        // Chat-wide and measured over a short trailing window, so a copypasta wave shows up in
        // seconds instead of being diluted by everything collected since the session began.
        Originality = Format.Percent(g.ChatUniquePercent);
        OriginalityBrush = BrushCache.Frozen(Format.UniqueColor(g.ChatUniquePercent));
        OriginalitySub = $"{Format.Count(g.ChatUniqueDistinct)} of {Format.Count(g.ChatUniqueCounted)} " +
                         $"in last {Format.WindowLabel(snapshot.Config.OriginalityWindowSec)}";
        Session = Format.Duration(snapshot.GeneratedAtMs - g.SessionStartMs);
        // Rough but honest: a stored message is a timestamp, a string reference and its share of
        // the distinct-text map. ~120 bytes each is the right order of magnitude for chat lines.
        StorageUsage = g.WindowMessages == 0
            ? "nothing stored yet"
            : $"holding {Format.Count(g.WindowMessages)} messages from {g.TrackedChatters} chatters " +
              $"(~{Math.Max(1, g.WindowMessages * 120L / 1_048_576L)} MB)";
        SessionSub = $"window {Format.WindowLabel(snapshot.Config.RateWindowSec)} · keep {snapshot.Config.RetentionMs / 3_600_000}h";
    }

    private void ApplyRows(Snapshot snapshot, string search, string? selectedLogin)
    {
        var source = snapshot.Rows;
        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source
                .Where(r => r.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || r.Login.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Bars are scaled against the leader so the #1 row always reads as a full bar.
        var maxRate = snapshot.Rows.Count > 0 ? snapshot.Rows.Max(r => r.Rate) : 0;
        if (maxRate <= 0) maxRate = 1;

        while (Rows.Count > source.Count) Rows.RemoveAt(Rows.Count - 1);
        while (Rows.Count < source.Count) Rows.Add(new ChatterRowVm());

        for (var i = 0; i < source.Count; i++)
        {
            Rows[i].Update(source[i], i, maxRate, snapshot.GeneratedAtMs, selectedLogin);
        }

        HasRows = source.Count > 0;
        RowCount = source.Count == snapshot.Rows.Count
            ? snapshot.Rows.Count.ToString()
            : $"{source.Count} / {snapshot.Rows.Count}";

        if (!snapshot.Connection.IsLive)
        {
            EmptyTitle = "Not connected";
            EmptySubtitle = "Enter a channel above and hit Connect";
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            EmptyTitle = "No match";
            EmptySubtitle = $"Nobody matches \"{search}\"";
        }
        else
        {
            EmptyTitle = "Waiting for chat";
            EmptySubtitle = "Messages will appear here as they arrive";
        }
    }

    private void ApplyDetail(Snapshot snapshot)
    {
        if (snapshot.Detail is not { } detail)
        {
            HasDetail = false;
            return;
        }

        var row = detail.Row;
        HasDetail = true;
        DetailName = row.DisplayName;
        DetailInitial = row.DisplayName.Length > 0 ? row.DisplayName[..1].ToUpperInvariant() : "?";

        var color = Format.UserColor(row.Login, row.ColorArgb);
        DetailBrush = BrushCache.Frozen(color);

        var rank = snapshot.Rows.ToList().FindIndex(r => r.Login == row.Login);
        DetailRank = rank >= 0 ? "#" + (rank + 1) : string.Empty;
        DetailRankBrush = BrushCache.Frozen(Format.RankColor(Math.Max(rank, 0)));

        DetailFirstSeen = "first seen " + Format.AgoPhrase(row.FirstSeenMs, snapshot.GeneratedAtMs);
        DetailMod = row.IsMod;
        DetailVip = row.IsVip;
        DetailSub = row.IsSubscriber;

        var uniqueColor = Format.UniqueColor(row.UniquePercent);
        DetailCounted = Format.Count(row.WindowMessages);
        DetailCountedSub = $"last {snapshot.Config.RetentionMs / 3_600_000}h";
        DetailUnique = Format.Count(row.UniqueMessages);
        DetailUniqueSub = Format.Percent(row.UniquePercent) + "% original";
        DetailUniqueBrush = BrushCache.Frozen(uniqueColor);
        DetailTotal = Format.Count(row.TotalMessages);

        // Until a session outlives the retention window these two are equal, and saying so beats
        // letting it look like a duplicated number.
        DetailTotalSub = row.TotalMessages == row.WindowMessages ? "all still counted" : "since connect";

        DetailUniquePercent = row.UniquePercent;
        DetailUniquePercentText = Format.Percent(row.UniquePercent);
        DetailUniqueColor = uniqueColor;
        DetailVerdict = Verdict(row.UniquePercent);
        DetailUniqueCount = Format.Count(row.UniqueMessages);
        DetailRepeatedCount = Format.Count(row.WindowMessages - row.UniqueMessages);
        DetailRate = Format.Rate(row.Rate);
        DetailPeak = Format.Rate(detail.PeakRate);
        DetailActivity = detail.ActivityProfile;
        DetailActivityScale = $"0 – {detail.ActivityPeakPerBucket} per 20s";

        // Scaled against the worst offender, not against the whole message count — otherwise
        // every bar is the same small stub and the ranking is invisible.
        var peak = detail.TopRepeated.Count > 0 ? detail.TopRepeated[0].Count : 0;
        Repeated.Clear();
        foreach (var item in detail.TopRepeated) Repeated.Add(new RepeatedVm(item.Text, item.Count, peak));
        HasRepeated = Repeated.Count > 0;

        RecentLabel = $"RECENT MESSAGES · {detail.RecentMessages.Count}";
        Recent.Clear();
        // No UI-side cap: the engine already trims the log to the configured length, and clipping
        // it again here made the panel look like it only kept a handful of messages.
        foreach (var (ts, text) in detail.RecentMessages) Recent.Add(new RecentVm(ts, text));
    }

    private void ApplyFooter(Snapshot snapshot, string lastLog)
    {
        var (message, color) = snapshot.Connection switch
        {
            ConnectionState.Failed f => (f.Reason, Color.FromRgb(0xFF, 0x6B, 0x7A)),
            ConnectionState.Reconnecting r =>
                ($"Reconnecting to #{r.Channel}, attempt {r.Attempt}", Color.FromRgb(0xFF, 0xBE, 0x4B)),
            _ => (string.IsNullOrEmpty(lastLog) ? DefaultFooter(snapshot.Connection) : lastLog,
                Color.FromRgb(0x77, 0x77, 0x8D)),
        };

        FooterMessage = message;
        FooterBrush = BrushCache.Frozen(color);
        FooterHint = $"counted = messages inside the {snapshot.Config.RetentionMs / 3_600_000}h window · " +
                     $"speed averaged over {Format.WindowLabel(snapshot.Config.RateWindowSec)}";
    }

    private static string DefaultFooter(ConnectionState state) => state switch
    {
        ConnectionState.Demo => "Demo feed — synthetic chat, not connected to Twitch",
        ConnectionState.Live l => $"Reading #{l.Channel} anonymously",
        ConnectionState.Connecting c => $"Connecting to #{c.Channel}…",
        _ => "Not connected",
    };

    private static string Verdict(double percent) => percent switch
    {
        >= 85 => "Writes something new nearly every time.",
        >= 60 => "Mostly original with a few catchphrases.",
        >= 35 => "Leans on a small set of repeated lines.",
        >= 15 => "Heavy repetition — emote spam territory.",
        _ => "Almost entirely copy-paste.",
    };
}
