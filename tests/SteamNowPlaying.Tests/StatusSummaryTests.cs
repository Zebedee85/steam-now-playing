using SteamNowPlaying.Core;
using SteamNowPlaying.Steam;
using Xunit;

namespace SteamNowPlaying.Tests;

public class StatusSummaryTests
{
    static readonly NowPlaying Song = new("Fire Water Burn", "Bloodhound Gang", "One Fierce Beer Coaster", true, "track");

    static StatusSnapshot Live(
        NowPlaying? track = null,
        string? statusText = "Listening to Fire Water Burn • Bloodhound Gang",
        bool paused = false,
        SteamState steam = SteamState.Online,
        bool savedLogin = true,
        bool spotify = true,
        bool inGame = false,
        string? problem = null) =>
        new(paused, steam, savedLogin, spotify, inGame, statusText, track ?? Song, problem);

    [Theory]
    [InlineData(SteamState.Connecting, true, LiveState.ConnectingSteam)]
    [InlineData(SteamState.Reconnecting, true, LiveState.ConnectingSteam)]
    [InlineData(SteamState.SignedOut, false, LiveState.SignInToSteam)]
    [InlineData(SteamState.WaitingForQr, false, LiveState.SignInToSteam)]
    public void Steam_not_online_is_not_live(SteamState state, bool savedLogin, LiveState expected)
    {
        Assert.Equal(expected, StatusSummary.Classify(Live(steam: state, savedLogin: savedLogin)));
    }

    [Fact]
    public void Paused_wins_over_everything()
    {
        Assert.Equal(LiveState.Paused, StatusSummary.Classify(Live(paused: true, steam: SteamState.Error, spotify: false)));
    }

    [Fact]
    public void Classifies_the_live_states()
    {
        Assert.Equal(LiveState.ConnectSpotify, StatusSummary.Classify(Live(spotify: false)));
        Assert.Equal(LiveState.InGame, StatusSummary.Classify(Live(inGame: true)));
        Assert.Equal(LiveState.ShowingTrack, StatusSummary.Classify(Live()));
        Assert.Equal(LiveState.ShowingIdleText, StatusSummary.Classify(Live(track: Song with { IsPlaying = false }, statusText: "AFK")));
        Assert.Equal(LiveState.NothingPlaying, StatusSummary.Classify(Live(statusText: null)));
    }

    [Fact]
    public void Header_shows_the_status_text_while_live()
    {
        var (status, sub) = StatusSummary.Header(Live());
        Assert.Equal("Listening to Fire Water Burn • Bloodhound Gang", status);
        Assert.Equal("Showing now.", sub);
    }

    [Fact]
    public void Tray_shows_track_and_artist_on_their_own_lines()
    {
        Assert.Equal("Steam Now Playing\nFire Water Burn\nBloodhound Gang", StatusSummary.TrayText(Live()));
    }

    [Fact]
    public void Tray_skips_an_empty_artist()
    {
        var local = Song with { Artist = "" };
        Assert.Equal("Steam Now Playing\nFire Water Burn", StatusSummary.TrayText(Live(track: local)));
    }

    [Fact]
    public void Tray_describes_non_live_states()
    {
        Assert.Equal("Steam Now Playing\nSharing paused", StatusSummary.TrayText(Live(paused: true)));
        Assert.Equal("Steam Now Playing\nIn a game, song hidden", StatusSummary.TrayText(Live(inGame: true)));
        Assert.Equal("Steam Now Playing\nNot live: connect Spotify", StatusSummary.TrayText(Live(spotify: false)));
        Assert.Equal("Steam Now Playing\nShowing: AFK", StatusSummary.TrayText(Live(track: Song with { IsPlaying = false }, statusText: "AFK")));
    }

    [Fact]
    public void Tray_flags_a_spotify_problem()
    {
        Assert.EndsWith("\nSpotify needs attention", StatusSummary.TrayText(Live(problem: "Can't reach Spotify.")));
    }

    [Fact]
    public void Tray_text_never_exceeds_the_windows_limit()
    {
        var huge = Song with { Title = new string('t', 500), Artist = new string('a', 500) };
        var text = StatusSummary.TrayText(Live(track: huge, problem: "x"));
        Assert.True(text.Length <= StatusSummary.MaxTrayText, $"length {text.Length}");
        Assert.StartsWith("Steam Now Playing\n", text);
        Assert.Equal(3, text.Split('\n').Length); // the problem line gives way to the artist
    }

    [Fact]
    public void Tray_text_does_not_split_an_emoji()
    {
        var emoji = Song with { Title = new string('a', 50) + "😀😀😀" };
        var line = StatusSummary.TrayText(Live(track: emoji)).Split('\n')[1];
        Assert.EndsWith("…", line);
        Assert.False(char.IsHighSurrogate(line[^2]));
    }

    [Fact]
    public void Tray_text_strips_control_characters_from_titles()
    {
        var odd = Song with { Title = "Line\r\nBreak\tTab" };
        Assert.Equal("Steam Now Playing\nLine  Break Tab\nBloodhound Gang", StatusSummary.TrayText(Live(track: odd)));
    }
}
