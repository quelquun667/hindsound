using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The launch finds the data folder of the user as every other file of the app does: a folder the system gives as no full
/// path, such as one under an empty home folder, means no entry at all, before anything decides about it.
/// </summary>
public sealed class ApplicationEntryForUserTests
{
    [Theory]
    [InlineData("")]
    [InlineData(".local/share")]
    public void A_data_folder_that_is_no_full_path_makes_no_entry(string dataHome)
    {
        Assert.Null(ApplicationEntry.ForUser(_ => dataHome, null));
    }

    [Fact]
    public void No_data_folder_makes_no_entry()
    {
        Assert.Null(ApplicationEntry.ForUser(_ => null, null));
    }

    [Fact]
    public void The_entry_goes_in_the_applications_folder_of_the_data_folder()
    {
        var dataHome = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "hindsound-share");
        Environment.SpecialFolder? asked = null;

        var entry = ApplicationEntry.ForUser(folder => { asked = folder; return dataHome; }, null);

        Assert.Equal(Environment.SpecialFolder.LocalApplicationData, asked);
        Assert.NotNull(entry);
        Assert.Equal(Path.Combine(dataHome, "applications", ApplicationEntry.FileName), entry.FilePath);
    }
}
