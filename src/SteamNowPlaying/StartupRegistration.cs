using Microsoft.Win32;
using SteamNowPlaying.Core;

namespace SteamNowPlaying;

/// <summary>"Start with Windows" via the current user's Run key (no admin rights needed).</summary>
static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SteamNowPlaying";

    static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Write("Couldn't change start-with-Windows: " + ex.Message);
        }
    }

    /// <summary>If the exe was moved, point the startup entry at the new location.</summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && current != Command)
            {
                key.SetValue(ValueName, Command);
            }
        }
        catch
        {
            // Not important enough to bother the user.
        }
    }
}
