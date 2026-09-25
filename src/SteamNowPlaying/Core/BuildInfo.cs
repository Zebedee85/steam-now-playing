using System.Reflection;

namespace SteamNowPlaying.Core;

public static class BuildInfo
{
    public const string AppName = "Steam Now Playing";
    public const string RepoUrl = "https://github.com/Zebedee85/steam-now-playing";

    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev";

    /// <summary>Spotify Client ID baked in at build time, if any.</summary>
    public static string? DefaultSpotifyClientId { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SpotifyClientId")?.Value is { Length: > 0 } id ? id.Trim() : null;
}
