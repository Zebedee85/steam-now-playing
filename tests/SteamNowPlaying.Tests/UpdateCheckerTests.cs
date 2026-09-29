using System.Net;
using System.Text;
using SteamNowPlaying.Core;
using Xunit;

namespace SteamNowPlaying.Tests;

[Collection(SettingsFolderCollection.Name)]
public sealed class UpdateCheckerTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "snp-tests-" + Guid.NewGuid().ToString("N"));
    readonly string originalFolder = AppSettings.Folder;

    public UpdateCheckerTests()
    {
        AppSettings.Folder = folder; // keeps the update log lines out of the real log
    }

    public void Dispose()
    {
        AppSettings.Folder = originalFolder;
        try { Directory.Delete(folder, recursive: true); } catch { }
    }

    static string Release(string tag, bool prerelease = false, bool draft = false, string? url = null) =>
        $$"""{"tag_name":"{{tag}}","prerelease":{{(prerelease ? "true" : "false")}},"draft":{{(draft ? "true" : "false")}},"html_url":"{{url ?? BuildInfo.RepoUrl + "/releases/tag/" + tag}}"}""";

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0.0")]
    [InlineData(" v0.10.1 ", "0.10.1")]
    public void Parses_release_tags(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.2.3-beta")]
    [InlineData("0.0.0-dev.12")]
    [InlineData("1.2.3+abc")]
    public void Rejects_prerelease_and_junk_tags(string? tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    [Fact]
    public void Numeric_comparison_not_string_comparison()
    {
        Assert.True(UpdateChecker.ParseVersion("v0.10.0") > UpdateChecker.ParseVersion("v0.9.9"));
    }

    [Fact]
    public void Reads_a_normal_release()
    {
        var update = UpdateChecker.ParseLatestRelease(Release("v2.1.0"))!;
        Assert.Equal(new Version(2, 1, 0), update.Version);
        Assert.Equal("v2.1.0", update.Tag);
        Assert.Equal(BuildInfo.RepoUrl + "/releases/tag/v2.1.0", update.PageUrl);
    }

    [Fact]
    public void Ignores_drafts_and_prereleases()
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(Release("v2.0.0", prerelease: true)));
        Assert.Null(UpdateChecker.ParseLatestRelease(Release("v2.0.0", draft: true)));
    }

    [Fact]
    public void Never_trusts_a_foreign_page_url()
    {
        var update = UpdateChecker.ParseLatestRelease(Release("v2.0.0", url: "https://evil.example/steam-now-playing.exe"))!;
        Assert.Equal(BuildInfo.RepoUrl + "/releases/latest", update.PageUrl);
    }

    [Fact]
    public async Task Reports_a_newer_release()
    {
        using var checker = new UpdateChecker(() => true, new FakeGitHub(HttpStatusCode.OK, Release("v99.0.0")));
        AvailableUpdate? raised = null;
        checker.UpdateFound += u => raised = u;

        var update = await checker.CheckNowAsync(CancellationToken.None);

        Assert.NotNull(update);
        Assert.Same(update, raised);
        Assert.Same(update, checker.Available);
    }

    [Fact]
    public async Task Stays_quiet_when_up_to_date()
    {
        using var checker = new UpdateChecker(() => true, new FakeGitHub(HttpStatusCode.OK, Release("v0.0.1")));
        Assert.Null(await checker.CheckNowAsync(CancellationToken.None));
        Assert.Null(checker.Available);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Failures_are_swallowed(HttpStatusCode status, string body)
    {
        using var checker = new UpdateChecker(() => true, new FakeGitHub(status, body));
        Assert.Null(await checker.CheckNowAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sends_a_user_agent_as_github_requires()
    {
        var fake = new FakeGitHub(HttpStatusCode.OK, Release("v0.0.1"));
        using var checker = new UpdateChecker(() => true, fake);
        await checker.CheckNowAsync(CancellationToken.None);
        Assert.StartsWith("SteamNowPlaying/", fake.LastUserAgent);
        Assert.Equal(UpdateChecker.LatestReleaseApi, fake.LastUrl);
    }

    sealed class FakeGitHub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUserAgent { get; private set; }
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            LastUserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
