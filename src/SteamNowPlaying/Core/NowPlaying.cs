namespace SteamNowPlaying.Core;

/// <summary>What Spotify reports as currently playing.</summary>
/// <param name="Title">Track or episode name.</param>
/// <param name="Artist">Artists joined with ", ", or the show name for podcasts.</param>
/// <param name="Album">Album name, or the podcast publisher.</param>
/// <param name="IsPlaying">False when paused.</param>
/// <param name="Kind">Spotify's type: "track", "episode", ...</param>
public sealed record NowPlaying(string Title, string Artist, string Album, bool IsPlaying, string Kind);
