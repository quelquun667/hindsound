using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>A data folder the system gave as no full path, such as an empty one, never gets the entry in the current folder.</summary>
public sealed class ApplicationEntryFolderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("share")]
    public void A_data_folder_that_is_no_full_path_gets_nothing(string dataHome)
    {
        var share = Path.Combine(Path.GetTempPath(), "hindsound-no-such-package", "share");

        Assert.Equal(EntryOutcome.NotPackaged.ToString(), new ApplicationEntry(dataHome, []).Decide(share, "/opt/hindsound").ToString());
        Assert.False(Directory.Exists("applications"));
    }
}
