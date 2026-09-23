namespace ChatPulse.Chat;

/// <summary>
/// Synthetic chat that behaves like a large channel: a handful of hyperactive emote spammers, a
/// long tail of normal viewers, and a few copy-paste bots with near-zero originality.
/// <para>
/// Used by <c>--demo</c> and by the screenshot harness so the UI can be reviewed without touching
/// the network. Seeded, so a given seed always produces the same leaderboard.
/// </para>
/// </summary>
public sealed class DemoChatSource : IChatSource
{
    private readonly Random _random;
    private readonly int _backfillSeconds;
    private readonly double _speed;

    /// <param name="backfillSeconds">Seconds of history synthesised instantly, so stats look established.</param>
    public DemoChatSource(string channel = "demo_channel", int seed = 1337, int backfillSeconds = 900, double speed = 1.0)
    {
        Channel = channel;
        _random = new Random(seed);
        _backfillSeconds = backfillSeconds;
        _speed = speed;
    }

    public string Channel { get; }

    public async Task RunAsync(Func<ChatEvent, Task> emit, CancellationToken ct)
    {
        var chatters = BuildChatters(_random);
        await emit(new ChatEvent.Log($"Demo feed started — {chatters.Count} synthetic chatters"));

        await BackfillAsync(chatters, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), emit);

        await emit(new ChatEvent.Status(new ConnectionState.Demo(Channel)));
        await emit(new ChatEvent.Log($"Backfilled {_backfillSeconds}s of history"));

