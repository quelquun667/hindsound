using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// An automatic pass stopped by a switch to Off, with the mode back on before the pass ended: still no game paused it, and
/// the flow decides again soon, as a switch back on with no pass running does.
/// </summary>
public class UpdateFlowOffAndBackTests
{
    private const string PausedLine = "Paused by a game or a clip. It will try again later.";

    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);
    private static readonly int Floor = (int)TimeSpan.FromSeconds(60).TotalMilliseconds;

    [Fact]
    public void Check_stopped_by_off_and_back_is_not_paused()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        OffAndBack(rig, UpdateMode.Auto);

        rig.Host.FinishPass();

        Assert.NotEqual(PausedLine, rig.Flow.View.StatusLine);
        Assert.Contains("update check: stopped by a switch to Off", rig.Host.Lines);
        Assert.Null(rig.State.LastCheck);
    }

    [Fact]
    public void Check_stopped_by_off_and_back_checks_again_soon()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        OffAndBack(rig, UpdateMode.Auto);

        rig.Host.FinishPass();
        Assert.Equal(Floor, rig.Host.LastDelay);
        rig.Host.FireLast();

        Assert.Single(rig.Host.Passes);
        rig.Host.FinishPass();
        Assert.Equal("0.2.0", rig.State.HighestSeen);
    }

    [Fact]
    public void Check_stopped_by_off_and_back_to_ask_is_not_paused()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.Host.FireLast();
        OffAndBack(rig, UpdateMode.Ask);

        rig.Host.FinishPass();

        Assert.NotEqual(PausedLine, rig.Flow.View.StatusLine);
        Assert.Equal(Floor, rig.Host.LastDelay);
    }

    [Fact]
    public void Download_stopped_by_off_and_back_is_not_paused()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        OffAndBack(rig, UpdateMode.Auto);

        rig.Host.FinishPass();

        Assert.NotEqual(PausedLine, rig.Flow.View.StatusLine);
        Assert.Contains("update download: stopped by a switch to Off", rig.Host.Lines);
        Assert.Null(rig.State.Pending);
    }

    [Fact]
    public void Download_stopped_by_off_and_back_downloads_again_soon()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        OffAndBack(rig, UpdateMode.Auto);

        rig.Host.FinishPass();
        Assert.Equal(Floor, rig.Host.LastDelay);
        rig.Host.FireLast();

        Assert.Single(rig.Host.Passes);
        rig.Host.FinishPass();
        Assert.Equal(rig.PendingSetup, rig.State.Pending);
    }

    [Fact]
    public void Check_now_stopped_by_a_game_after_off_and_back_is_still_paused()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.Flow.CheckNow();
        OffAndBack(rig, UpdateMode.Ask);
        rig.Host.Surface = Game;

        rig.Host.FinishPass();

        Assert.Equal(PausedLine, rig.Flow.View.StatusLine);
        Assert.Equal((int)UpdateDecider.BusyRetry.TotalMilliseconds, rig.Host.LastDelay);
    }

    [Fact]
    public void Next_pass_stopped_by_a_game_is_paused_again()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        OffAndBack(rig, UpdateMode.Auto);
        rig.Host.FinishPass();
        rig.Host.FireLast();
        rig.Host.Surface = Game;

        rig.Host.FinishPass();

        Assert.Equal(PausedLine, rig.Flow.View.StatusLine);
        Assert.Equal((int)UpdateDecider.BusyRetry.TotalMilliseconds, rig.Host.LastDelay);
    }

    private static void OffAndBack(FlowRig rig, UpdateMode back)
    {
        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        rig.Host.Mode = back;
        rig.Flow.SettingsChanged();
    }
}
