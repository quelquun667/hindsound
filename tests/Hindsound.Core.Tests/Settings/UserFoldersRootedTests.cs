using System.IO;
using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

/// <summary>
/// A path that starts at a root without being complete still depends on where the process is: on Windows, "\Music" is on
/// the current drive and "C:Music" in the current folder of drive C. Such a folder is none, as a relative one is.
/// </summary>
public sealed class UserFoldersRootedTests
{
    [Theory]
    [InlineData(@"\Music")]
    [InlineData("C:Music")]
    [InlineData(@"C:relative\Music")]
    public void A_folder_rooted_without_being_complete_is_none(string given)
    {
        // On Linux both are relative names, so none there too.
        Assert.Null(UserFolders.Find(Environment.SpecialFolder.MyMusic, _ => given));
        Assert.True(Path.IsPathFullyQualified(UserFolders.Under(Environment.SpecialFolder.MyMusic, _ => given, "Hindsound")));
    }

    [Fact]
    public void A_folder_from_the_root_of_the_current_drive_is_none_on_windows()
    {
        // "/Music" is a full path on Linux, but on Windows it is on whatever drive the process started from.
        var found = UserFolders.Find(Environment.SpecialFolder.MyMusic, _ => "/Music");

        Assert.Equal(OperatingSystem.IsWindows() ? null : "/Music", found);
    }
}
