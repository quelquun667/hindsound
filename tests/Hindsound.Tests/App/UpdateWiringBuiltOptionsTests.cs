using Hindsound.App;
using Hindsound.Tests.Updates;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// Parse already drops --selftest-update without --settings; the wiring checks it again on its own. Options built by hand,
/// without Parse, must not give the test flow a state next to the user's.
/// </summary>
public class UpdateWiringBuiltOptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 14, 0, 0, TimeSpan.FromHours(1));

    private static TempFolder InstalledCopy()
    {
        var folder = new TempFolder();
        File.WriteAllBytes(folder.File(InstallKind.UninstallerName), []);
        return folder;
    }

    [Fact]
    public void Test_folder_without_settings_is_a_test_run()
    {
        using var copy = InstalledCopy();
        using var files = new TempFolder();
        var options = new AppOptions { SelfTestUpdateFolder = files.Path };

        Assert.Equal(UpdateWiring.TestRun, UpdateWiring.Why(false, options, copy.Path));
        Assert.Null(UpdateWiring.Create(false, options, copy.Path, new FakeUpdateHost(), Now));
    }

    [Fact]
    public void Test_folder_with_settings_is_the_test_flow()
    {
        using var copy = InstalledCopy();
        using var files = new TempFolder();
        var options = new AppOptions { SettingsPath = files.File("s.json"), SelfTestUpdateFolder = files.Path };

        Assert.Equal(UpdateWiring.TestFolder, UpdateWiring.Why(false, options, copy.Path));
        Assert.NotNull(UpdateWiring.Create(false, options, copy.Path, new FakeUpdateHost(), Now));
    }
}
