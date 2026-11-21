using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Mac.Audio.Devices;

namespace Hindsound.Mac.Tests.Audio.Devices;

/// <summary>The process list of libproc and the HAL of the runner, read for real.</summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed partial class MacDevicesNativeTests
{
    [Fact]
    public void The_process_list_holds_this_process_with_its_parent_and_launchd()
    {
        var processes = new MacProcessSnapshot().Take();

        var own = Assert.Single(processes, process => process.Id == Environment.ProcessId);
        Assert.Equal(getppid(), own.ParentId);
        Assert.Equal(Path.GetFileName(Environment.ProcessPath), own.ExeName);
        Assert.Contains(processes, process => process.Id == own.ParentId);
        Assert.Contains(new ProcessEntry(1, 0, "launchd"), processes);
        Assert.True(processes.Count > 20, $"only {processes.Count} processes");
        Assert.Equal(processes.OrderBy(process => process.Id).Select(process => process.Id), processes.Select(process => process.Id));
    }

    [Fact]
    public void The_kernel_gives_the_path_of_this_program_and_the_process_a_pid_runs_for()
    {
        var facts = MacProcessSnapshot.ReadFacts();

        var own = Assert.Single(facts, process => process.Id == Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, own.Path);
        Assert.True(MacProcessSnapshot.KnowsResponsible);
        Assert.NotNull(MacProcessSnapshot.ResponsibleFor(Environment.ProcessId));
    }

    [Fact]
    public void The_hal_takes_the_listeners_of_the_watch_and_gives_them_back()
    {
        var (remove, statuses) = CoreAudioHal.Listen(MacDeviceWatcher.Selectors, () => { });

        Assert.Equal([0, 0, 0], statuses);
        remove.Dispose();
        remove.Dispose();
    }

    [Fact]
    public void Every_device_listed_has_its_uid_and_a_name()
    {
        var choices = new MacAudioChoices(_ => { });

        foreach (var flow in new[] { AudioFlow.System, AudioFlow.Mic })
        {
            Assert.All(choices.Devices(flow), device =>
            {
                Assert.False(string.IsNullOrEmpty(device.Id));
                Assert.False(string.IsNullOrWhiteSpace(device.Name));
            });
        }

        Assert.All(MacAudioChoices.ReadClients(), client => Assert.True(client.ProcessId > 0));
        Assert.All(choices.Apps(), app => Assert.False(string.IsNullOrWhiteSpace(app.Name)));
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial int getppid();
}
