using ChatPulse.Chat;

namespace ChatPulse.Tools;

/// <summary>
/// <c>--selftest [channel]</c>: proves the build can actually reach Twitch.
/// <para>
/// Worth its own mode because a published single-file build carries its own runtime and TLS
/// stack; a UI that starts but silently never connects is nearly impossible to diagnose from the
/// outside. This exercises the real socket and reports what happened.
/// </para>
/// <para>Read-only and anonymous, exactly like normal operation: it joins, listens, and leaves.</para>
/// </summary>
public static class SelfTest
{
    /// <summary>Ceiling on the whole run, so a dead network fails the check instead of hanging it.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    /// <summary>How long to stay in the room after joining, listening for real traffic.</summary>
    private static readonly TimeSpan Listen = TimeSpan.FromSeconds(6);

    public static async Task<int> Run(string channel)
    {
        Console.WriteLine("ChatPulse self-test");
        Console.WriteLine($"  runtime  = {Environment.Version}");
        Console.WriteLine($"  channel  = #{channel}");

        var messages = 0;
        var joined = false;
        var lastLog = string.Empty;
        string? failure = null;

        using var cts = new CancellationTokenSource(Timeout);

        try
        {
            var source = new TwitchChatSource(channel);
            var run = source.RunAsync(e =>
            {
                switch (e)
                {
                    case ChatEvent.Status(ConnectionState.Live):
                        joined = true;
                        break;
                    case ChatEvent.Message:
                        Interlocked.Increment(ref messages);
                        break;
                    case ChatEvent.Log(var text):
                        lastLog = text;
                        // ASCII only: an attached Windows console is rarely on a UTF-8 code page.
                        Console.WriteLine($"  > {text}");
                        break;
                }

                return Task.CompletedTask;
            }, cts.Token);

            while (!joined && !cts.IsCancellationRequested) await Task.Delay(200, cts.Token);

            // The join alone already proves the socket and the TLS stack; do not hang waiting for
            // traffic on a quiet channel.
            var until = DateTime.UtcNow + Listen;
            while (DateTime.UtcNow < until && messages < 5 && !cts.IsCancellationRequested)
            {
                await Task.Delay(200, cts.Token);
            }

            await cts.CancelAsync();
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
                // Expected: we asked it to stop.
            }
        }
        catch (OperationCanceledException)
        {
            if (!joined) failure = $"timed out after {Timeout.TotalSeconds:0}s without joining";
        }
        catch (Exception e)
        {
            failure = $"{e.GetType().Name}: {e.Message}";
        }

        Console.WriteLine();

        if (failure is not null || !joined)
        {
            Console.WriteLine($"FAILED - {failure ?? "never joined the channel"}");
            if (lastLog.Length > 0) Console.WriteLine($"last event: {lastLog}");
            return 1;
        }

        var traffic = messages == 0 ? "channel was quiet" : $"read {messages} message(s)";
        Console.WriteLine($"OK - TLS handshake succeeded, joined #{channel}, {traffic}");
        return 0;
    }
}
