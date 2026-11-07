using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>A Setup taken away between the end of its download and the step after it, as an antivirus does with an unsigned file.</summary>
public class UpdateFlowGoneSetupTests
{
    private static readonly UpdateVersion Next = FlowRig.Next;
    private static readonly int AlmostADay = (int)TimeSpan.FromHours(23).TotalMilliseconds;

    private static void TakeSetupAway(FlowRig rig) =>
        rig.Host.BetweenWorkAndDone = () => File.Delete(UpdateFiles.SetupPath(rig.Folder, Next));

    [Fact]
    public void Setup_gone_at_ready_is_not_downloaded_again_at_once()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        TakeSetupAway(rig);

        rig.Host.FinishPass();

        Assert.Empty(rig.Host.Passes);
        Assert.Equal([UpdateUrlPolicy.ManifestUrl, ReleaseTransport.SetupUrl], rig.Transport.Urls);
        Assert.Empty(rig.Host.Announced);
        Assert.Null(rig.State.Pending);
        Assert.True(rig.Host.LastDelay >= AlmostADay);
    }

    [Fact]
    public void Setup_gone_at_ready_drops_a_standing_click()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();
        TakeSetupAway(rig);

        rig.Host.FinishPass();

        Assert.Empty(rig.Host.Passes);
        Assert.Empty(rig.Started);
        Assert.False(rig.Host.Left);
        Assert.Equal("Download failed. Nothing changed.", rig.Flow.View.CardLine);
        Assert.True(rig.Host.LastDelay >= AlmostADay);
    }
}
