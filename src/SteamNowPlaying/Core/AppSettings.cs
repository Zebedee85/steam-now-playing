using System.Text.Json;

namespace SteamNowPlaying.Core;

/// <summary>Non-secret settings, stored as JSON in %APPDATA%\SteamNowPlaying.</summary>
public sealed class AppSettings
{
    public string StatusTemplate { get; set; } = StatusFormatter.DefaultTemplate;

    /// <summary>Shown when nothing is playing. Empty clears the status instead.</summary>
    public string IdleText { get; set; } = "";

    /// <summary>Set the account to Online when signing in (off for people who use Invisible).</summary>
    public bool GoOnline { get; set; } = true;

    /// <summary>Overrides the Client ID built into the app. Empty uses the built-in one.</summary>
    public string SpotifyClientId { get; set; } = "";

    /// <summary>
    /// Unique per install. Steam only allows one session per LoginID from the same IP, and the
    /// default LoginID matches the real Steam client's, which would sign that client out.
    /// </summary>
    public uint LoginId { get; set; }

    public bool Paused { get; set; }

    public bool TrayHintShown { get; set; }

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamNowPlaying");

    static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
            }
        }
        catch (Exception ex)
        {
            Log.Write("Settings unreadable, using defaults: " + ex.Message);
        }

        settings ??= new AppSettings();
        if (settings.LoginId == 0)
        {
            settings.LoginId = (uint)Random.Shared.Next(0x10000, int.MaxValue);
            settings.Save();
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            FileUtil.WriteAllBytesAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("Couldn't save settings: " + ex.Message);
        }
    }
}
