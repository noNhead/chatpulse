using System.IO;
using System.Runtime.InteropServices;
using ChatPulse.Core;
using ChatPulse.Tools;

namespace ChatPulse;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var cli = CliArgs.Parse(args);

        if (args.Contains("--help") || args.Contains("-h"))
        {
            WithConsole(() => Console.WriteLine(CliArgs.Help));
            return 0;
        }

        if (cli.IconPath is { } iconPath)
        {
            // Generated from the same geometry as the in-app logo, so the repository stays free of
            // binary assets and the two marks cannot drift apart. Called by the build script.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(iconPath))!);
            File.WriteAllBytes(iconPath, Ui.AppIcon.BuildIco(16, 32, 48, 64, 128, 256));
            WithConsole(() => Console.WriteLine($"[icon] wrote {Path.GetFullPath(iconPath)}"));
            return 0;
        }

        if (cli.SelfTestChannel is { } channel)
        {
            var exitCode = 1;
            WithConsole(() => exitCode = SelfTest.Run(channel).GetAwaiter().GetResult());
            return exitCode;
        }

        App.Cli = cli;
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// This is a WinExe, so it has no console of its own. Console modes attach to the parent's
    /// console when there is one (launched from a terminal) and allocate one otherwise, so
    /// <c>--selftest</c> and <c>--help</c> print somewhere visible either way.
    /// </summary>
    private static void WithConsole(Action body)
    {
        var attached = AttachConsole(AttachParentProcess);
        var allocated = !attached && AllocConsole();
        try
        {
            body();
            if (allocated)
            {
                Console.WriteLine();
                Console.Write("Press Enter to close...");
                Console.ReadLine();
            }
        }
        finally
        {
            if (attached || allocated) FreeConsole();
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
}
