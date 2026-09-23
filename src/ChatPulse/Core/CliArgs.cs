namespace ChatPulse.Core;

/// <summary>Command-line surface, kept tiny on purpose.</summary>
public sealed record CliArgs
{
    public string? Channel { get; init; }
    public bool Demo { get; init; }
    public int DemoSeed { get; init; } = 1337;
    public bool Overlay { get; init; }
    public bool AutoConnect { get; init; }
    public string? ScreenshotDir { get; init; }
    public string? SelfTestChannel { get; init; }
    public string? IconPath { get; init; }

    public const string Help = """
        ChatPulse — realtime Twitch chat analytics

          --channel <name>      connect to this channel on startup
          --demo                run against a synthetic chat feed (no network)
          --overlay             open the floating overlay right away
          --seed <int>          demo feed seed (default 1337)
          --screenshot <dir>    render the UI into <dir> and exit (no window shown)
          --selftest [channel]  connect to Twitch once, report, exit
          --help                this text
        """;

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--demo":
                    result = result with { Demo = true };
                    break;
                case "--overlay":
                    result = result with { Overlay = true };
                    break;
                case "--connect":
                    result = result with { AutoConnect = true };
                    break;
                case "--channel" when i + 1 < args.Length:
                    result = result with { Channel = args[++i], AutoConnect = true };
                    break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var seed):
                    i++;
                    result = result with { DemoSeed = seed };
                    break;
                case "--screenshot":
                    result = result with
                    {
                        ScreenshotDir = Next(args, ref i) ?? "screenshots",
                        Demo = true,
                    };
                    break;
                case "--selftest":
                    // Twitch's own channel: always exists, always has traffic.
                    result = result with { SelfTestChannel = Next(args, ref i) ?? "twitch" };
                    break;
                case "--icon" when i + 1 < args.Length:
                    result = result with { IconPath = args[++i] };
                    break;
                default:
                    if (args[i].StartsWith("--")) Console.Error.WriteLine($"Unknown option: {args[i]}");
                    break;
            }
        }

        return result;
    }

    /// <summary>Consumes the next argument only when it is a value rather than another flag.</summary>
    private static string? Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) return null;
        return args[++i];
    }
}
