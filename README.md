# Steam Now Playing

Shows what you're listening to on Spotify as your Steam status, so friends see something like:

> **In non-Steam game:** Listening to Fire Water Burn • Bloodhound Gang

It's one small Windows app. You sign in to Steam by scanning a QR code with the Steam app, so this app never sees your Steam password. It then follows whatever Spotify is playing on your account, on any device.

## Get started

1. Download **SteamNowPlaying.exe** from the [latest release](../../releases/latest) and put it somewhere permanent, such as a `Steam Now Playing` folder in Documents.
2. Run it. The app isn't code-signed, so Windows may say *"Windows protected your PC"*. Click **More info → Run anyway**.
3. **Steam:** click **Sign in**, then scan the QR code with the Steam app on your phone (**Steam Guard** tab → **Scan a QR code**).
4. **Spotify:** click **Connect** and approve access in your browser.

That's it. Minimise the window and it keeps running in the tray. Right-click the tray icon to pause or quit.

> **"Spotify won't let this account use the app yet"?** Spotify limits small apps to five people, each added by hand. Ask whoever sent you the app to add your Spotify email, or [use your own Spotify app](#using-your-own-spotify-app).

## Good to know

- **Real games come first.** This is a second, lightweight Steam session. If you start a game while it's running, Steam may ask about *playing on another computer*; carry on. The song is hidden while you play and comes back when you stop.
- **Invisible?** Untick *Set me to Online* in **Settings**, or the app will switch you back to Online when it signs in.
- **Your status text** is editable in **Settings**, using `{track}`, `{artist}` and `{album}`. You can also choose what to show when nothing is playing (the default shows nothing).
- **Start with Windows** is in **Settings**. The app then starts quietly in the tray.
- **From your Steam library (optional):** Steam → *Games* → *Add a Non-Steam Game to My Library* → pick the exe. Then under *Properties*, set *Launch Options* to `--detach`, so Steam's own "In non-Steam game" status doesn't cover yours.

## Privacy and removal

- The app only talks to Steam and Spotify.
- It keeps its files in `%APPDATA%\SteamNowPlaying`: `settings.json`, the Steam and Spotify sign-ins (`steam.dat`, `spotify.dat`, encrypted with Windows DPAPI so only your Windows account can read them), and `log.txt` (no passwords, codes or tokens).
- To remove everything: **Sign out** and **Disconnect** in the app, quit it, and delete that folder. For good measure, remove *Steam Now Playing* from the Steam app (**Steam Guard → Authorized devices**) and from your Spotify account page (**Manage apps**).

## Using your own Spotify app

Needed if the person who shared the app has no spaces left. Spotify requires the app owner to have **Premium**.

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**.
2. Add the redirect URI `http://127.0.0.1:8888/callback` exactly, tick **Web API**, and save.
3. Copy the **Client ID**. The secret isn't needed.
4. In Steam Now Playing, open **Settings**, paste it into **Spotify Client ID**, save, and click **Connect**.

## Sharing it with friends (for the repo owner)

- The built-in Spotify Client ID comes from the repository variable `SPOTIFY_CLIENT_ID` (**Settings → Secrets and variables → Actions → Variables**). It's not a secret: the app uses PKCE, so no client secret is ever shipped.
- Spotify's development mode allows **up to 5 users** per app. Add each friend's name and Spotify email under your app's **User Management** in the Spotify dashboard.
- To publish a version, push a tag such as `v1.0.0`. GitHub Actions runs the tests, builds a single self-contained `SteamNowPlaying.exe` and attaches it to a release.

## Building it yourself

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet test tests/SteamNowPlaying.Tests/SteamNowPlaying.Tests.csproj
dotnet publish src/SteamNowPlaying/SteamNowPlaying.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:SpotifyClientId=<your client id> -o out
```

## Credits

Inspired by [HuxleyMc/Steam-Spotify](https://github.com/HuxleyMc/Steam-Spotify). Built on [SteamKit2](https://github.com/SteamRE/SteamKit) and [QRCoder](https://github.com/Shane32/QRCoder). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with Valve or Spotify.
