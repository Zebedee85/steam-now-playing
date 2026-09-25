using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Discovery;
using SteamKit2.Internal;
using SteamNowPlaying.Core;

namespace SteamNowPlaying.Steam;

public enum SteamState { SignedOut, Connecting, WaitingForQr, SigningIn, Online, Reconnecting, Error }

/// <summary>What we keep between runs. The refresh token is the long-lived Steam sign-in.</summary>
public sealed record SteamLogin(string AccountName, string RefreshToken);

/// <summary>
/// A second, lightweight Steam session (alongside the user's own Steam client) that shows a
/// custom "non-Steam game" name as the status. Signs in by QR code, so the app never sees a password.
/// </summary>
public sealed class SteamSession : IDisposable
{
    /// <summary>
    /// A "shortcut" game id. With this id Steam shows game_extra_info as the game name, which is how
    /// custom text appears as "In non-Steam game: ...".
    /// </summary>
    const ulong CustomGameId = 15190414816125648896UL;
    const string SecretName = "steam";

    readonly AppSettings settings;
    readonly SteamClient client;
    readonly CallbackManager manager;
    readonly SteamUser user;
    readonly SteamFriends friends;
    readonly object gate = new();
    readonly Thread callbackThread;

    volatile bool running = true;
    volatile bool wantOnline;
    SteamLogin? login;
    CancellationTokenSource? qrCts;
    int reconnectDelaySeconds = 5;
    bool loggedOn;
    bool playingBlocked;
    string? desiredStatus;
    string? sentStatus;
    bool statusKnownToSteam;

    public SteamSession(AppSettings settings)
    {
        this.settings = settings;

        // WebSockets over port 443 only: raw TCP to Steam (port 27017) is blocked or mangled on some
        // networks, and it's what failed on the first test machine.
        var config = SteamConfiguration.Create(builder => builder.WithProtocolTypes(ProtocolTypes.WebSocket));
        client = new SteamClient(config);
        manager = new CallbackManager(client);
        user = client.GetHandler<SteamUser>() ?? throw new InvalidOperationException("SteamUser handler missing");
        friends = client.GetHandler<SteamFriends>() ?? throw new InvalidOperationException("SteamFriends handler missing");

        manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        manager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
        manager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionState);

        login = SecretStore.Load<SteamLogin>(SecretName);

