using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SteamNowPlaying.Core;

namespace SteamNowPlaying.Spotify;

public enum SpotifyPollStatus { Ok, NotConnected, NeedsReconnect, NotAllowed, RateLimited, Error }

public sealed record SpotifyPoll(SpotifyPollStatus Status, NowPlaying? Track = null, TimeSpan? RetryAfter = null, string? Message = null);

public sealed record SpotifyTokens(string ClientId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed class SpotifyAuthException(string message, bool needsReconnect = false) : Exception(message)
{
    public bool NeedsReconnect { get; } = needsReconnect;
}

/// <summary>
/// Spotify Web API client using Authorization Code + PKCE, so no client secret ships with the app.
/// Each person signs in with their own Spotify account.
/// </summary>
public sealed class SpotifyClient : IDisposable
{
    public const int CallbackPort = 8888;
    public static readonly string RedirectUri = $"http://127.0.0.1:{CallbackPort}/callback";

    const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    const string TokenUrl = "https://accounts.spotify.com/api/token";
    const string CurrentlyPlayingUrl = "https://api.spotify.com/v1/me/player/currently-playing?additional_types=track,episode";
    const string ProfileUrl = "https://api.spotify.com/v1/me";
    const string Scopes = "user-read-currently-playing user-read-playback-state";
    const string SecretName = "spotify";

    const string ReturnPage = """
        <!doctype html><html><head><meta charset="utf-8"><title>Steam Now Playing</title>
        <style>body{font-family:Segoe UI,system-ui,sans-serif;background:#1e2430;color:#e8ecf2;display:grid;place-items:center;height:100vh;margin:0}
        div{text-align:center}h1{font-size:20px;font-weight:600}p{color:#aab3c2}</style></head>
        <body><div><h1>All done</h1><p>You can close this tab and go back to Steam Now Playing.</p></div></body></html>
        """;

    readonly HttpClient http;
    readonly SemaphoreSlim refreshLock = new(1, 1);
    volatile SpotifyTokens? tokens;

    public SpotifyClient(string clientId, HttpMessageHandler? handler = null, bool loadSaved = true)
    {
        ClientId = clientId;
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);

        if (loadSaved)
        {
            var saved = SecretStore.Load<SpotifyTokens>(SecretName);
            if (saved is not null && saved.ClientId == clientId) tokens = saved;
        }
    }

    public string ClientId { get; }

    public bool IsConnected => tokens is not null;

    public string? DisplayName { get; private set; }

    /// <summary>Opens the browser for sign-in and waits for Spotify to redirect back.</summary>
    public async Task ConnectAsync(Action<string> openBrowser, CancellationToken ct)
    {
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.RandomState();
        var url = AuthorizeUrl + "?" + BuildQuery(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["scope"] = Scopes,
            ["redirect_uri"] = RedirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = Pkce.Challenge(verifier),
            ["state"] = state,
        });

        using var listener = new LoopbackCallbackListener(CallbackPort);
        listener.Start();
        openBrowser(url);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        IReadOnlyDictionary<string, string> reply;
        try
        {
            reply = await listener.WaitForCallbackAsync("/callback", ReturnPage, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SpotifyAuthException("Spotify didn't send you back within 5 minutes. Try connecting again.");
        }

        if (reply.TryGetValue("error", out var error))
        {
            throw new SpotifyAuthException(error == "access_denied" ? "Spotify access wasn't allowed." : $"Spotify said: {error}");
        }
        if (!reply.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            throw new SpotifyAuthException("Spotify's reply didn't match this sign-in. Try again.");
        }
        if (!reply.TryGetValue("code", out var code) || code.Length == 0)
        {
            throw new SpotifyAuthException("Spotify didn't send an authorisation code. Try again.");
        }

        var newTokens = await RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }, previousRefreshToken: null, ct).ConfigureAwait(false);

        SetTokens(newTokens);
        Log.Write("Spotify connected.");
        await LoadProfileAsync(ct).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        tokens = null;
        DisplayName = null;
        SecretStore.Delete(SecretName);
    }

    /// <summary>Fetches the display name shown in the window. Failures are ignored.</summary>
    public async Task LoadProfileAsync(CancellationToken ct)
    {
        try
        {
            using var response = await SendAuthorizedAsync(ProfileUrl, ct).ConfigureAwait(false);
            if (response is null || !response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var name = Str(doc.RootElement, "display_name");
            DisplayName = name.Length > 0 ? name : Str(doc.RootElement, "id");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Cosmetic only.
        }
    }

    /// <summary>Asks Spotify what's playing right now.</summary>
    public async Task<SpotifyPoll> PollAsync(CancellationToken ct)
    {
        if (tokens is null) return new SpotifyPoll(SpotifyPollStatus.NotConnected);

        try
        {
            var response = await SendAuthorizedAsync(CurrentlyPlayingUrl, ct).ConfigureAwait(false);
            if (response is null)
            {
                return tokens is null
                    ? new SpotifyPoll(SpotifyPollStatus.NeedsReconnect, Message: "Your Spotify sign-in has expired. Connect again.")
                    : new SpotifyPoll(SpotifyPollStatus.Error, Message: "Couldn't refresh the Spotify sign-in.");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NoContent:
                        return new SpotifyPoll(SpotifyPollStatus.Ok);
                    case HttpStatusCode.OK:
                        return new SpotifyPoll(SpotifyPollStatus.Ok, ParseCurrentlyPlaying(body));
                    case HttpStatusCode.Forbidden:
                        return new SpotifyPoll(SpotifyPollStatus.NotAllowed, Message: ForbiddenMessage(body));
                    case HttpStatusCode.TooManyRequests:
                        return new SpotifyPoll(SpotifyPollStatus.RateLimited,
                            RetryAfter: response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
                    case HttpStatusCode.Unauthorized:
                        return new SpotifyPoll(SpotifyPollStatus.Error, Message: "Spotify rejected the sign-in. Retrying.");
                    default:
                        return new SpotifyPoll(SpotifyPollStatus.Error, Message: $"Spotify returned an error ({(int)response.StatusCode}).");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new SpotifyPoll(SpotifyPollStatus.Error, Message: "Spotify didn't answer in time.");
        }
        catch (HttpRequestException)
        {
            return new SpotifyPoll(SpotifyPollStatus.Error, Message: "Can't reach Spotify. Check your internet connection.");
        }
        catch (JsonException)
        {
            return new SpotifyPoll(SpotifyPollStatus.Error, Message: "Spotify sent something unexpected.");
        }
    }

    /// <summary>
    /// GETs a URL with the access token, refreshing it first if it has expired, and once more on 401.
    /// Returns null if the sign-in can't be refreshed.
    /// </summary>
    async Task<HttpResponseMessage?> SendAuthorizedAsync(string url, CancellationToken ct)
    {
        var current = tokens;
        if (current is null) return null;

        if (current.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (!await RefreshAsync(current, ct).ConfigureAwait(false)) return null;
            current = tokens;
            if (current is null) return null;
        }

        var response = await GetAsync(url, current.AccessToken, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        if (!await RefreshAsync(current, ct).ConfigureAwait(false)) return null;
        current = tokens;
        return current is null ? null : await GetAsync(url, current.AccessToken, ct).ConfigureAwait(false);
    }

    Task<HttpResponseMessage> GetAsync(string url, string accessToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http.SendAsync(request, ct);
    }

    async Task<bool> RefreshAsync(SpotifyTokens stale, CancellationToken ct)
    {
        await refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = tokens;
            if (current is null) return false;
            if (!ReferenceEquals(current, stale) && current.ExpiresAt > DateTimeOffset.UtcNow) return true; // someone else refreshed

            try
            {
                var refreshed = await RequestTokensAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                    ["client_id"] = ClientId,
                }, current.RefreshToken, ct).ConfigureAwait(false);
                SetTokens(refreshed);
                return true;
            }
            catch (SpotifyAuthException ex) when (ex.NeedsReconnect)
            {
                Log.Write("Spotify sign-in no longer valid: " + ex.Message);
                Disconnect();
                return false;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    async Task<SpotifyTokens> RequestTokensAsync(Dictionary<string, string> form, string? previousRefreshToken, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadString(body, "error");
            var description = TryReadString(body, "error_description");
            throw error switch
            {
                "invalid_grant" => new SpotifyAuthException("Your Spotify sign-in has expired. Connect again.", needsReconnect: true),
                "invalid_client" => new SpotifyAuthException("Spotify doesn't recognise this app's Client ID. Check it in Settings.", needsReconnect: true),
                _ => new SpotifyAuthException($"Spotify sign-in failed ({(int)response.StatusCode}{(description is null ? "" : ": " + description)})."),
            };
        }

        return ParseTokenResponse(ClientId, body, previousRefreshToken, DateTimeOffset.UtcNow);
    }

    internal static SpotifyTokens ParseTokenResponse(string clientId, string body, string? previousRefreshToken, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var access = Str(root, "access_token");
        if (access.Length == 0) throw new SpotifyAuthException("Spotify didn't return an access token.");

        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3600;
        var refresh = Str(root, "refresh_token");
        if (refresh.Length == 0) refresh = previousRefreshToken ?? throw new SpotifyAuthException("Spotify didn't return a refresh token.");

        // Refresh a minute early so a request never goes out with a token that's about to lapse.
        return new SpotifyTokens(clientId, access, refresh, now.AddSeconds(Math.Max(60, expiresIn - 60)));
    }

    /// <summary>Parses /v1/me/player/currently-playing. Returns null when nothing usable is playing (including ads).</summary>
    public static NowPlaying? ParseCurrentlyPlaying(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var isPlaying = root.TryGetProperty("is_playing", out var playing) && playing.ValueKind == JsonValueKind.True;
        var kind = Str(root, "currently_playing_type");
        if (kind.Length == 0) kind = "unknown";

        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) return null;

        var title = Str(item, "name");
        if (title.Length == 0) return null;

        var artist = "";
        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
        {
            artist = string.Join(", ", artists.EnumerateArray().Select(a => Str(a, "name")).Where(n => n.Length > 0));
        }

        var album = "";
        if (item.TryGetProperty("album", out var albumEl) && albumEl.ValueKind == JsonValueKind.Object)
        {
            album = Str(albumEl, "name");
        }

        if (item.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
        {
            if (artist.Length == 0) artist = Str(show, "name");
            if (album.Length == 0) album = Str(show, "publisher");
        }

        return new NowPlaying(title, artist, album, isPlaying, kind);
    }

    internal static string ForbiddenMessage(string body)
    {
        var detail = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                detail = Str(err, "message");
            }
        }
        catch (JsonException) { }

        return "Spotify won't let this account use the app yet. Ask the person who shared it to add your " +
               "Spotify email to their app's user list, or use your own Client ID in Settings." +
               (detail.Length > 0 ? $" (Spotify said: {detail})" : "");
    }

    void SetTokens(SpotifyTokens value)
    {
        tokens = value;
        SecretStore.Save(SecretName, value);
    }

    static string BuildQuery(Dictionary<string, string> values) =>
        string.Join("&", values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    static string? TryReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public void Dispose()
    {
        http.Dispose();
        refreshLock.Dispose();
    }
}
