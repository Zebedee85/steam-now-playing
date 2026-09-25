using SteamNowPlaying.Spotify;
using Xunit;

namespace SteamNowPlaying.Tests;

public class SpotifyParsingTests
{
    [Fact]
    public void Parses_a_playing_track_with_several_artists()
    {
        const string json = """
            {"is_playing":true,"currently_playing_type":"track",
             "item":{"name":"Under Pressure","artists":[{"name":"Queen"},{"name":"David Bowie"}],"album":{"name":"Hot Space"}}}
            """;
        var np = SpotifyClient.ParseCurrentlyPlaying(json)!;
        Assert.Equal("Under Pressure", np.Title);
        Assert.Equal("Queen, David Bowie", np.Artist);
        Assert.Equal("Hot Space", np.Album);
        Assert.True(np.IsPlaying);
        Assert.Equal("track", np.Kind);
    }

    [Fact]
    public void Paused_track_is_not_playing()
    {
        const string json = """{"is_playing":false,"currently_playing_type":"track","item":{"name":"X","artists":[{"name":"Y"}]}}""";
        Assert.False(SpotifyClient.ParseCurrentlyPlaying(json)!.IsPlaying);
    }

    [Fact]
    public void Podcast_episode_uses_show_name_as_artist()
    {
        const string json = """
            {"is_playing":true,"currently_playing_type":"episode",
             "item":{"name":"Episode 12","show":{"name":"The Rest Is History","publisher":"Goalhanger"}}}
            """;
        var np = SpotifyClient.ParseCurrentlyPlaying(json)!;
        Assert.Equal("Episode 12", np.Title);
        Assert.Equal("The Rest Is History", np.Artist);
        Assert.Equal("Goalhanger", np.Album);
        Assert.Equal("episode", np.Kind);
    }

    [Fact]
    public void Ads_and_empty_bodies_mean_nothing_playing()
    {
        Assert.Null(SpotifyClient.ParseCurrentlyPlaying("""{"is_playing":true,"currently_playing_type":"ad","item":null}"""));
        Assert.Null(SpotifyClient.ParseCurrentlyPlaying(""));
    }

    [Fact]
    public void Token_response_keeps_old_refresh_token_when_spotify_omits_it()
    {
        var now = DateTimeOffset.UnixEpoch;
        var t = SpotifyClient.ParseTokenResponse("cid", """{"access_token":"A2","expires_in":3600}""", "R1", now);
        Assert.Equal("A2", t.AccessToken);
        Assert.Equal("R1", t.RefreshToken);
        Assert.Equal(now.AddSeconds(3540), t.ExpiresAt);
    }

    [Fact]
    public void Token_response_uses_new_refresh_token_when_rotated()
    {
        var t = SpotifyClient.ParseTokenResponse("cid", """{"access_token":"A","refresh_token":"R2","expires_in":3600}""", "R1", DateTimeOffset.UtcNow);
        Assert.Equal("R2", t.RefreshToken);
    }

    [Fact]
    public void Token_response_without_access_token_throws()
    {
        Assert.Throws<SpotifyAuthException>(() => SpotifyClient.ParseTokenResponse("cid", "{}", "R1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Forbidden_message_explains_the_allowlist_and_includes_spotify_detail()
    {
        var msg = SpotifyClient.ForbiddenMessage("""{"error":{"status":403,"message":"User not registered in the Developer Dashboard"}}""");
        Assert.Contains("own Client ID", msg);
        Assert.Contains("User not registered", msg);
    }

    [Fact]
    public void Pkce_challenge_matches_rfc7636_example()
    {
        // RFC 7636 appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Pkce_verifier_is_url_safe_and_long_enough()
    {
        var v = Pkce.CreateVerifier();
        Assert.Equal(64, v.Length);
        Assert.True(v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'));
    }
}
