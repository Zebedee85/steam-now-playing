using System.Security.Cryptography;
using System.Text.Json;

namespace SteamNowPlaying.Core;

/// <summary>
/// Sign-in tokens, encrypted with Windows DPAPI for the current Windows user.
/// Another user on the PC, or a copy of the file on another PC, can't decrypt them.
/// </summary>
public static class SecretStore
{
    static readonly byte[] Entropy = "SteamNowPlaying/secrets/v1"u8.ToArray();

    static string PathFor(string name) => Path.Combine(AppSettings.Folder, name + ".dat");

    public static T? Load<T>(string name) where T : class
    {
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex)
        {
            Log.Write($"Stored {name} sign-in unreadable, ignoring it: {ex.GetType().Name}");
            return null;
        }
    }

    public static void Save<T>(string name, T value) where T : class
    {
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(value);
            var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            FileUtil.WriteAllBytesAtomic(PathFor(name), cipher);
        }
        catch (Exception ex)
        {
            Log.Write($"Couldn't save {name} sign-in: {ex.GetType().Name}");
        }
    }

    public static void Delete(string name)
    {
        try { File.Delete(PathFor(name)); }
        catch { /* already gone */ }
    }
}
