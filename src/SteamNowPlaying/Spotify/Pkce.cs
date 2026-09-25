using System.Security.Cryptography;
using System.Text;

namespace SteamNowPlaying.Spotify;

/// <summary>PKCE helpers (RFC 7636): the app proves it started the sign-in without needing a client secret.</summary>
public static class Pkce
{
    const string VerifierChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    public static string CreateVerifier(int length = 64) => RandomNumberGenerator.GetString(VerifierChars, length);

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string RandomState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
