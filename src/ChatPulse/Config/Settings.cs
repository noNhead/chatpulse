using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatPulse.Stats;

namespace ChatPulse.Config;

/// <summary>
/// User-tunable knobs. Persisted as JSON under <c>%APPDATA%\ChatPulse</c> so nothing has to be
/// configured before the first run.
/// </summary>
public sealed record Settings
{
    public string Channel { get; init; } = string.Empty;
    public RateUnit RateUnit { get; init; } = RateUnit.PerMinute;

    /// <summary>Sliding window the speed is averaged over, in seconds.</summary>
    public int RateWindowSec { get; init; } = 60;

    /// <summary>
    /// Trailing window the chat-wide originality is measured over. Deliberately its own setting
    /// rather than a reuse of <see cref="RateWindowSec"/>: one knob quietly driving two unrelated
    /// numbers is how a label ends up lying about what it shows.
    /// </summary>
    public int OriginalityWindowSec { get; init; } = 60;

    public int RetentionHours { get; init; } = 24;

    /// <summary>
    /// Hard cap on messages kept per chatter inside the retention window; 0 means no cap.
    /// This is what bounds memory on a huge channel — the retention window alone does not,
    /// because one hyperactive chatter can write tens of thousands of lines in 24h.
    /// </summary>
    public int MaxEntriesPerUser { get; init; } = 5_000;

    /// <summary>How many raw messages per chatter the detail panel's log keeps.</summary>
    public int MessageLogPerUser { get; init; } = 200;

    public SortKey SortKey { get; init; } = SortKey.Rate;
    public int RefreshMs { get; init; } = 250;
    public int OverlayTopN { get; init; } = 10;
    public double OverlayOpacity { get; init; } = 0.92;
    public bool OverlayAlwaysOnTop { get; init; } = true;
    public bool OverlayCompact { get; init; }

    public static readonly int[] RateWindowChoices = [15, 30, 60, 300, 900];
    public static readonly int[] OriginalityWindowChoices = [15, 30, 60, 300, 900];
    public static readonly int[] RetentionChoices = [1, 6, 12, 24];
    public static readonly int[] MessageLogChoices = [50, 200, 1000, 5000];
    public static readonly int[] RefreshChoices = [100, 250, 500, 1000];
    public static readonly int[] OverlayTopChoices = [5, 8, 10, 15, 20];

    public EngineConfig ToEngineConfig() => new()
    {
        RetentionMs = RetentionHours * 60L * 60L * 1000L,
        RateWindowSec = RateWindowSec,
        OriginalityWindowSec = OriginalityWindowSec,
        RateUnit = RateUnit,
        SortKey = SortKey,
        // 0 in the UI means "no cap"; the engine only understands a number.
        MaxEntriesPerUser = MaxEntriesPerUser <= 0 ? int.MaxValue : MaxEntriesPerUser,
        RecentMessagesPerUser = MessageLogPerUser,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChatPulse",
        "settings.json");

    /// <summary>Shown in the UI instead of the resolved path, which contains the account name.</summary>
    public const string DisplayPath = @"%APPDATA%\ChatPulse\settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions)?.Clamped()
                   ?? new Settings();
        }
        catch
        {
            // A corrupt or unreadable settings file must never stop the app from starting.
            return new Settings();
        }
    }

    /// <summary>
    /// Pulls every number back into the range the UI offers. The file is hand-editable, and a value
    /// outside that range is not merely odd: a zero speed window or refresh period throws in the
    /// snapshot loop, a negative log length throws on the ingest path — either one silently kills a
    /// background loop and freezes the UI.
    /// </summary>
    private Settings Clamped() => this with
    {
        Channel = Channel ?? string.Empty,
        RateWindowSec = Math.Clamp(RateWindowSec, RateWindowChoices[0], RateWindowChoices[^1]),
        OriginalityWindowSec = Math.Clamp(
            OriginalityWindowSec, OriginalityWindowChoices[0], OriginalityWindowChoices[^1]),
        RetentionHours = Math.Clamp(RetentionHours, RetentionChoices[0], RetentionChoices[^1]),
        MessageLogPerUser = Math.Clamp(MessageLogPerUser, MessageLogChoices[0], MessageLogChoices[^1]),
        RefreshMs = Math.Clamp(RefreshMs, RefreshChoices[0], RefreshChoices[^1]),
        OverlayTopN = Math.Clamp(OverlayTopN, OverlayTopChoices[0], OverlayTopChoices[^1]),
        OverlayOpacity = Math.Clamp(OverlayOpacity, 0.35, 1.0),
    };

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Persisting preferences is best-effort; losing them is not worth crashing over.
        }
    }
}
