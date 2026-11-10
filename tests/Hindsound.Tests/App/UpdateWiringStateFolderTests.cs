using Hindsound.App;
using Hindsound.Tests.Updates;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The state folder of the flow a run started as a user starts it gets. The portable copy keeps its own, so its launches
/// never read nor rewrite the installed copy's state, nor clean the folder where its Setup waits. Nothing is called on the
/// flows made here: neither the disk nor the network is reached.
/// </summary>
public class UpdateWiringStateFolderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));

    private static UpdateFlow Flow(UpdateChannel channel, string exeFolder)
    {
        WindowsPlatform.Install();
        var flow = UpdateWiring.Create(updaterMayRun: true, AppOptions.Parse([]), exeFolder, new FakeUpdateHost(), Now, channel);
        Assert.NotNull(flow);
        Assert.Same(channel, flow.Channel);
        return flow;
    }

    [Fact]
    public void The_installed_copy_keeps_its_state_under_hindsound_updates()
    {
        using var copy = new TempFolder();
        File.WriteAllText(copy.File(InstallKind.UninstallerName), "");

        var folder = Flow(UpdateChannel.WindowsSetup, copy.Path).Folder;

        Assert.Equal(UpdateStateStore.DefaultFolder, folder);
        Assert.Equal("updates", Path.GetFileName(folder));
        Assert.Equal("Hindsound", Path.GetFileName(Path.GetDirectoryName(folder)));
    }

    [Fact]
    public void The_portable_copy_keeps_its_state_apart_under_hindsound_updates_portable()
    {
        using var copy = new TempFolder();
        File.WriteAllText(copy.File(PortableCopy.MarkerName), "");

        var folder = Flow(UpdateChannel.WindowsPortable, copy.Path).Folder;

        Assert.NotEqual(UpdateStateStore.DefaultFolder, folder);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(UpdateStateStore.DefaultFolder)!, "updates-portable"), folder);
    }
}
