using System.Text;
using SteamNowPlaying.Steam;

namespace SteamNowPlaying.Core;

/// <summary>Everything the window and tray need to describe what's on the Steam profile right now.</summary>
public sealed record StatusSnapshot(
    bool Paused,
    SteamState SteamState,
    bool HasSavedSteamLogin,
    bool SpotifyConnected,
    bool PlayingBlocked,
    string? StatusText,
    NowPlaying? Track,
    string? SpotifyProblem);

public enum LiveState { Paused, ConnectingSteam, SignInToSteam, ConnectSpotify, InGame, ShowingTrack, ShowingIdleText, NothingPlaying }

/// <summary>Turns a snapshot into the header text and the tray tooltip.</summary>
public static class StatusSummary
{
    /// <summary>NotifyIcon.Text throws above this length (.NET 6 and later).</summary>
    public const int MaxTrayText = 127;

    // Two full lines of song plus the app name still fit under the limit.
    const int MaxTrayLine = 52;

    public static LiveState Classify(StatusSnapshot s)
    {
        if (s.Paused) return LiveState.Paused;
        if (s.SteamState != SteamState.Online) return s.HasSavedSteamLogin ? LiveState.ConnectingSteam : LiveState.SignInToSteam;
        if (!s.SpotifyConnected) return LiveState.ConnectSpotify;
        if (s.PlayingBlocked) return LiveState.InGame;
        if (s.StatusText is not null) return s.Track?.IsPlaying == true ? LiveState.ShowingTrack : LiveState.ShowingIdleText;
        return LiveState.NothingPlaying;
    }

    /// <summary>The big line and the grey line under it in the main window.</summary>
    public static (string Status, string Sub) Header(StatusSnapshot s) => Classify(s) switch
    {
        LiveState.Paused => ("Paused", "Sharing is paused, so your Steam status is left alone."),
        LiveState.ConnectingSteam => ("Not live", "Connecting to Steam…"),
        LiveState.SignInToSteam => ("Not live", "Sign in to Steam below to start."),
        LiveState.ConnectSpotify => ("Not live", "Connect Spotify below to start."),
        LiveState.InGame => ("In a game", "You're playing something on Steam. Your song comes back when you stop."),
        LiveState.ShowingTrack => (s.StatusText!, "Showing now."),
        LiveState.ShowingIdleText => (s.StatusText!, "Showing while nothing's playing."),
        _ => ("Nothing playing", "Your normal Steam status is showing."),
    };

    /// <summary>Tray tooltip: the app name, then the song (or what's going on) on the lines below.</summary>
    public static string TrayText(StatusSnapshot s)
    {
        var lines = new List<string> { BuildInfo.AppName };
        switch (Classify(s))
        {
            case LiveState.ShowingTrack:
                lines.Add(s.Track!.Title);
                if (s.Track.Artist.Length > 0) lines.Add(s.Track.Artist);
                break;
            case LiveState.ShowingIdleText:
                lines.Add("Showing: " + s.StatusText);
                break;
            case LiveState.Paused:
                lines.Add("Sharing paused");
                break;
            case LiveState.ConnectingSteam:
                lines.Add("Connecting to Steam…");
                break;
            case LiveState.SignInToSteam:
                lines.Add("Not live: sign in to Steam");
                break;
            case LiveState.ConnectSpotify:
                lines.Add("Not live: connect Spotify");
                break;
            case LiveState.InGame:
                lines.Add("In a game, song hidden");
                break;
            default:
                lines.Add("Nothing playing");
                break;
        }

        if (s.SpotifyProblem is not null && !s.Paused) lines.Add("Spotify needs attention");

        return Fit(lines);
    }

    /// <summary>Cleans and shortens each line, dropping trailing lines if the whole still won't fit.</summary>
    static string Fit(List<string> lines)
    {
        var cleaned = lines.Select(l => Shorten(OneLine(l), MaxTrayLine)).Where(l => l.Length > 0).ToList();
        var text = string.Join("\n", cleaned);
        while (text.Length > MaxTrayText && cleaned.Count > 1)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
            text = string.Join("\n", cleaned);
        }
        return Shorten(text, MaxTrayText);
    }

    static string OneLine(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString().Trim();
    }

    static string Shorten(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = max - 1;
        if (char.IsHighSurrogate(s[cut - 1])) cut--; // don't split an emoji
        return s[..cut].TrimEnd() + "…";
    }
}
