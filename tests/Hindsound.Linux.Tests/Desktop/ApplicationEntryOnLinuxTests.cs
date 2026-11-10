using System.Runtime.Versioning;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The entry of the menu as a launch on Linux reads its folders: the test host runs from its own build folder, which is no
/// package, so nothing of the user's is written, and the log gets its one line.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class ApplicationEntryOnLinuxTests
{
    [Fact]
    public void A_launch_from_no_package_writes_nothing_and_says_so_once()
    {
        Assert.Null(Environment.GetEnvironmentVariable("APPIMAGE"));
        var lines = new List<string>();
        var dataHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var entry = Path.Combine(dataHome, "applications", ApplicationEntry.FileName);
        var before = File.Exists(entry) ? File.GetLastWriteTimeUtc(entry) : (DateTime?)null;

        ApplicationEntry.AtLaunch("/opt/hindsound/hindsound", lines.Add);

        Assert.Equal(["desktop entry: not run from an AppImage or the folder of the archive, nothing written"], lines);
        Assert.Equal(before, File.Exists(entry) ? File.GetLastWriteTimeUtc(entry) : null);
        Assert.StartsWith("/", dataHome, StringComparison.Ordinal);
    }

    [Fact]
    public void The_system_folders_of_the_session_are_absolute()
    {
        var folders = EntryRules.SystemDataFolders(Environment.GetEnvironmentVariable("XDG_DATA_DIRS"));

        Assert.NotEmpty(folders);
        Assert.All(folders, folder => Assert.StartsWith("/", folder, StringComparison.Ordinal));
    }
}
