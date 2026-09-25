using System.Text.RegularExpressions;

namespace SteamNowPlaying.Core;

/// <summary>Turns a track into the text shown on Steam.</summary>
public static partial class StatusFormatter
{
    public const string DefaultTemplate = "Listening to {track} • {artist}";

    /// <summary>Longest status we send. Steam truncates long names anyway.</summary>
    public const int MaxLength = 100;

    static readonly char[] TrailingJunk = [' ', '•', '·', '-', '–', '—', '|', ',', ':', '/'];

    /// <summary>
    /// Returns the status text, or null to clear the status.
    /// Placeholders: {track}, {artist}, {album} (case-insensitive).
    /// </summary>
    public static string? Format(NowPlaying? track, string? template, string? idleText)
    {
        if (track is null || !track.IsPlaying)
        {
            return Clean(idleText);
        }

        var t = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;
        var text = t
            .Replace("{track}", track.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", track.Artist, StringComparison.OrdinalIgnoreCase)
            .Replace("{album}", track.Album, StringComparison.OrdinalIgnoreCase);

        return Clean(text);
    }

    static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        s = Whitespace().Replace(s, " ").Trim().TrimEnd(TrailingJunk).Trim();
        if (s.Length == 0) return null;

        if (s.Length > MaxLength)
        {
            var cut = MaxLength - 1;
            if (char.IsHighSurrogate(s[cut - 1])) cut--; // don't split an emoji
            s = s[..cut].TrimEnd() + "…";
        }

        return s;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
