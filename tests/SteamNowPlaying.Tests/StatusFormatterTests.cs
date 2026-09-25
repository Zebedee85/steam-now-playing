using SteamNowPlaying.Core;
using Xunit;

namespace SteamNowPlaying.Tests;

public class StatusFormatterTests
{
    static NowPlaying Track(string title = "Fire Water Burn", string artist = "Bloodhound Gang", string album = "One Fierce Beer Coaster", bool playing = true) =>
        new(title, artist, album, playing, "track");

    [Fact]
    public void Default_template_shows_track_and_artist()
    {
        Assert.Equal("Listening to Fire Water Burn • Bloodhound Gang", StatusFormatter.Format(Track(), null, null));
    }

    [Fact]
    public void Paused_with_no_idle_text_clears_status()
    {
        Assert.Null(StatusFormatter.Format(Track(playing: false), null, ""));
        Assert.Null(StatusFormatter.Format(null, null, "   "));
    }

    [Fact]
    public void Paused_with_idle_text_shows_it()
    {
        Assert.Equal("Taking a break", StatusFormatter.Format(Track(playing: false), null, "  Taking a break "));
    }

    [Fact]
    public void Custom_template_placeholders_are_case_insensitive()
    {
        Assert.Equal("♪ Fire Water Burn (One Fierce Beer Coaster)",
            StatusFormatter.Format(Track(), "♪ {TRACK} ({Album})", null));
    }

    [Fact]
    public void Missing_artist_does_not_leave_a_dangling_separator()
    {
        Assert.Equal("Listening to Some Local File", StatusFormatter.Format(Track(title: "Some Local File", artist: ""), null, null));
    }

    [Fact]
    public void Long_text_is_truncated_with_ellipsis()
    {
        var text = StatusFormatter.Format(Track(title: new string('a', 300)), null, null)!;
        Assert.Equal(StatusFormatter.MaxLength, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Truncation_never_splits_an_emoji()
    {
        var title = new string('a', StatusFormatter.MaxLength - 14) + string.Concat(Enumerable.Repeat("😀", 20));
        var text = StatusFormatter.Format(Track(title: title, artist: ""), "{track}", null)!;
        Assert.False(char.IsHighSurrogate(text[^2]));
        Assert.True(text.Length <= StatusFormatter.MaxLength);
    }

    [Fact]
    public void Whitespace_is_collapsed()
    {
        Assert.Equal("Listening to A B • C", StatusFormatter.Format(Track(title: "A \n  B", artist: "C"), null, null));
    }
}
