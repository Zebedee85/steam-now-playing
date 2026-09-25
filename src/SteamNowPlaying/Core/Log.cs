namespace SteamNowPlaying.Core;

/// <summary>
/// Small text log in the settings folder, for troubleshooting. Never pass tokens, codes
/// or passwords to it.
/// </summary>
public static class Log
{
    const long MaxBytes = 512 * 1024;
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppSettings.Folder, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
