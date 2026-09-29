using System.Net.Http.Headers;
using System.Text.Json;

namespace SteamNowPlaying.Core;

/// <summary>A newer release on GitHub.</summary>
public sealed record AvailableUpdate(Version Version, string Tag, string PageUrl);

/// <summary>
/// Asks GitHub, about once a day, whether a newer release exists. Only reads the public releases
/// API; nothing about the user is sent. Never downloads or runs anything itself.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    public const string LatestReleaseApi = "https://api.github.com/repos/Zebedee85/steam-now-playing/releases/latest";

    static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    readonly HttpClient http;
    readonly Func<bool> enabled;
    readonly CancellationTokenSource lifetime = new();
    Task? loop;

    public UpdateChecker(Func<bool> enabled, HttpMessageHandler? handler = null)
    {
        this.enabled = enabled;
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SteamNowPlaying", CurrentVersion()?.ToString() ?? "dev"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>Raised on a background thread when a newer release is found.</summary>
    public event Action<AvailableUpdate>? UpdateFound;

    public AvailableUpdate? Available { get; private set; }

    public void Start()
    {
        // Development builds (0.0.0-dev.N) would always look out of date.
        if (CurrentVersion() is null) return;
        loop ??= Task.Run(() => LoopAsync(lifetime.Token));
    }

    async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                if (enabled() && Available is null) await CheckNowAsync(ct).ConfigureAwait(false);
                await Task.Delay(CheckInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>Checks once. Returns the update if there is one; failures are logged and return null.</summary>
    public async Task<AvailableUpdate?> CheckNowAsync(CancellationToken ct)
    {
        var current = CurrentVersion();
        if (current is null) return null;

        try
        {
            using var response = await http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Write($"Update check: GitHub returned {(int)response.StatusCode}.");
                return null;
            }

            var update = ParseLatestRelease(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (update is null || update.Version <= current) return null;

            Available = update;
            Log.Write($"Update available: {update.Tag}");
            UpdateFound?.Invoke(update);
            return update;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) && !ct.IsCancellationRequested)
        {
            Log.Write("Update check failed: " + ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Reads GitHub's "latest release" JSON. Ignores drafts, pre-releases, unparseable tags, and any
    /// page URL that isn't this project's own releases page.
    /// </summary>
    public static AvailableUpdate? ParseLatestRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

        var tag = Str(root, "tag_name");
        var version = ParseVersion(tag);
        if (version is null) return null;

        var page = Str(root, "html_url");
        var releasesPrefix = BuildInfo.RepoUrl + "/releases/";
        if (!page.StartsWith(releasesPrefix, StringComparison.OrdinalIgnoreCase)) page = BuildInfo.RepoUrl + "/releases/latest";

        return new AvailableUpdate(version, tag, page);
    }

    /// <summary>"v1.2.3" or "1.2.3" to a Version; null for anything else, including "1.2.3-beta".</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        if (s.Contains('-') || s.Contains('+')) return null;
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>This build's version, or null for development builds.</summary>
    static Version? CurrentVersion() => ParseVersion(BuildInfo.Version);

    static bool IsTrue(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;

    static string Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public void Dispose()
    {
        lifetime.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* shutting down */ }
        http.Dispose();
        lifetime.Dispose();
    }
}
