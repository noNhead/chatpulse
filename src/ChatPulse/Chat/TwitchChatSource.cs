using System.IO;
using System.Net.WebSockets;
using System.Text;

namespace ChatPulse.Chat;

/// <summary>
/// Read-only Twitch chat over the official IRC-via-WebSocket gateway.
/// <para>
/// Anonymous login: Twitch accepts any <c>justinfan&lt;digits&gt;</c> nick with no password and
/// grants read-only access to any public channel — no OAuth, no account, nothing to leak. We ask
/// for the <c>tags</c> capability so display names, colours and badges come through.
/// </para>
/// <para>
/// Built on <see cref="ClientWebSocket"/> from the BCL, so this file is the entire Twitch client:
/// no third-party dependency is involved.
/// </para>
/// </summary>
public sealed class TwitchChatSource : IChatSource
{
    private const string Gateway = "wss://irc-ws.chat.twitch.tv:443";
    private const string Tmi = "tmi.twitch.tv";
    private static readonly int[] BackoffMs = [1_000, 2_000, 4_000, 8_000, 15_000, 30_000];

    /// <summary>
    /// Ceiling on one WebSocket message. Twitch's run to kilobytes even for a burst of lines, so a
    /// megabyte means a broken peer — and without a ceiling the buffer would grow without bound.
    /// </summary>
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly string _channel;

    public TwitchChatSource(string channel)
    {
        _channel = Normalise(channel);
        if (!IsValidChannel(_channel))
        {
            throw new ArgumentException("Not a valid Twitch channel name.", nameof(channel));
        }
    }

    public string Channel => _channel;

    /// <summary><c>"#Foo "</c> -> <c>"foo"</c></summary>
    public static string Normalise(string channel) => channel.Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>
    /// Twitch logins are 1-25 characters of a-z, 0-9 and underscore. The name is written straight
    /// into the JOIN line, so anything else — a pasted CR/LF above all — must never reach the socket.
    /// </summary>
    public static bool IsValidChannel(string channel) =>
        channel.Length is >= 1 and <= 25
        && channel.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    public async Task RunAsync(Func<ChatEvent, Task> emit, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (attempt == 0)
                {
                    await emit(new ChatEvent.Status(new ConnectionState.Connecting(_channel)));
                }
                else
                {
                    var wait = BackoffMs[Math.Min(attempt - 1, BackoffMs.Length - 1)];
                    await emit(new ChatEvent.Status(new ConnectionState.Reconnecting(_channel, attempt, wait)));
                    await emit(new ChatEvent.Log($"Reconnecting in {wait / 1000}s (attempt {attempt})"));
                    await Task.Delay(wait, ct);
                }

                await SessionAsync(emit, ct);
                attempt = 0; // clean disconnect after a healthy session -> restart the ladder
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                attempt++;
                await emit(new ChatEvent.Log($"Connection lost: {e.Message}"));
            }
        }
    }

    /// <summary>One connection attempt. Returns when the socket closes; throws when it breaks.</summary>
    private async Task SessionAsync(Func<ChatEvent, Task> emit, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(Gateway), ct);

        var nick = "justinfan" + Random.Shared.Next(10_000, 99_999);
        await SendLineAsync(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands", ct);
        await SendLineAsync(socket, $"NICK {nick}", ct);
        await SendLineAsync(socket, $"JOIN #{_channel}", ct);
        await emit(new ChatEvent.Log($"Connected to {Gateway} as {nick}"));

        var joined = false;

        await foreach (var raw in ReadLinesAsync(socket, ct))
        {
            var line = IrcParser.Parse(raw);
            if (line is null) continue;

            switch (line.Command)
            {
                case "PRIVMSG":
                    if (ToChatMessage(line) is { } message)
                        await emit(new ChatEvent.Message(message));
                    break;

                case "PING":
                    await SendLineAsync(socket, $"PONG :{line.Trailing ?? Tmi}", ct);
                    break;

                // 366 = end of /NAMES, the reliable "you are in the room" signal.
                case "366":
                case "ROOMSTATE":
                    if (!joined)
                    {
                        joined = true;
                        await emit(new ChatEvent.Status(
                            new ConnectionState.Live(_channel, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
                        await emit(new ChatEvent.Log($"Joined #{_channel}"));
                    }

                    break;

                case "NOTICE":
                    await emit(new ChatEvent.Log($"Twitch: {line.Trailing}"));
                    break;

                case "RECONNECT":
                    await emit(new ChatEvent.Log("Twitch asked us to reconnect"));
                    return;
            }
        }
    }

    /// <summary>
    /// Twitch may split a burst of IRC lines across WebSocket fragments, so we buffer until the
    /// frame ends and only then cut on CRLF. The bytes are decoded once the frame is whole: a
    /// multi-byte character split across two reads would otherwise turn into two replacement
    /// characters.
    /// </summary>
    private static async IAsyncEnumerable<string> ReadLinesAsync(
        ClientWebSocket socket,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var frame = new MemoryStream();

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) yield break;

            frame.Write(buffer, 0, result.Count);
            if (frame.Length > MaxMessageBytes)
            {
                throw new InvalidDataException($"Message from Twitch exceeded {MaxMessageBytes / 1024} KB");
            }

            if (!result.EndOfMessage) continue;

            var text = Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
            frame.SetLength(0);

            foreach (var line in text.Split("\r\n"))
            {
                if (!string.IsNullOrWhiteSpace(line)) yield return line;
            }
        }
    }

    private static ChatMessage? ToChatMessage(IrcLine line)
    {
        if (line.Trailing is not { } text) return null;

        var login = (line.Tag("login") ?? IrcParser.NickOf(line.Prefix))?.ToLowerInvariant();
        if (login is null) return null;

        var badges = line.Tag("badges") ?? string.Empty;
        var displayName = line.Tag("display-name");

        return new ChatMessage(
            Login: login,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? login : displayName,
            ColorArgb: IrcParser.ParseColor(line.Tag("color")),
            Text: text,
            TimestampMs: long.TryParse(line.Tag("tmi-sent-ts"), out var ts)
                ? ts
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsMod: line.Tag("mod") == "1" || badges.Contains("broadcaster/") || badges.Contains("moderator/"),
            IsVip: badges.Contains("vip/"),
            IsSubscriber: line.Tag("subscriber") == "1" || badges.Contains("subscriber/"));
    }

    private static Task SendLineAsync(ClientWebSocket socket, string line, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, ct);
}
