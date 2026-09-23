using System.Globalization;
using System.Text;
using ChatPulse.Chat;

namespace ChatPulse.Stats;

/// <summary>
/// In-memory chat accounting.
/// <para>
/// Per chatter we keep the messages of the last <see cref="EngineConfig.RetentionMs"/> (24h by
/// default) plus a count-per-distinct-text map, which gives all three numbers the tracker needs
/// in O(1): messages counted in the window (<c>entries.Count</c>), unique messages
/// (<c>counts.Count</c>), and the session total (a counter that is never trimmed).
/// </para>
/// <para>
/// Speed is "messages in the last RateWindowSec", rescaled to the chosen unit.
/// </para>
/// <para>
/// Not thread safe by design: AppState funnels every call through a single lock, and the UI only
/// ever sees immutable <see cref="Snapshot"/>s.
/// </para>
/// </summary>
public sealed class StatsEngine
{
    private const int SparkBuckets = 20;
    private const int GlobalSparkBuckets = 48;

    /// <summary>45 buckets over 15 minutes = one bucket per 20 seconds, which is a labelable unit.</summary>
    private const int DetailBuckets = 45;

    private const long DetailSpanMs = 15 * 60 * 1000;
    private const double MinEffectiveWindowSec = 5.0;

    private readonly Dictionary<string, UserAcc> _users = new(512, StringComparer.Ordinal);

    /// <summary>
    /// Recent chat across all users, newest last. Carries the text as well as the timestamp so
    /// originality can be measured over a trailing window rather than over everything collected
    /// since the session began — a cumulative ratio only ever drifts downwards and stops saying
    /// anything about what chat is doing now.
    /// <para>
    /// Only spans as far back as the widest window that reads it, not the full retention period.
    /// </para>
    /// </summary>
    private readonly Deque<Entry> _chatLog = new();

    /// <summary>Reused across snapshots so counting distinct texts allocates nothing.</summary>
    private readonly HashSet<string> _distinctScratch = new(StringComparer.Ordinal);

    /// <summary>
    /// Every distinct message text in the window, counted across the whole chat.
    /// <para>
    /// Summing each chatter's distinct count answers "does this person repeat themselves"; it
    /// cannot answer "is the whole chat repeating one line", because forty people each posting
    /// KEKW once are individually 100% original. This map compares them against each other.
    /// </para>
    /// <para>
    /// It doubles as an intern table: every chatter's copy of a repeated line points at the one
    /// string stored here, so tracking chat-wide originality costs less memory than not tracking it.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, TextCount> _chatTexts = new(StringComparer.Ordinal);

    private long _totalMessages;
    private double _peakChatRate;
    private long _firstMessageMs = long.MaxValue;

    public EngineConfig Config { get; set; } = new();

