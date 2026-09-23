using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ChatPulse.Core;
using ChatPulse.Ui;

namespace ChatPulse.Tools;

/// <summary>
/// <c>--screenshot &lt;dir&gt;</c>: drives the UI through its main states and writes a PNG of each,
/// then quits.
/// <para>
/// Uses <see cref="RenderTargetBitmap"/>, which walks the visual tree rather than grabbing the
/// screen. The windows are positioned off-screen and never shown to the user, so nothing on the
/// desktop can cover them and the output is identical run to run.
/// </para>
/// </summary>
public static class ScreenshotHarness
{
    /// <summary>Parks the windows outside every plausible monitor arrangement.</summary>
    private const double OffScreen = -32000;

    public static async Task RunAsync(string outDir, AppState state, Action shutdown)
    {
        var dir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(dir);

        var window = new MainWindow(state)
        {
            Left = OffScreen,
            Top = OffScreen,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();

        // Let the demo feed backfill and a couple of snapshots tick through.
        await Settle(window, 3000);
        Capture(window, Path.Combine(dir, "01-dashboard.png"));

        var loudest = state.Snapshot.Rows.FirstOrDefault()?.Login;
        if (loudest is not null)
        {
            state.SelectedLogin = loudest;
            await Settle(window, 900);
            Capture(window, Path.Combine(dir, "02-chatter-detail.png"));

            // The retention caps are easy to regress and hard to see in a still, so state them.
            var detail = state.Snapshot.Detail;
            Console.WriteLine(
                $"[screenshot] {loudest}: counted={detail?.Row.WindowMessages} " +
                $"log={detail?.RecentMessages.Count} (cap {state.Settings.MessageLogPerUser}, " +
                $"history cap {state.Settings.MaxEntriesPerUser})");
        }

        var original = state.Snapshot.Rows
            .Where(r => r.WindowMessages > 30)
            .OrderByDescending(r => r.UniquePercent)
            .FirstOrDefault()?.Login;
        if (original is not null && original != loudest)
        {
            state.SelectedLogin = original;
            await Settle(window, 900);
            Capture(window, Path.Combine(dir, "03-original-detail.png"));
        }

        state.SelectedLogin = null;
        state.SettingsOpen = true;
        await Settle(window, 700);
        Capture(window, Path.Combine(dir, "04-settings.png"));
        state.SettingsOpen = false;

        var overlay = new OverlayWindow(state)
        {
            Left = OffScreen,
            Top = OffScreen,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
        };
        overlay.Show();
        overlay.Apply(state.Snapshot, state.Settings);
        await Settle(overlay, 900);
        // Composited onto the app background: a translucent card has nothing behind it in a PNG.
        Capture(overlay, Path.Combine(dir, "05-overlay.png"), Color.FromRgb(0x07, 0x07, 0x0B));

        Console.WriteLine($"[screenshot] wrote {Directory.GetFiles(dir, "*.png").Length} PNG(s) to {dir}");

        overlay.Close();
        window.Close();
        shutdown();
    }

    /// <summary>Waits for data to arrive and for WPF to finish laying the tree out.</summary>
    private static async Task Settle(Window window, int delayMs)
    {
        await Task.Delay(delayMs);
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void Capture(Window window, string path, Color? background = null)
    {
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        if (width <= 0 || height <= 0) return;

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        if (background is { } colour)
        {
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(colour), null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, width, height));
            }

            target.Render(backdrop);
        }
        else
        {
            target.Render(window);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);

        Console.WriteLine($"[screenshot] {Path.GetFileName(path)} {width}x{height}");
    }
}
