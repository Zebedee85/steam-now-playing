namespace SteamNowPlaying.Core;

static class FileUtil
{
    /// <summary>Writes via a temp file so a crash never leaves a half-written file behind.</summary>
    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
