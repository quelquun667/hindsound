using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// The Windows flow has announced a version, so it holds one: a click on that notice still goes to the window and its
/// Setup, never to the release page, which is only for a platform that tells without installing.
/// </summary>
public class UpdateFlowWindowsNoPageTests
{
    private static readonly UpdateVersion Next = FlowRig.Next;
    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);

    [Fact]
    public void An_update_ready_announced_in_auto_opens_no_page()
    {
        using var rig = new FlowRig();
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        rig.Host.Surface = Game;
        rig.Host.FinishPass();

        Assert.Equal([(Next, UpdateMode.Auto)], rig.Host.Announced);
        Assert.Null(rig.Flow.ReleasePageToOpen);
    }

    [Fact]
    public void An_update_available_announced_in_ask_opens_no_page()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();

        Assert.Equal([(Next, UpdateMode.Ask)], rig.Host.Announced);
        Assert.Null(rig.Flow.ReleasePageToOpen);
    }
}
