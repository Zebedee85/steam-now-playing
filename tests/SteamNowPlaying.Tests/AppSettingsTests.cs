using SteamNowPlaying.Core;
using Xunit;

namespace SteamNowPlaying.Tests;

[Collection(SettingsFolderCollection.Name)]
public sealed class AppSettingsTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "snp-tests-" + Guid.NewGuid().ToString("N"));
    readonly string originalFolder = AppSettings.Folder;

    public AppSettingsTests()
    {
        AppSettings.Folder = folder;
    }

    public void Dispose()
    {
        AppSettings.Folder = originalFolder;
        try { Directory.Delete(folder, recursive: true); } catch { }
    }

    string SettingsFile => Path.Combine(folder, "settings.json");

    [Fact]
    public void Round_trips_and_keeps_a_login_id()
    {
        var first = AppSettings.Load();
        Assert.NotEqual(0u, first.LoginId);
        first.IdleText = "AFK";
        first.CheckForUpdates = false;
        first.Save();

        var second = AppSettings.Load();
        Assert.Equal(first.LoginId, second.LoginId);
        Assert.Equal("AFK", second.IdleText);
        Assert.False(second.CheckForUpdates);
    }

    [Fact]
    public void Update_checks_are_on_by_default()
    {
        Assert.True(AppSettings.Load().CheckForUpdates);
    }

    [Fact]
    public void Corrupt_file_is_kept_aside_and_defaults_used()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsFile, "{ this is not json");

        var settings = AppSettings.Load();

        Assert.Equal(StatusFormatter.DefaultTemplate, settings.StatusTemplate);
        Assert.Equal("{ this is not json", File.ReadAllText(SettingsFile + ".bad"));
        Assert.NotEqual("{ this is not json", File.ReadAllText(SettingsFile)); // replaced with good defaults
    }
}
