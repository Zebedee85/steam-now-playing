# Roadmap

A running list of what's planned, what's done and why. Newest thinking goes at the top of each section. Anything worth remembering (ideas, decisions, rejected options) goes here so it isn't lost between sessions.

## Released

### v0.1.0 (2026-09-25)
- Spotify → Steam "In non-Steam game" status, QR sign-in for Steam, PKCE for Spotify.
- DPAPI-encrypted sign-ins, tray icon, start with Windows, single-file exe from GitHub Actions.

## In progress (next release, v0.2.0)

- [x] **Tray tooltip** shows the song and artist on their own lines (or what's going on: paused, in a game, not live), plus a "Spotify needs attention" line when there's a problem. Uses the 127-character limit that .NET 6+ allows, instead of the old 63.
- [x] **Update checker.** Asks GitHub's releases API once a day (first check 30 s after start). Shows an "Update to x.y.z" link in the window and the tray menu, and a one-time notification per version. It only opens the project's own releases page and never downloads anything itself. Can be turned off in Settings. Development builds don't check.
- [x] **Release pipeline hardening.**
  - The workflow is read-only by default. Only the release job (tags only) gets write access.
  - `SHA256SUMS.txt` is published next to the exe and verified before the release is created.
  - A GitHub build provenance attestation is attached (check with `gh attestation verify SteamNowPlaying.exe -R Zebedee85/steam-now-playing`).
  - Checkout no longer keeps the token around, and Dependabot watches NuGet packages and Actions weekly.
- [x] **Robustness.**
  - An unreadable `settings.json` is kept as `settings.json.bad` rather than silently overwritten.
  - A stale delayed Steam reconnect can no longer fire after the user has signed out or started a fresh sign-in.
  - Header and tray text logic moved into `StatusSummary` so it's unit-tested.

## Next up

- **Code signing** to get rid of the SmartScreen "Windows protected your PC" warning. Options, cheapest first:
  1. **SignPath Foundation**: free signing for open-source projects. Needs an application, an OSI licence (MIT is fine) and a public build on GitHub Actions. The best fit if accepted.
  2. **Azure Artifact Signing** (formerly Trusted Signing): a monthly subscription, with identity validation for an individual. Integrates with GitHub Actions via `azure/artifact-signing-action`.
  3. A traditional OV certificate on a hardware token: the most expensive and awkward in CI. Not worth it for a hobby app.

  Note: even signed, a new certificate builds SmartScreen reputation gradually, so the warning may linger for the first downloads.
- **Check the tray tooltip with `&` in names** (for example *Simon & Garfunkel*). Some Windows versions treat `&` in notify icon tooltips as a mnemonic and hide it. If so, escape it in `StatusSummary.TrayText`.
- **StatusSync tests.** Put `SteamSession` behind a small interface (`SetStatus`, `State`, `PlayingBlocked`, `Changed`) so the poll loop's back-off and state handling can be tested without a live Steam connection.

## Ideas (not committed)

- A playback progress or elapsed time in the tooltip (Spotify gives `progress_ms`, but updating every 4 s is chatty for little gain).
- A global hotkey to pause sharing.
- Per-kind templates: podcasts might read better as "Listening to {track} on {artist}".
- Winget or Scoop manifest once releases are signed.

## Decisions

- **No auto-install of updates.** The app points to the release page and the person downloads it. That's simpler and safer than a self-updater, and it keeps the "only talks to Steam, Spotify and GitHub" promise easy to state.
- **No client secret, ever.** Spotify uses PKCE; the Client ID in the build is public by design.
