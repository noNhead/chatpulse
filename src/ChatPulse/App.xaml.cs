using System.Windows;
using ChatPulse.Core;
using ChatPulse.Tools;
using ChatPulse.Ui;

namespace ChatPulse;

public partial class App : Application
{
    /// <summary>
    /// Set by <see cref="Program"/> before the application starts. A property rather than a
    /// constructor argument because the XAML compiler generates its own parameterless
    /// instantiation, which must still compile even though StartupObject bypasses it.
    /// </summary>
    public static CliArgs Cli { get; set; } = new();

    private AppState? _state;

    public App()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _state = new AppState(Cli, Dispatcher);

        if (Cli.ScreenshotDir is { } dir)
        {
            // Renders offscreen and quits: no window is ever shown, so nothing on the desktop can
            // cover it and the output is identical run to run.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunScreenshotsAsync(dir, _state);
            return;
        }

        var window = new MainWindow(_state);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// CI runs this mode as a smoke test, so a failure has to end the process with a non-zero exit
    /// code — left to the discarded task, it would keep an invisible app running until the job
    /// times out.
    /// </summary>
    private async Task RunScreenshotsAsync(string dir, AppState state)
    {
        try
        {
            await ScreenshotHarness.RunAsync(dir, state, () => Shutdown(0));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[screenshot] failed: {e}");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Dispose();
        base.OnExit(e);
    }
}