    public long SessionStartMs { get; private set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Start of the data we actually hold. Normally this is when the session began, but a source
    /// may replay history (the demo feed backfills 15 minutes), and rates measured against
    /// "seconds since I pressed connect" would be nonsense in that case.
    /// </summary>
    private long CoverageStartMs =>
        _firstMessageMs == long.MaxValue ? SessionStartMs : Math.Min(SessionStartMs, _firstMessageMs);

    public void Reset()
    {
        _users.Clear();
        _chatLog.Clear();
        _chatTexts.Clear();
        _distinctScratch.Clear();
        _totalMessages = 0;
        _peakChatRate = 0;
        _firstMessageMs = long.MaxValue;
        SessionStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void Add(ChatMessage message)
    {
        if (!_users.TryGetValue(message.Login, out var acc))
        {
            acc = new UserAcc(message.Login, message.TimestampMs);
            _users[message.Login] = acc;
        }

        acc.DisplayName = message.DisplayName;
        if (message.ColorArgb != 0) acc.ColorArgb = message.ColorArgb;
        acc.IsMod |= message.IsMod;
        acc.IsVip |= message.IsVip;
        acc.IsSubscriber |= message.IsSubscriber;
        acc.LastMessageMs = Math.Max(acc.LastMessageMs, message.TimestampMs);

        var normalised = Normalise(message.Text);
        if (_chatTexts.TryGetValue(normalised, out var shared))
        {
            normalised = shared.Text; // reuse the one instance every chatter's map already points at
        }
        else
        {
            shared = new TextCount(normalised);
            _chatTexts[normalised] = shared;
        }

        shared.Count++;

        acc.Entries.AddLast(new Entry(message.TimestampMs, normalised));
        acc.Counts[normalised] = acc.Counts.GetValueOrDefault(normalised) + 1;

        // Counting is done on the normalised form, but the UI should show what was actually
        // typed — "LULW", not "lulw".
        acc.Samples.TryAdd(normalised, message.Text);
        acc.TotalMessages++;

        acc.Recent.AddLast((message.TimestampMs, message.Text));
        while (acc.Recent.Count > Config.RecentMessagesPerUser) acc.Recent.RemoveFirst();

        _chatLog.AddLast(new Entry(message.TimestampMs, normalised));
        _totalMessages++;
        if (message.TimestampMs < _firstMessageMs) _firstMessageMs = message.TimestampMs;
    }

    public Snapshot BuildSnapshot(ConnectionState connection, string channel, string? selectedLogin, long nowMs)
    {
        Trim(nowMs);

        var cfg = Config;
        var rateWindow = EffectiveRateWindowSec(nowMs, cfg);
        var rateCutoff = nowMs - cfg.RateWindowSec * 1000L;
        var scale = cfg.RateUnit.Seconds() / rateWindow;

        var rows = new List<ChatterRow>(_users.Count);
        var windowMessages = 0;
        var active = 0;

        foreach (var acc in _users.Values)
        {
            if (acc.Entries.Count == 0) continue;

            var recentCount = acc.Entries.Count - LowerBound(acc.Entries, rateCutoff);
            if (recentCount > 0) active++;
            windowMessages += acc.Entries.Count;

            rows.Add(new ChatterRow(
                acc.Login,
                acc.DisplayName,
                acc.ColorArgb,
                recentCount * scale,
                acc.Entries.Count,
                acc.Counts.Count,
                acc.Counts.Count * 100.0 / acc.Entries.Count,
                acc.TotalMessages,
                acc.FirstSeenMs,
                acc.LastMessageMs,
                acc.IsMod,
                acc.IsVip,
                acc.IsSubscriber,
                BuildSpark(acc.Entries, nowMs, cfg.RateWindowSec * 1000L, SparkBuckets)));
        }

        SortRows(rows, cfg.SortKey);

        var chatRate = (_chatLog.Count - LowerBound(_chatLog, rateCutoff)) * scale;
        if (chatRate > _peakChatRate) _peakChatRate = chatRate;

        // Chat-wide originality over its own trailing window: distinct lines anyone posted,
        // over every line anyone posted. Forty people each posting KEKW once is 1 distinct of 40.
        var originalityFrom = LowerBound(_chatLog, nowMs - cfg.OriginalityWindowSec * 1000L);
        var originalityCounted = _chatLog.Count - originalityFrom;
        _distinctScratch.Clear();
        for (var i = originalityFrom; i < _chatLog.Count; i++) _distinctScratch.Add(_chatLog[i].Norm);
        var originalityDistinct = _distinctScratch.Count;

        var global = new GlobalStats(
            _totalMessages,
            windowMessages,
            rows.Count,
            active,
            chatRate,
            _peakChatRate,
            originalityCounted == 0 ? 0 : originalityDistinct * 100.0 / originalityCounted,
            originalityDistinct,
            originalityCounted,
            CoverageStartMs,
            BuildSpark(_chatLog, nowMs, cfg.RateWindowSec * 1000L, GlobalSparkBuckets));

        ChatterDetail? detail = null;
        if (selectedLogin is not null && _users.TryGetValue(selectedLogin, out var selectedAcc))
        {
            var row = rows.FirstOrDefault(r => r.Login == selectedLogin);
            if (row is not null) detail = BuildDetail(selectedAcc, row, nowMs, cfg);
        }

        return new Snapshot(
            nowMs,
            cfg,
            connection,
            channel,
            global,
            rows.Count > cfg.MaxRows ? rows.GetRange(0, cfg.MaxRows) : rows,
            detail);
    }

    private static void SortRows(List<ChatterRow> rows, SortKey key)
    {
        Comparison<ChatterRow> comparison = key switch
        {
            // Ties on speed are extremely common (everyone at 0), so fall back to volume.
            SortKey.Rate => (a, b) => b.Rate.CompareTo(a.Rate) is var c and not 0
                ? c
                : b.WindowMessages.CompareTo(a.WindowMessages),
            SortKey.Messages => (a, b) => b.WindowMessages.CompareTo(a.WindowMessages) is var c and not 0
                ? c
                : b.Rate.CompareTo(a.Rate),
            SortKey.Unique => (a, b) => b.UniquePercent.CompareTo(a.UniquePercent) is var c and not 0
                ? c
                : b.WindowMessages.CompareTo(a.WindowMessages),
            _ => (a, b) => b.TotalMessages.CompareTo(a.TotalMessages) is var c and not 0
                ? c
                : b.Rate.CompareTo(a.Rate),
        };

        rows.Sort((a, b) => comparison(a, b) is var c and not 0
            ? c
            : string.CompareOrdinal(a.Login, b.Login));
    }

    private ChatterDetail BuildDetail(UserAcc acc, ChatterRow row, long nowMs, EngineConfig cfg)
    {
        var topRepeated = acc.Counts
            .Where(kv => kv.Value > 1)
            .OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => new RepeatedMessage(acc.Samples.GetValueOrDefault(kv.Key, kv.Key), kv.Value))
            .ToList();

        var recent = acc.Recent.ToList();
        recent.Reverse();

        return new ChatterDetail(
            row,
            topRepeated,
            recent,
            BuildSpark(acc.Entries, nowMs, DetailSpanMs, DetailBuckets),
            BucketPeak(acc.Entries, nowMs, DetailSpanMs, DetailBuckets),
            PeakRateOf(acc.Entries, cfg));
    }

    /// <summary>Slides the rate window across the user's history and keeps the highest count seen.</summary>
    private static double PeakRateOf(Deque<Entry> entries, EngineConfig cfg)
    {
        if (entries.Count == 0) return 0;

        var windowMs = cfg.RateWindowSec * 1000L;
        var scale = (double)cfg.RateUnit.Seconds() / cfg.RateWindowSec;
        var best = 0;
        var left = 0;

        for (var right = 0; right < entries.Count; right++)
        {
            while (entries[right].Ts - entries[left].Ts > windowMs) left++;
            var count = right - left + 1;
            if (count > best) best = count;
        }

        return best * scale;
    }

    private void Trim(long nowMs)
    {
        var cutoff = nowMs - Config.RetentionMs;
        var max = Config.MaxEntriesPerUser;

        var dead = new List<string>();
        foreach (var (login, acc) in _users)
        {
            while (acc.Entries.Count > 0 && (acc.Entries[0].Ts < cutoff || acc.Entries.Count > max))
            {
                var gone = acc.Entries.RemoveFirst();
                var left = acc.Counts.GetValueOrDefault(gone.Norm) - 1;
                if (left <= 0)
                {
                    acc.Counts.Remove(gone.Norm);
                    acc.Samples.Remove(gone.Norm);
                }
                else
                {
                    acc.Counts[gone.Norm] = left;
                }

                // The chat-wide table spans users, so it only forgets a text once the last
                // occurrence anywhere has left the window.
                if (_chatTexts.TryGetValue(gone.Norm, out var shared) && --shared.Count <= 0)
                {
                    _chatTexts.Remove(gone.Norm);
                }
            }

            // A user with nothing left in the window is not part of the tracker any more.
            if (acc.Entries.Count == 0) dead.Add(login);
        }

        foreach (var login in dead) _users.Remove(login);

        // The chat log only feeds the speed, the global sparkline and the originality window, so
        // it is trimmed to the widest of those rather than to the full retention period. Holding
        // 24h of it would cost hundreds of thousands of entries nothing reads.
        var chatLogSpanMs = Math.Max(Config.RateWindowSec, Config.OriginalityWindowSec) * 2_000L;
        var chatLogCutoff = nowMs - Math.Min(Config.RetentionMs, chatLogSpanMs);
        while (_chatLog.Count > 0 && _chatLog[0].Ts < chatLogCutoff) _chatLog.RemoveFirst();
    }

    /// <summary>
    /// Right after startup the full rate window has not elapsed yet; dividing by it would make
    /// every speed look artificially low for the first minute. We divide by whatever has actually
    /// been observed instead (floored, so the very first seconds do not explode).
    /// </summary>
    private double EffectiveRateWindowSec(long nowMs, EngineConfig cfg)
    {
        var observedSec = (nowMs - CoverageStartMs) / 1000.0;
        return Math.Clamp(observedSec, MinEffectiveWindowSec, cfg.RateWindowSec);
    }

    private static int LowerBound(Deque<Entry> entries, long cutoff)
    {
        int lo = 0, hi = entries.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (entries[mid].Ts < cutoff) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    private static float[] BuildSpark(Deque<Entry> entries, long nowMs, long spanMs, int buckets)
    {
        var result = new float[buckets];
        var start = nowMs - spanMs;
        var peak = 0f;

        for (var i = LowerBound(entries, start); i < entries.Count; i++)
        {
            var bucket = Math.Clamp((int)((entries[i].Ts - start) / (double)spanMs * buckets), 0, buckets - 1);
            result[bucket] += 1f;
            if (result[bucket] > peak) peak = result[bucket];
        }

        if (peak > 0) for (var b = 0; b < buckets; b++) result[b] /= peak;
        return result;
    }

    /// <summary>Raw message count of the busiest bucket, so the normalised chart can be labelled.</summary>
    private static int BucketPeak(Deque<Entry> entries, long nowMs, long spanMs, int buckets)
    {
        var counts = new int[buckets];
        var start = nowMs - spanMs;
        var peak = 0;

        for (var i = LowerBound(entries, start); i < entries.Count; i++)
        {
            var bucket = Math.Clamp((int)((entries[i].Ts - start) / (double)spanMs * buckets), 0, buckets - 1);
            counts[bucket]++;
            if (counts[bucket] > peak) peak = counts[bucket];
        }

        return peak;
    }

    /// <summary>
    /// Two messages count as the same when they only differ in case, spacing, or the invisible
    /// padding spammers append to slip past Twitch's own duplicate filter (U+E0000 tag block,
    /// zero-width joiners, BOM). Without this, "KEKW" and "KEKW\U000E0000" would look original.
    /// </summary>
    public static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var rune in text.EnumerateRunes())
        {
            var cp = rune.Value;
            var invisible = cp is >= 0xE0000 and <= 0xE007F
                or >= 0x200B and <= 0x200F
                or 0xFEFF or 0x2060 or 0x00AD;
            if (invisible) continue;

            if (Rune.IsWhiteSpace(rune))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(rune);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim().ToLower(CultureInfo.InvariantCulture);
    }

    private readonly record struct Entry(long Ts, string Norm);

    /// <summary>A distinct message text and how many times it appears anywhere in the window.</summary>
    private sealed class TextCount(string text)
    {
        public string Text { get; } = text;

        public int Count;
    }

    private sealed class UserAcc(string login, long firstSeenMs)
    {
        public string Login { get; } = login;
        public long FirstSeenMs { get; } = firstSeenMs;
        public string DisplayName { get; set; } = login;
        public int ColorArgb { get; set; }
        public bool IsMod { get; set; }
        public bool IsVip { get; set; }
        public bool IsSubscriber { get; set; }
        public long TotalMessages { get; set; }
        public long LastMessageMs { get; set; } = firstSeenMs;

        public Deque<Entry> Entries { get; } = new();

        /// <summary>normalised text -> how many times it appears in the window</summary>
        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        /// <summary>normalised text -> the first raw spelling we saw, for display only</summary>
        public Dictionary<string, string> Samples { get; } = new(StringComparer.Ordinal);

        public Deque<(long Ts, string Text)> Recent { get; } = new();
    }
}
