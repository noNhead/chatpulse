using ChatPulse.Chat;

namespace ChatPulse.Stats;

/// <summary>The unit the leaderboard's speed column is expressed in.</summary>
public enum RateUnit
{
    PerSecond,
    PerMinute,
    PerHour,
}

public enum SortKey
{
    Rate,
    Messages,
    Unique,
    Total,
}

public static class UnitExtensions
{
    public static int Seconds(this RateUnit unit) => unit switch
    {
        RateUnit.PerSecond => 1,
        RateUnit.PerMinute => 60,
        _ => 3600,
    };

    public static string Suffix(this RateUnit unit) => unit switch
    {
        RateUnit.PerSecond => "msg/s",
        RateUnit.PerMinute => "msg/min",
        _ => "msg/h",
    };

    public static string Label(this SortKey key) => key switch
    {
        SortKey.Rate => "Speed",
        SortKey.Messages => "Messages",
        SortKey.Unique => "Unique %",
        _ => "Session total",
    };
}

public sealed record EngineConfig
{
    /// <summary>How far back messages are kept and counted. Requirement: 24h by default.</summary>
    public long RetentionMs { get; init; } = 24L * 60 * 60 * 1000;

    /// <summary>Sliding window the speed is measured over.</summary>
    public int RateWindowSec { get; init; } = 60;

    /// <summary>Trailing window the chat-wide originality is measured over.</summary>
    public int OriginalityWindowSec { get; init; } = 60;

    public RateUnit RateUnit { get; init; } = RateUnit.PerMinute;

    public SortKey SortKey { get; init; } = SortKey.Rate;

    /// <summary>Hard cap per user so a 24h window on a huge channel cannot eat all the memory.</summary>
    public int MaxEntriesPerUser { get; init; } = 5_000;

    public int RecentMessagesPerUser { get; init; } = 40;

    public int MaxRows { get; init; } = 400;
}

/// <summary>One row of the leaderboard.</summary>
public sealed record ChatterRow(
    string Login,
    string DisplayName,
    int ColorArgb,
    double Rate,
    int WindowMessages,
    int UniqueMessages,
    double UniquePercent,
    long TotalMessages,
    long FirstSeenMs,
    long LastMessageMs,
    bool IsMod,
    bool IsVip,
    bool IsSubscriber,
    float[] Spark);

public sealed record RepeatedMessage(string Text, int Count);

/// <summary>Everything the detail view shows for one selected chatter.</summary>
public sealed record ChatterDetail(
    ChatterRow Row,
    IReadOnlyList<RepeatedMessage> TopRepeated,
    IReadOnlyList<(long Ts, string Text)> RecentMessages,
    float[] ActivityProfile,
    int ActivityPeakPerBucket,
    double PeakRate);

public sealed record GlobalStats(
    long TotalMessages,
    int WindowMessages,
    int TrackedChatters,
    int ActiveChatters,
    double ChatRate,
    double PeakChatRate,
    /// <summary>
    /// Distinct lines over all lines, chat-wide, across the originality window — "is chat
    /// posting the same thing right now". Compares people against each other, not only against
    /// themselves: forty chatters each posting KEKW once is 1 distinct of 40, not 100%.
    /// </summary>
    double ChatUniquePercent,
    int ChatUniqueDistinct,
    int ChatUniqueCounted,
    long SessionStartMs,
    float[] Spark)
{
    public static GlobalStats Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), []);
}

public sealed record Snapshot(
    long GeneratedAtMs,
    EngineConfig Config,
    ConnectionState Connection,
    string Channel,
    GlobalStats Global,
    IReadOnlyList<ChatterRow> Rows,
    ChatterDetail? Detail)
{
    public static Snapshot Empty { get; } = new(
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        new EngineConfig(),
        new ConnectionState.Idle(),
        string.Empty,
        GlobalStats.Empty,
        [],
        null);
}
