using SteamNowPlaying.Core;
using SteamNowPlaying.Spotify;
using SteamNowPlaying.Steam;

namespace SteamNowPlaying;

/// <summary>Polls Spotify and keeps the Steam status in step with it.</summary>
public sealed class StatusSync : IDisposable
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);

    readonly AppSettings settings;
    readonly CancellationTokenSource lifetime = new();
    readonly object gate = new();
    SpotifyClient? spotify;
    Task? pollLoop;
    CancellationTokenSource? connectCts;

    public StatusSync(AppSettings settings)
    {
        this.settings = settings;
        Steam = new SteamSession(settings);
        Steam.Changed += () => Changed?.Invoke();
        spotify = CreateSpotifyClient();
    }

    /// <summary>Raised whenever anything shown in the window may have changed. Fires on background threads.</summary>
    public event Action? Changed;

    public SteamSession Steam { get; }

    public SpotifyClient? Spotify => spotify;

    public NowPlaying? CurrentTrack { get; private set; }

    /// <summary>A Spotify problem worth showing, or null.</summary>
    public string? SpotifyProblem { get; private set; }

    public bool SpotifyConnecting => connectCts is not null;

    /// <summary>The text we're asking Steam to show (null = no status).</summary>
    public string? StatusText { get; private set; }

    public bool Paused => settings.Paused;

    public string? EffectiveClientId =>
        !string.IsNullOrWhiteSpace(settings.SpotifyClientId) ? settings.SpotifyClientId.Trim() : BuildInfo.DefaultSpotifyClientId;

    public void Start()
    {
        if (Steam.HasSavedLogin) Steam.Start();
        pollLoop ??= Task.Run(() => PollLoopAsync(lifetime.Token));
    }

    SpotifyClient? CreateSpotifyClient()
    {
        var id = EffectiveClientId;
        return id is null ? null : new SpotifyClient(id);
    }

    async Task PollLoopAsync(CancellationToken ct)
    {
        var profileLoadedFor = (SpotifyClient?)null;

        while (!ct.IsCancellationRequested)
        {
            var wait = PollInterval;
            var client = spotify;

            try
            {
                if (client is null)
                {
                    Update(null, "No Spotify app is set up. Add a Client ID in Settings.");
                    wait = TimeSpan.FromSeconds(1);
                }
                else if (!client.IsConnected)
                {
                    Update(null, SpotifyProblem); // keep any sign-in error on screen
                    wait = TimeSpan.FromSeconds(1);
                }
                else
                {
                    if (!ReferenceEquals(profileLoadedFor, client))
                    {
                        profileLoadedFor = client;
                        await client.LoadProfileAsync(ct).ConfigureAwait(false);
                    }

                    var poll = await client.PollAsync(ct).ConfigureAwait(false);
                    switch (poll.Status)
                    {
                        case SpotifyPollStatus.Ok:
                            Update(poll.Track, null);
                            break;
                        case SpotifyPollStatus.NotConnected:
                            Update(null, SpotifyProblem);
                            break;
                        case SpotifyPollStatus.NeedsReconnect:
                            Update(null, poll.Message);
                            break;
                        case SpotifyPollStatus.NotAllowed:
                            Update(null, poll.Message);
                            wait = TimeSpan.FromSeconds(60);
                            break;
                        case SpotifyPollStatus.RateLimited:
                            wait = poll.RetryAfter is { } retry && retry > TimeSpan.FromSeconds(5) ? retry : TimeSpan.FromSeconds(5);
                            SetProblem("Spotify asked for a short break. Carrying on shortly.");
                            break;
                        default:
                            // Transient: keep showing the last track rather than flickering the status.
                            SetProblem(poll.Message);
                            wait = TimeSpan.FromSeconds(15);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Write("Spotify poll error: " + ex);
                SetProblem("Something went wrong talking to Spotify. Retrying.");
                wait = TimeSpan.FromSeconds(15);
            }

            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    void Update(NowPlaying? track, string? problem)
    {
        var changed = !Equals(track, CurrentTrack) || problem != SpotifyProblem;
        CurrentTrack = track;
        SpotifyProblem = problem;
        ApplyStatus(notify: changed);
    }

    void SetProblem(string? problem)
    {
        if (SpotifyProblem == problem) return;
        SpotifyProblem = problem;
        Changed?.Invoke();
    }

    /// <summary>Works out the status from the current track and settings, and sends it to Steam.</summary>
    public void ApplyStatus() => ApplyStatus(notify: true);

    void ApplyStatus(bool notify)
    {
        string? text = null;
        if (!settings.Paused && spotify?.IsConnected == true)
        {
            text = StatusFormatter.Format(CurrentTrack, settings.StatusTemplate, settings.IdleText);
        }

        var changed = text != StatusText;
        StatusText = text;
        Steam.SetStatus(text);
        if (changed) Log.Write(text is null ? "Status cleared." : "Status: " + text);
        if (changed || notify) Changed?.Invoke();
    }

    public async Task ConnectSpotifyAsync(Action<string> openBrowser)
    {
        var client = spotify ?? throw new InvalidOperationException("No Spotify Client ID is set.");
        CancellationTokenSource cts;
        lock (gate)
        {
            if (connectCts is not null) return;
            cts = connectCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        }

        SpotifyProblem = null;
        Changed?.Invoke();
        try
        {
            await client.ConnectAsync(openBrowser, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the window.
        }
        catch (Exception ex) when (ex is SpotifyAuthException or InvalidOperationException or HttpRequestException)
        {
            SpotifyProblem = ex.Message;
            Log.Write("Spotify connect failed: " + ex.Message);
        }
        finally
        {
            lock (gate) connectCts = null;
            cts.Dispose();
            ApplyStatus();
        }
    }

    public void CancelSpotifyConnect()
    {
        lock (gate) connectCts?.Cancel();
    }

    public void DisconnectSpotify()
    {
        spotify?.Disconnect();
        Update(null, null);
        Log.Write("Spotify disconnected.");
    }

    public void SetPaused(bool paused)
    {
        settings.Paused = paused;
        settings.Save();
        ApplyStatus();
    }

    /// <summary>Call after the settings dialog saves.</summary>
    public void SettingsChanged()
    {
        var wanted = EffectiveClientId;
        if (spotify?.ClientId != wanted)
        {
            var old = spotify;
            spotify = CreateSpotifyClient();
            old?.Dispose();
            Update(null, null);
        }
        else
        {
            ApplyStatus();
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        try { pollLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        Steam.Dispose();
        spotify?.Dispose();
        lifetime.Dispose();
    }
}
