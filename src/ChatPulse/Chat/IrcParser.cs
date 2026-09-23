using System.Globalization;
using System.Text;

namespace ChatPulse.Chat;

/// <summary>
/// Minimal IRCv3 line parser — enough for what Twitch actually sends us.
/// <code>@tag=a;tag2=b :nick!user@host COMMAND param1 param2 :trailing text</code>
/// </summary>
public sealed record IrcLine(
    IReadOnlyDictionary<string, string> Tags,
    string? Prefix,
    string Command,
    IReadOnlyList<string> Params)
{
    public string? Trailing => Params.Count > 0 ? Params[^1] : null;

    public string? Tag(string name) =>
        Tags.TryGetValue(name, out var v) && v.Length > 0 ? v : null;
}

public static class IrcParser
{
    public static IrcLine? Parse(string raw)
    {
        var line = raw.TrimEnd('\r', '\n');
        if (line.Length == 0) return null;

        var i = 0;
        IReadOnlyDictionary<string, string> tags = EmptyTags;

        if (line[i] == '@')
        {
            var end = line.IndexOf(' ', i);
            if (end < 0) return null;
            tags = ParseTags(line[(i + 1)..end]);
            i = end + 1;
        }

        string? prefix = null;
        if (i < line.Length && line[i] == ':')
        {
            var end = line.IndexOf(' ', i);
            if (end < 0) return null;
            prefix = line[(i + 1)..end];
            i = end + 1;
        }

        while (i < line.Length && line[i] == ' ') i++;
        if (i >= line.Length) return null;

        var cmdEnd = line.IndexOf(' ', i);
        if (cmdEnd < 0) cmdEnd = line.Length;
        var command = line[i..cmdEnd];
        i = cmdEnd;

        var parameters = new List<string>(4);
        while (i < line.Length)
        {
            while (i < line.Length && line[i] == ' ') i++;
            if (i >= line.Length) break;
            if (line[i] == ':')
            {
                parameters.Add(line[(i + 1)..]);
                break;
            }

            var end = line.IndexOf(' ', i);
            if (end < 0) end = line.Length;
            parameters.Add(line[i..end]);
            i = end;
        }

        return new IrcLine(tags, prefix, command, parameters);
    }

    private static readonly Dictionary<string, string> EmptyTags = new(0);

    private static Dictionary<string, string> ParseTags(string blob)
    {
        var result = new Dictionary<string, string>(16, StringComparer.Ordinal);
        foreach (var part in blob.Split(';'))
        {
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=');
            if (eq < 0) result[part] = string.Empty;
            else result[part[..eq]] = UnescapeTag(part[(eq + 1)..]);
        }

        return result;
    }

    /// <summary>IRCv3 tag escaping: <c>\s</c> space, <c>\:</c> semicolon, <c>\r</c> <c>\n</c>, <c>\\</c>.</summary>
    private static string UnescapeTag(string value)
    {
        if (!value.Contains('\\')) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i + 1] switch
                {
                    ':' => ';',
                    's' => ' ',
                    'r' => '\r',
                    'n' => '\n',
                    '\\' => '\\',
                    var other => other,
                });
                i++;
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary><c>nick!user@host</c> -> <c>nick</c></summary>
    public static string? NickOf(string? prefix)
    {
        if (prefix is null) return null;
        var bang = prefix.IndexOf('!');
        if (bang > 0) return prefix[..bang];
        return prefix.Contains('.') ? null : prefix;
    }

    /// <summary>Twitch sends <c>#RRGGBB</c>; we want an opaque ARGB int, or 0 when unset.</summary>
    public static int ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return 0;
        return int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? unchecked((int)0xFF000000) | rgb
            : 0;
    }
}
