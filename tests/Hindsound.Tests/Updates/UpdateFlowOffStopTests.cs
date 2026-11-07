using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>An automatic pass stopped by a switch to Off: no game paused it, and nothing tries again.</summary>
public class UpdateFlowOffStopTests
{
    private const string PausedLine = "Paused by a game or a clip. It will try again later.";

    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);

    [Fact]
    public void Check_stopped_by_off_is_not_paused()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        var timers = rig.Host.Timers.Count;

        rig.Host.FinishPass();

        Assert.NotEqual(PausedLine, rig.Flow.View.StatusLine);
        Assert.Equal(timers, rig.Host.Timers.Count);
        Assert.Contains("update check: stopped, updates are off", rig.Host.Lines);
        Assert.Null(rig.State.LastCheck);
    }

    [Fact]
    public void Download_stopped_by_off_is_not_paused()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        var timers = rig.Host.Timers.Count;

        rig.Host.FinishPass();

        Assert.NotEqual(PausedLine, rig.Flow.View.StatusLine);
        Assert.Equal(timers, rig.Host.Timers.Count);
        Assert.Contains("update download: stopped, updates are off", rig.Host.Lines);
        Assert.Null(rig.State.Pending);
    }

    [Fact]
    public void Check_now_in_off_stopped_by_a_game_is_still_paused()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Off;
        rig.Launch();
        rig.Flow.CheckNow();
        rig.Host.Surface = Game;

        rig.Host.FinishPass();

        Assert.Equal(PausedLine, rig.Flow.View.StatusLine);
        Assert.Equal((int)UpdateDecider.BusyRetry.TotalMilliseconds, rig.Host.LastDelay);
    }
}
