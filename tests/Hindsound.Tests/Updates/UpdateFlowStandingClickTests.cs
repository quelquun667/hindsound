using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>A click that stood during its download and did not end in a Setup: the flow still checks again on its own.</summary>
public class UpdateFlowStandingClickTests
{
    private static readonly UpdateVersion Next = FlowRig.Next;
    private static readonly UpdateSurface Clip = new(FullScreen: false, Presentation: false, ClipInProgress: true, SessionEnding: false);

    [Fact]
    public void Click_dropped_by_a_clip_leaves_a_timer()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();
        rig.Host.Surface = Clip;
        rig.Host.FinishPass();
        Assert.Equal("Not now: a game or a clip is running.", rig.Flow.View.CardLine);

        rig.Host.Surface = default;
        rig.Host.Now += TimeSpan.FromDays(3);
        rig.Host.FireLast();

        Assert.Single(rig.Host.Passes);
    }

    [Fact]
    public void Click_whose_setup_did_not_start_leaves_a_timer()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();

        // Other bytes of the same size: the Setup still reads as downloaded, and the runner's hash refuses it.
        rig.Host.BetweenWorkAndDone = () => File.WriteAllBytes(UpdateFiles.SetupPath(rig.Folder, Next), new byte[rig.Transport.Setup.Length]);
        rig.Host.FinishPass();
        Assert.Empty(rig.Started);
        Assert.False(rig.Host.Left);

        rig.Host.Now += TimeSpan.FromDays(3);
        rig.Host.FireLast();

        Assert.Single(rig.Host.Passes);
    }
}
