using Hindsound.Linux.Desktop;
using Hindsound.Settings;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>A system with no config folder (no home): the entry is never written to a relative folder. Runs everywhere.</summary>
public sealed class AutostartWithoutConfigTests
{
    [Fact]
    public void Without_a_config_folder_there_is_no_autostart_folder()
    {
        Assert.Null(AutostartEntry.FolderIn(null));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "config", "autostart"), AutostartEntry.FolderIn(Path.Combine(Path.GetTempPath(), "config")));
    }

    [Fact]
    public void Without_a_folder_the_entry_is_absent_and_enabling_it_is_refused()
    {
        var entry = new AutostartEntry(folder: null);

        Assert.Equal(RunEntry.None, entry.State("/opt/hindsound"));
        entry.Disable();

        // An IOException is what the app reports as refused.
        Assert.Throws<IOException>(() => entry.Enable("/opt/hindsound"));
        Assert.Throws<IOException>(() => entry.FilePath);
    }
}
