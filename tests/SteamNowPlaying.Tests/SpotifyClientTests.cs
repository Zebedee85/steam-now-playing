using System.Net;
using System.Text;
using SteamNowPlaying.Core;
using SteamNowPlaying.Spotify;
using Xunit;

namespace SteamNowPlaying.Tests;

/// <summary>Exercises polling, token refresh and error handling against a fake Spotify.</summary>
public sealed class SpotifyClientTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "snp-tests-" + Guid.NewGuid().ToString("N"));
    readonly string originalFolder = AppSettings.Folder;

    public SpotifyClientTests()
    {
        AppSettings.Folder = folder;
    }

    public void Dispose()
    {
        AppSettings.Folder = originalFolder;
        try { Directory.Delete(folder, recursive: true); } catch { }
    }

    static SpotifyTokens Tokens(DateTimeOffset? expires = null) =>
        new("cid", "ACCESS1", "REFRESH1", expires ?? DateTimeOffset.UtcNow.AddHours(1));

    SpotifyClient ClientWith(FakeSpotify fake, SpotifyTokens? tokens)
    {
        if (tokens is not null) SecretStore.Save("spotify", tokens);
        return new SpotifyClient("cid", fake);
    }

    const string PlayingJson = """{"is_playing":true,"currently_playing_type":"track","item":{"name":"Song","artists":[{"name":"Band"}],"album":{"name":"LP"}}}""";

    [Fact]
    public async Task Not_connected_without_saved_tokens()
    {
        using var client = ClientWith(new FakeSpotify(), null);
        Assert.False(client.IsConnected);
        Assert.Equal(SpotifyPollStatus.NotConnected, (await client.PollAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Saved_tokens_for_a_different_client_id_are_ignored()
    {
        SecretStore.Save("spotify", Tokens() with { ClientId = "other" });
        using var client = new SpotifyClient("cid", new FakeSpotify());
        Assert.False(client.IsConnected);
        Assert.Equal(SpotifyPollStatus.NotConnected, (await client.PollAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Returns_the_playing_track_using_the_bearer_token()
    {
        var fake = new FakeSpotify { Player = _ => Json(HttpStatusCode.OK, PlayingJson) };
        using var client = ClientWith(fake, Tokens());

        var poll = await client.PollAsync(CancellationToken.None);

        Assert.Equal(SpotifyPollStatus.Ok, poll.Status);
        Assert.Equal("Song", poll.Track!.Title);
        Assert.Equal("Bearer ACCESS1", fake.LastAuthorization);
    }

    [Fact]
    public async Task No_content_means_nothing_playing()
    {
        var fake = new FakeSpotify { Player = _ => new HttpResponseMessage(HttpStatusCode.NoContent) };
        using var client = ClientWith(fake, Tokens());
        var poll = await client.PollAsync(CancellationToken.None);
        Assert.Equal(SpotifyPollStatus.Ok, poll.Status);
        Assert.Null(poll.Track);
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_before_polling_and_saved()
    {
        var fake = new FakeSpotify
        {
            Token = form =>
            {
                Assert.Equal("refresh_token", form["grant_type"]);
                Assert.Equal("REFRESH1", form["refresh_token"]);
                Assert.Equal("cid", form["client_id"]);
                return Json(HttpStatusCode.OK, """{"access_token":"ACCESS2","refresh_token":"REFRESH2","expires_in":3600}""");
            },
            Player = _ => Json(HttpStatusCode.OK, PlayingJson),
        };
        using var client = ClientWith(fake, Tokens(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var poll = await client.PollAsync(CancellationToken.None);

        Assert.Equal(SpotifyPollStatus.Ok, poll.Status);
        Assert.Equal("Bearer ACCESS2", fake.LastAuthorization);
        Assert.Equal("REFRESH2", SecretStore.Load<SpotifyTokens>("spotify")!.RefreshToken);
    }

    [Fact]
    public async Task Unauthorized_triggers_one_refresh_and_retry()
    {
        var calls = 0;
        var fake = new FakeSpotify
        {
            Token = _ => Json(HttpStatusCode.OK, """{"access_token":"ACCESS2","expires_in":3600}"""),
            Player = auth => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json(HttpStatusCode.OK, PlayingJson),
        };
        using var client = ClientWith(fake, Tokens());

        var poll = await client.PollAsync(CancellationToken.None);

        Assert.Equal(SpotifyPollStatus.Ok, poll.Status);
        Assert.Equal(2, calls);
        Assert.Equal("Bearer ACCESS2", fake.LastAuthorization);
        Assert.Equal("REFRESH1", SecretStore.Load<SpotifyTokens>("spotify")!.RefreshToken);
    }

    [Fact]
    public async Task Revoked_refresh_token_disconnects_and_asks_to_reconnect()
    {
        var fake = new FakeSpotify
        {
            Token = _ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Refresh token revoked"}"""),
        };
        using var client = ClientWith(fake, Tokens(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var poll = await client.PollAsync(CancellationToken.None);

        Assert.Equal(SpotifyPollStatus.NeedsReconnect, poll.Status);
        Assert.False(client.IsConnected);
        Assert.Null(SecretStore.Load<SpotifyTokens>("spotify"));
    }

    [Fact]
    public async Task Forbidden_is_reported_as_not_allowed()
    {
        var fake = new FakeSpotify { Player = _ => Json(HttpStatusCode.Forbidden, """{"error":{"status":403,"message":"User not registered"}}""") };
        using var client = ClientWith(fake, Tokens());
        var poll = await client.PollAsync(CancellationToken.None);
        Assert.Equal(SpotifyPollStatus.NotAllowed, poll.Status);
        Assert.Contains("User not registered", poll.Message);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Rate_limit_passes_retry_after_through()
    {
        var fake = new FakeSpotify
        {
            Player = _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
                return r;
            },
        };
        using var client = ClientWith(fake, Tokens());
        var poll = await client.PollAsync(CancellationToken.None);
        Assert.Equal(SpotifyPollStatus.RateLimited, poll.Status);
        Assert.Equal(TimeSpan.FromSeconds(42), poll.RetryAfter);
    }

    [Fact]
    public async Task Network_failure_is_a_soft_error()
    {
        var fake = new FakeSpotify { Player = _ => throw new HttpRequestException("offline") };
        using var client = ClientWith(fake, Tokens());
        var poll = await client.PollAsync(CancellationToken.None);
        Assert.Equal(SpotifyPollStatus.Error, poll.Status);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public void Disconnect_forgets_saved_tokens()
    {
        using var client = ClientWith(new FakeSpotify(), Tokens());
        Assert.True(client.IsConnected);
        client.Disconnect();
        Assert.False(client.IsConnected);
        Assert.Null(SecretStore.Load<SpotifyTokens>("spotify"));
    }

    static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    sealed class FakeSpotify : HttpMessageHandler
    {
        public Func<Dictionary<string, string>, HttpResponseMessage> Token { get; init; } =
            _ => Json(HttpStatusCode.BadRequest, """{"error":"unexpected"}""");

        public Func<string?, HttpResponseMessage> Player { get; init; } =
            _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://accounts.spotify.com/api/token", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                var form = LoopbackCallbackListener.ParseQuery(body).ToDictionary(kv => kv.Key, kv => kv.Value);
                return Token(form);
            }

            LastAuthorization = request.Headers.Authorization?.ToString();
            if (url.StartsWith("https://api.spotify.com/v1/me/player/currently-playing", StringComparison.Ordinal))
            {
                return Player(LastAuthorization);
            }
            if (url == "https://api.spotify.com/v1/me")
            {
                return Json(HttpStatusCode.OK, """{"display_name":"Tester","id":"t"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
