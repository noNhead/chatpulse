using System.Globalization;
using System.Windows.Media;

namespace ChatPulse.Ui;

public static class Format
{
    /// <summary>1_234 -> "1.2K", 1_234_567 -> "1.2M". Keeps metric tiles from reflowing.</summary>
    public static string Count(long value) => Math.Abs(value) switch
    {
        < 1_000 => value.ToString(CultureInfo.InvariantCulture),
        < 1_000_000 => TrimZero(value / 1_000.0) + "K",
        _ => TrimZero(value / 1_000_000.0) + "M",
    };

    public static string Count(int value) => Count((long)value);

    /// <summary>
    /// Speeds are small numbers people compare at a glance: one decimal below 100, and no trailing
    /// ".0" — a column reading "38.0 / 32.0 / 8.0" spends a third of its width saying nothing.
    /// </summary>
    public static string Rate(double value) => value switch
    {
        >= 100 => Math.Round(value).ToString("0", CultureInfo.InvariantCulture),
        > 0 => TrimZero(value),
        _ => "0",
    };

    public static string Percent(double value) => value >= 99.95 ? "100" : TrimZero(value);

    public static string Ago(long fromMs, long nowMs)
    {
        var sec = Math.Max(0, (nowMs - fromMs) / 1000);
        return sec switch
        {
            < 5 => "now",
            < 60 => $"{sec}s",
            < 3600 => $"{sec / 60}m",
            < 86400 => $"{sec / 3600}h",
            _ => $"{sec / 86400}d",
        };
    }

    /// <summary>Same scale as <see cref="Ago"/> but as a phrase: "just now", "12m ago".</summary>
    public static string AgoPhrase(long fromMs, long nowMs)
    {
        var text = Ago(fromMs, nowMs);
        return text == "now" ? "just now" : text + " ago";
    }

    public static string Duration(long ms)
    {
        var sec = Math.Max(0, ms / 1000);
        var h = sec / 3600;
        var m = sec % 3600 / 60;
        var s = sec % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public static string Clock(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string WindowLabel(int seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        _ when seconds % 3600 == 0 => $"{seconds / 3600}h",
        _ => $"{seconds / 60}m",
    };

    private static string TrimZero(double v)
    {
        var s = v.ToString("0.0", CultureInfo.InvariantCulture);
        return s.EndsWith(".0", StringComparison.Ordinal) ? s[..^2] : s;
    }

    /// <summary>Rank tint for the top three rows / overlay entries.</summary>
    public static Color RankColor(int index) => index switch
    {
        0 => Color.FromRgb(0xFF, 0xD2, 0x4B),
        1 => Color.FromRgb(0xCB, 0xD3, 0xDE),
        2 => Color.FromRgb(0xE0, 0x92, 0x5A),
        _ => Color.FromRgb(0x8B, 0x8B, 0xA2),
    };

    /// <summary>
    /// Originality read as a health signal: green = writes real sentences, red = copy-pastes the
    /// same line. This is the only place hue carries meaning, so nothing else competes with it.
    /// </summary>
    public static Color UniqueColor(double percent) => percent switch
    {
        >= 70 => Color.FromRgb(0x3B, 0xE3, 0x9A),
        >= 40 => Color.FromRgb(0xFF, 0xBE, 0x4B),
        _ => Color.FromRgb(0xFF, 0x6B, 0x7A),
    };

    private static readonly Color[] AvatarPalette =
    [
        Color.FromRgb(0x91, 0x46, 0xFF), Color.FromRgb(0x00, 0xC8, 0xB4), Color.FromRgb(0xFF, 0x7A, 0x45),
        Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0xFF, 0xC2, 0x4B), Color.FromRgb(0xFF, 0x5C, 0x8A),
        Color.FromRgb(0x7C, 0xE3, 0x8B), Color.FromRgb(0xB3, 0x88, 0xFF), Color.FromRgb(0x29, 0xB6, 0xF6),
        Color.FromRgb(0xE5, 0x73, 0x73), Color.FromRgb(0x80, 0xCB, 0xC4), Color.FromRgb(0xF0, 0x62, 0x92),
    ];

    /// <summary>Stable per-user colour when Twitch has no colour for them.</summary>
    public static Color UserColor(string login, int argb)
    {
        if (argb != 0)
        {
            return Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        var hash = 0;
        foreach (var c in login) hash = hash * 31 + c;
        return AvatarPalette[Math.Abs(hash % AvatarPalette.Length)];
    }
}