        // Live tail: emit messages with exponentially distributed gaps.
        var perSecond = chatters.Sum(c => c.MessagesPerMinute) / 60.0 * _speed;
        while (!ct.IsCancellationRequested)
        {
            var gapMs = (int)Math.Clamp(ExpDelay(_random, perSecond) * 1000, 8, 3_000);
            try
            {
                await Task.Delay(gapMs, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var chatter = PickWeighted(chatters, _random);
            await emit(new ChatEvent.Message(chatter.Speak(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _random)));
        }
    }

    private async Task BackfillAsync(List<Chatter> chatters, long now, Func<ChatEvent, Task> emit)
    {
        var start = now - _backfillSeconds * 1000L;
        var lines = new List<ChatMessage>(4096);

        foreach (var chatter in chatters)
        {
            var t = start + _random.Next(20_000);
            var gapMs = 60_000.0 / chatter.MessagesPerMinute;
            while (t < now)
            {
                lines.Add(chatter.Speak(t, _random));
                t += Math.Max(120, (long)(gapMs * (0.45 + _random.NextDouble() * 1.3)));
            }
        }

        lines.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));
        foreach (var line in lines) await emit(new ChatEvent.Message(line));
    }

    private static double ExpDelay(Random rnd, double ratePerSecond) =>
        ratePerSecond <= 0 ? 1.0 : -Math.Log(1.0 - rnd.NextDouble()) / ratePerSecond;

    private static Chatter PickWeighted(List<Chatter> chatters, Random rnd)
    {
        var total = chatters.Sum(c => c.MessagesPerMinute);
        var roll = rnd.NextDouble() * total;
        foreach (var c in chatters)
        {
            roll -= c.MessagesPerMinute;
            if (roll <= 0) return c;
        }

        return chatters[^1];
    }

    private sealed class Chatter(
        string login,
        string displayName,
        int colorArgb,
        double messagesPerMinute,
        double originality,
        IReadOnlyList<string> vocabulary,
        bool isMod,
        bool isVip,
        bool isSub)
    {
        public double MessagesPerMinute { get; } = messagesPerMinute;

        public ChatMessage Speak(long atMs, Random rnd)
        {
            var basis = vocabulary[rnd.Next(vocabulary.Count)];
            // originality 0.0 = repeats one line forever, 1.0 = never repeats itself.
            var text = rnd.NextDouble() < originality ? Decorate(basis, rnd) : basis;
            return new ChatMessage(login, displayName, colorArgb, text, atMs, isMod, isVip, isSub);
        }

        private static string Decorate(string basis, Random rnd) => rnd.Next(5) switch
        {
            0 => $"{basis} {Suffixes[rnd.Next(Suffixes.Length)]}",
            1 => $"{Prefixes[rnd.Next(Prefixes.Length)]} {basis}",
            2 => basis + " " + new string('!', rnd.Next(1, 4)),
            3 => basis.ToUpperInvariant(),
            _ => $"{basis} {Suffixes[rnd.Next(Suffixes.Length)]} {Suffixes[rnd.Next(Suffixes.Length)]}",
        };
    }

    private static readonly string[] Prefixes =
        ["honestly", "ngl", "wait", "bro", "chat", "ok but", "yo", "actually"];

    private static readonly string[] Suffixes =
        ["KEKW", "LULW", "Pog", "monkaS", "OMEGALUL", "peepoHappy", "Sadge", "EZ Clap"];

    private static readonly string[] Spam =
        ["KEKW", "OMEGALUL", "Pog", "LULW", "monkaS", "PogChamp", "5Head", "Sadge", "EZ"];

    private static readonly string[] Hype =
    [
        "LETS GOOOO", "NO WAY", "THAT WAS INSANE", "CLIP IT", "actual cracked gameplay",
        "he's him", "unreal", "GOAT behaviour", "chat did you see that",
    ];

    private static readonly string[] Normal =
    [
        "what game is this", "how long has he been live", "first time here, this is great",
        "the new setup looks clean", "anyone know the song", "gn chat",
        "just got here what did I miss", "that strat never works lol",
        "mods are asleep", "second monitor stream fr", "this is peak content",
        "the pacing on this run is nuts", "hi from brazil", "day 42 of asking for a nuzlocke",
        "wait he actually pulled it off", "chat is so cooked today", "monitor stutter or is it me",
        "does he have a schedule anywhere", "returned after 2 years, still elite",
    ];

    private static readonly string[] Questions =
    [
        "what's the sens?", "what mouse is that?", "sub goal?", "when's the next stream?",
        "any plans for the tournament?", "is the vod going up?", "which patch is this?",
    ];

    private static readonly string[] Bot = ["Follow the socials in the panels below!"];

    private static readonly int[] Colors =
    [
        unchecked((int)0xFF9146FF), unchecked((int)0xFF00D1B2), unchecked((int)0xFFFF7A45),
        unchecked((int)0xFF4FC3F7), unchecked((int)0xFFFFC24B), unchecked((int)0xFFFF5C8A),
        unchecked((int)0xFF7CE38B), unchecked((int)0xFFB388FF), unchecked((int)0xFF29B6F6),
        unchecked((int)0xFFE57373), unchecked((int)0xFF80CBC4), unchecked((int)0xFFF06292),
    ];

    private static List<Chatter> BuildChatters(Random rnd)
    {
        var result = new List<Chatter>();

        void Add(string name, double mpm, double originality, IReadOnlyList<string> vocab,
            bool mod = false, bool vip = false, bool sub = true)
        {
            result.Add(new Chatter(
                name.ToLowerInvariant(), name, Colors[result.Count % Colors.Length],
                mpm, originality, vocab, mod, vip, sub));
        }

        // Headliners — the ones the streamer actually notices.
        Add("Kappaccino", 41.0, 0.05, Spam);                         // pure emote spam
        Add("xQcLurker", 33.0, 0.55, [.. Hype, .. Spam]);
        Add("moon2Enjoyer", 27.0, 0.12, Spam);
        Add("VoidWalker_", 22.0, 0.85, [.. Normal, .. Hype], vip: true);
        Add("Nightbot_", 19.0, 0.0, Bot, mod: true, sub: false);     // one unique message, ever
        Add("PogTycoon", 17.5, 0.35, [.. Spam, .. Hype]);
        Add("Sable_TTV", 15.0, 0.92, [.. Normal, .. Questions], mod: true);
        Add("frostbyte99", 13.0, 0.7, Normal);
        Add("LULWmachine", 12.0, 0.03, ["LULW"]);                    // literally one word
        Add("clipthatchat", 10.5, 0.6, Hype);

        string[] mids =
        [
            "aquaticfern", "BingusPrime", "dreadnaught_", "Elowen", "fatalbyte",
            "GlassCannon", "hexadecimal", "IcarusFalls", "jellybeanz", "Kryptonian",
        ];
        foreach (var n in mids)
        {
            Add(n, rnd.NextDouble() * 5.5 + 3.0, rnd.NextDouble() * 0.55 + 0.4,
                [.. Normal, .. Questions, .. Spam]);
        }

        string[] tail =
        [
            "lunarmoth", "mistcaller", "NovaSurge", "obsidian_ow", "PixelPilgrim",
            "quietstorm", "RiftRunner", "solaceex", "tempest_ttv", "umbra_lynx",
            "ValkyrieX", "wanderlust", "xenonflux", "YonderPeak", "zephyrion",
            "battlecatt", "coldbrewfan", "duskwing", "emberglow", "fenrirwolf",
        ];
        foreach (var n in tail)
        {
            Add(n, rnd.NextDouble() * 2.2 + 0.4, rnd.NextDouble() * 0.5 + 0.5,
                [.. Normal, .. Questions], sub: rnd.Next(2) == 0);
        }

        return result;
    }
}
