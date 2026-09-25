# Third-party notices

Steam Now Playing is MIT-licensed (see [LICENSE](LICENSE)). It includes the following components, unmodified, from NuGet.

| Component | License | Source |
|---|---|---|
| SteamKit2 | LGPL-2.1-only | https://github.com/SteamRE/SteamKit |
| protobuf-net (used by SteamKit2) | Apache-2.0 | https://github.com/protobuf-net/protobuf-net |
| ZstdSharp.Port (used by SteamKit2) | MIT | https://github.com/oleg-st/ZstdSharp |
| System.IO.Hashing (used by SteamKit2) | MIT | https://github.com/dotnet/runtime |
| QRCoder | MIT | https://github.com/Shane32/QRCoder |
| System.Security.Cryptography.ProtectedData | MIT | https://github.com/dotnet/runtime |
| .NET runtime and Windows Forms | MIT | https://github.com/dotnet |

## About SteamKit2 and the LGPL

SteamKit2 is used as an unmodified library. This project's full source is public, so you can rebuild the app against a modified SteamKit2: change the `SteamKit2` package reference in `src/SteamNowPlaying/SteamNowPlaying.csproj` to your own build, then publish as described in the README. That satisfies the LGPL's relinking requirement.

The full text of each license is available at the source links above.
