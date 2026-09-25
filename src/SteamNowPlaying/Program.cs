using System.Diagnostics;
using SteamNowPlaying.Core;
using SteamNowPlaying.UI;

namespace SteamNowPlaying;

static class Program
{
    /// <summary>Broadcast to bring the running copy's window forward.</summary>
    internal static readonly int ShowMeMessage = NativeMethods.RegisterWindowMessage("SteamNowPlaying.ShowMe.b41c");

    [STAThread]
    static int Main(string[] args)
    {
        var detach = args.Any(a => a.Equals("--detach", StringComparison.OrdinalIgnoreCase));
        var minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        // When launched from a Steam library shortcut, restart outside Steam's process tree so
        // Steam doesn't also show its own "In non-Steam game" status over ours.
        if (detach && RelaunchViaExplorer()) return 0;

        using var mutex = new Mutex(initiallyOwned: true, @"Local\SteamNowPlaying.b41c", out var firstInstance);
        if (!firstInstance)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowMeMessage, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("UI error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal: " + e.ExceptionObject);

        ApplicationConfiguration.Initialize();
        Log.Write($"Starting {BuildInfo.AppName} {BuildInfo.Version}");
        StartupRegistration.RefreshPathIfEnabled();

        var settings = AppSettings.Load();
        Application.Run(new MainForm(settings, startHidden: minimized));
        return 0;
    }

    static bool RelaunchViaExplorer()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return false;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