        callbackThread = new Thread(CallbackLoop) { IsBackground = true, Name = "Steam callbacks" };
        callbackThread.Start();
    }

    /// <summary>Raised on state or message changes. May fire on any thread.</summary>
    public event Action? Changed;

    public SteamState State { get; private set; } = SteamState.SignedOut;
    public string? Message { get; private set; }
    public string? QrChallengeUrl { get; private set; }
    public string? PersonaName { get; private set; }
    public string? AccountName => login?.AccountName;
    public bool HasSavedLogin => login is not null;

    /// <summary>True while the user is playing a real game elsewhere; our status stays off until they stop.</summary>
    public bool PlayingBlocked => playingBlocked;

    static string MachineLabel => $"Steam Now Playing ({Environment.MachineName})";

    /// <summary>Connects, then signs in with the saved token or starts a QR sign-in.</summary>
    public void Start()
    {
        if (wantOnline) return;
        wantOnline = true;
        reconnectDelaySeconds = 5;
        SetState(SteamState.Connecting, null);
        client.Connect();
    }

    /// <summary>Cancels a QR sign-in in progress.</summary>
    public void CancelSignIn()
    {
        wantOnline = false;
        qrCts?.Cancel();
        client.Disconnect();
        SetState(SteamState.SignedOut, null);
    }

    /// <summary>Clears the status, signs out and forgets the saved sign-in.</summary>
    public void SignOut()
    {
        wantOnline = false;
        qrCts?.Cancel();
        ClearStatusAndLogOff();
        client.Disconnect();
        login = null;
        PersonaName = null;
        SecretStore.Delete(SecretName);
        Log.Write("Signed out of Steam.");
        SetState(SteamState.SignedOut, null);
    }

    /// <summary>Sets the text shown on Steam (null clears it). Safe from any thread.</summary>
    public void SetStatus(string? text)
    {
        lock (gate)
        {
            desiredStatus = text;
            PushStatusLocked(force: false);
        }
    }

    void PushStatusLocked(bool force)
    {
        if (!loggedOn || playingBlocked) return;
        if (!force && statusKnownToSteam && desiredStatus == sentStatus) return;

        var message = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob);
        message.Body.client_os_type = (uint)EOSType.Windows10;
        if (!string.IsNullOrEmpty(desiredStatus))
        {
            message.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = CustomGameId,
                game_extra_info = desiredStatus,
            });
        }

        try
        {
            client.Send(message);
            sentStatus = desiredStatus;
            statusKnownToSteam = true;
        }
        catch (Exception ex)
        {
            Log.Write("Couldn't send status: " + ex.Message);
        }
    }

    void CallbackLoop()
    {
        while (running)
        {
            try
            {
                manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Log.Write("Steam callback error: " + ex);
            }
        }
    }

    void OnConnected(SteamClient.ConnectedCallback callback)
    {
        if (!wantOnline)
        {
            client.Disconnect();
            return;
        }

        reconnectDelaySeconds = 5;
        if (login is not null)
        {
            LogOnWithSavedToken();
        }
        else
        {
            qrCts?.Cancel();
            qrCts = new CancellationTokenSource();
            _ = RunQrSignInAsync(qrCts.Token);
        }
    }

    void LogOnWithSavedToken()
    {
        var saved = login;
        if (saved is null) return;

        SetState(SteamState.SigningIn, null);
        user.LogOn(new SteamUser.LogOnDetails
        {
            Username = saved.AccountName,
            AccessToken = saved.RefreshToken,
            ShouldRememberPassword = true,
            LoginID = settings.LoginId,
            MachineName = MachineLabel,
        });
    }

    async Task RunQrSignInAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var session = await client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
                {
                    DeviceFriendlyName = MachineLabel,
                    IsPersistentSession = true,
                }).ConfigureAwait(false);

                session.ChallengeURLChanged = () => ShowQr(session.ChallengeURL);
                ShowQr(session.ChallengeURL);

                try
                {
                    var result = await session.PollingWaitForResultAsync(ct).ConfigureAwait(false);
                    login = new SteamLogin(result.AccountName, result.RefreshToken);
                    SecretStore.Save(SecretName, login);
                    QrChallengeUrl = null;
                    Log.Write("Steam QR sign-in approved.");
                    LogOnWithSavedToken();
                    return;
                }
                catch (AuthenticationException ex) when (ex.Result is EResult.Expired or EResult.FileNotFound or EResult.Timeout)
                {
                    // Code expired before it was scanned: start a fresh one.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user or by a disconnect.
        }
        catch (Exception ex)
        {
            Log.Write("QR sign-in failed: " + ex.Message);
            QrChallengeUrl = null;
            if (wantOnline)
            {
                SetState(SteamState.Error, "Steam sign-in didn't work. Try again in a moment.");
                wantOnline = false;
                client.Disconnect();
            }
        }
    }

    void ShowQr(string url)
    {
        QrChallengeUrl = url;
        SetState(SteamState.WaitingForQr, null);
    }

    void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result == EResult.OK)
        {
            lock (gate)
            {
                loggedOn = true;
                playingBlocked = false;
                statusKnownToSteam = false;
            }

            if (settings.GoOnline)
            {
                friends.SetPersonaState(EPersonaState.Online);
            }

            Log.Write("Signed in to Steam.");
            SetState(SteamState.Online, null);

            lock (gate) PushStatusLocked(force: true);

            if (callback.ClientSteamID is { } steamId)
            {
                _ = RenewRefreshTokenAsync(steamId);
            }
            return;
        }

        Log.Write($"Steam sign-in refused: {callback.Result} / {callback.ExtendedResult}");

        switch (callback.Result)
        {
            case EResult.InvalidPassword:
            case EResult.AccessDenied:
            case EResult.Expired:
            case EResult.Revoked:
            case EResult.InvalidLoginAuthCode:
            case EResult.AccountLogonDenied:
            case EResult.AccountLoginDeniedNeedTwoFactor:
                // Saved sign-in no longer works (expired or removed in the Steam app): sign in again by QR.
                login = null;
                SecretStore.Delete(SecretName);
                SetState(SteamState.Connecting, "Your Steam sign-in has expired. Scan a new code to sign in again.");
                client.Disconnect(); // reconnect handler starts a QR sign-in
                break;

            case EResult.RateLimitExceeded:
            case EResult.AccountLoginDeniedThrottle:
                reconnectDelaySeconds = 600;
                SetState(SteamState.Reconnecting, "Steam is limiting sign-in attempts. Trying again in 10 minutes.");
                client.Disconnect();
                break;

            default:
                SetState(SteamState.Reconnecting, $"Steam said {callback.Result}. Retrying.");
                client.Disconnect();
                break;
        }
    }

    async Task RenewRefreshTokenAsync(SteamID steamId)
    {
        var saved = login;
        if (saved is null) return;
        try
        {
            var result = await client.Authentication.GenerateAccessTokenForAppAsync(steamId, saved.RefreshToken, allowRenewal: true).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(result.RefreshToken) && result.RefreshToken != saved.RefreshToken)
            {
                login = saved with { RefreshToken = result.RefreshToken };
                SecretStore.Save(SecretName, login);
                Log.Write("Steam sign-in renewed.");
            }
        }
        catch (Exception ex)
        {
            Log.Write("Steam sign-in renewal skipped: " + ex.Message);
        }
    }

    void OnAccountInfo(SteamUser.AccountInfoCallback callback)
    {
        PersonaName = callback.PersonaName;
        Changed?.Invoke();
    }

    void OnPlayingSessionState(SteamUser.PlayingSessionStateCallback callback)
    {
        bool changed;
        lock (gate)
        {
            changed = playingBlocked != callback.PlayingBlocked;
            playingBlocked = callback.PlayingBlocked;
            if (changed && !playingBlocked)
            {
                // The game elsewhere has finished: put our status back.
                PushStatusLocked(force: true);
            }
        }

        if (changed)
        {
            Log.Write(callback.PlayingBlocked ? "In a game elsewhere; status paused." : "Game elsewhere finished; status back on.");
            Changed?.Invoke();
        }
    }

    void OnLoggedOff(SteamUser.LoggedOffCallback callback)
    {
        lock (gate) loggedOn = false;
        Log.Write("Logged off by Steam: " + callback.Result);
        SetState(SteamState.Reconnecting, callback.Result == EResult.LoggedInElsewhere
            ? "Steam signed this session out because the account signed in elsewhere. Reconnecting."
            : $"Steam signed this session out ({callback.Result}). Reconnecting.");
    }

    void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        lock (gate)
        {
            loggedOn = false;
            playingBlocked = false;
            statusKnownToSteam = false;
        }
        qrCts?.Cancel();
        QrChallengeUrl = null;

        if (!running || !wantOnline)
        {
            if (State != SteamState.Error) SetState(SteamState.SignedOut, null);
            return;
        }

        var delay = reconnectDelaySeconds;
        reconnectDelaySeconds = Math.Min(Math.Max(delay * 2, 5), 300);

        if (State != SteamState.Connecting || Message is null)
        {
            SetState(SteamState.Reconnecting, $"Connection to Steam lost. Retrying in {delay} s.");
        }

        _ = Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ =>
        {
            if (running && wantOnline) client.Connect();
        }, TaskScheduler.Default);
    }

    void ClearStatusAndLogOff()
    {
        lock (gate)
        {
            if (!loggedOn) return;
            try
            {
                var message = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob);
                message.Body.client_os_type = (uint)EOSType.Windows10;
                client.Send(message);
                user.LogOff();
            }
            catch (Exception ex)
            {
                Log.Write("Sign-off error: " + ex.Message);
            }
            loggedOn = false;
        }
    }

    void SetState(SteamState state, string? message)
    {
        State = state;
        Message = message;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        wantOnline = false;
        qrCts?.Cancel();
        ClearStatusAndLogOff();
        running = false;
        try { client.Disconnect(); } catch { }
        callbackThread.Join(TimeSpan.FromSeconds(2));
    }
}
