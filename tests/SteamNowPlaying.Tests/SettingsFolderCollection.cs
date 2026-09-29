using Xunit;

namespace SteamNowPlaying.Tests;

/// <summary>Tests that point <c>AppSettings.Folder</c> (a static) at a temp folder must not run in parallel.</summary>
[CollectionDefinition(Name)]
public sealed class SettingsFolderCollection
{
    public const string Name = "AppSettings.Folder";
}
