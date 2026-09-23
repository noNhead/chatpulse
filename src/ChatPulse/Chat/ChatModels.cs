namespace ChatPulse.Chat;

/// <summary>A single chat line, already normalised into something the stats engine can eat.</summary>
/// <param name="ColorArgb">Colour Twitch reports for the user, or 0 when they never picked one.</param>
public sealed record ChatMessage(
    string Login,
    string DisplayName,
    int ColorArgb,
    string Text,
    long TimestampMs,
    bool IsMod = false,
    bool IsVip = false,
    bool IsSubscriber = false);

/// <summary>Lifecycle of the chat connection, surfaced verbatim in the UI status pill.</summary>
public abstract record ConnectionState
{
    public sealed record Idle : ConnectionState;

    public sealed record Connecting(string Channel) : ConnectionState;

    public sealed record Live(string Channel, long SinceMs) : ConnectionState;

    public sealed record Reconnecting(string Channel, int Attempt, long InMs) : ConnectionState;

    public sealed record Failed(string Reason) : ConnectionState;

    public sealed record Demo(string Channel) : ConnectionState;

    public bool IsLive => this is Live or Demo;

    public string? ChannelOrNull => this switch
    {
        Connecting c => c.Channel,
        Live l => l.Channel,
        Reconnecting r => r.Channel,
        Demo d => d.Channel,
        _ => null,
    };
}

public abstract record ChatEvent
{
    public sealed record Message(ChatMessage Value) : ChatEvent;

    public sealed record Status(ConnectionState State) : ChatEvent;

    public sealed record Log(string Text) : ChatEvent;
}

/// <summary>Anything that can feed chat lines: the real Twitch socket, or the demo generator.</summary>
public interface IChatSource
{
    string Channel { get; }

    /// <summary>Runs until <paramref name="ct"/> is cancelled.</summary>
    Task RunAsync(Func<ChatEvent, Task> emit, CancellationToken ct);
}
