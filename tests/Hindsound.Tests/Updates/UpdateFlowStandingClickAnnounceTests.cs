using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// A click that stood during its download and did not end in a Setup: the version is announced as a download without a
/// click announces it, with the same rules, and no pass starts from there.
/// </summary>
public class UpdateFlowStandingClickAnnounceTests
{
    private static readonly UpdateVersion Next = FlowRig.Next;
    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);
    private static readonly UpdateSurface Clip = new(FullScreen: false, Presentation: false, ClipInProgress: true, SessionEnding: false);

    [Fact]
    public void Click_dropped_by_a_clip_announces_ready_in_auto()
    {
        using var rig = ClickedInAuto();
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Equal("Not now: a game or a clip is running.", rig.Flow.View.CardLine);
        Assert.Equal([(Next, UpdateMode.Auto)], rig.Host.Announced);
        Assert.Empty(rig.Host.Passes);
    }

    [Fact]
    public void Click_dropped_by_a_clip_announces_available_in_ask()
    {
        using var rig = ClickedInAuto();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Flow.SettingsChanged();
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Equal([(Next, UpdateMode.Ask)], rig.Host.Announced);
    }

    [Fact]
    public void Click_whose_setup_did_not_start_announces_available_in_ask()
    {
        using var rig = ClickedInAuto();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Flow.SettingsChanged();
        rig.Host.BetweenWorkAndDone = ChangeSetup(rig);

        rig.Host.FinishPass();

        Assert.Empty(rig.Started);
        Assert.Equal([(Next, UpdateMode.Ask)], rig.Host.Announced);
    }

    [Fact]
    public void Click_whose_setup_did_not_start_in_auto_announces_nothing_and_downloads_nothing_at_once()
    {
        using var rig = ClickedInAuto();
        rig.Host.BetweenWorkAndDone = ChangeSetup(rig);

        rig.Host.FinishPass();

        Assert.Empty(rig.Started);
        Assert.Empty(rig.Host.Announced);
        Assert.Empty(rig.Host.Passes);
        Assert.Equal((int)UpdateDecider.CheckInterval.TotalMilliseconds, rig.Host.LastDelay);
    }

    [Fact]
    public void Click_dropped_by_a_clip_announces_once_per_version()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        Assert.Single(rig.Host.Announced);
        rig.Flow.UpdateClicked();
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Single(rig.Host.Announced);
    }

    [Fact]
    public void Click_dropped_by_a_clip_does_not_announce_a_skipped_version()
    {
        using var rig = new FlowRig();
        rig.Host.Skipped = Next;
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        Assert.Equal(ClickKind.Downloading, rig.Flow.UpdateClicked().Kind);
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Empty(rig.Host.Announced);
    }

    [Fact]
    public void Click_dropped_by_a_clip_does_not_announce_a_version_out_of_attempts()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData().WithAttempt(Next).WithAttempt(Next).WithAttempt(Next));
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        Assert.Equal(ClickKind.Downloading, rig.Flow.UpdateClicked().Kind);
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Empty(rig.Host.Announced);
    }

    [Fact]
    public void Click_dropped_by_a_clip_does_not_announce_while_the_first_run_card_is_open()
    {
        using var rig = ClickedInAuto();
        rig.Host.FirstRunCardOpen = true;
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Empty(rig.Host.Announced);
    }

    [Fact]
    public void Click_dropped_by_a_clip_does_not_announce_in_off()
    {
        using var rig = ClickedInAuto();
        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();

        Assert.Equal("Not now: a game or a clip is running.", rig.Flow.View.CardLine);
        Assert.Empty(rig.Host.Announced);
    }

    // Auto: a game held the download back after the check, then the user clicked Update. Its download is the pass left.
    private static FlowRig ClickedInAuto()
    {
        var rig = new FlowRig();
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.Surface = Game;
        rig.Host.FinishPass();
        Assert.Empty(rig.Host.Passes);
        Assert.Empty(rig.Host.Announced);

        rig.Host.Surface = default;
        Assert.Equal(ClickKind.Downloading, rig.Flow.UpdateClicked().Kind);
        return rig;
    }

    // Other bytes of the same size: the Setup still reads as downloaded, and the runner's hash refuses it.
    private static Action ChangeSetup(FlowRig rig) =>
        () => File.WriteAllBytes(UpdateFiles.SetupPath(rig.Folder, Next), new byte[rig.Transport.Setup.Length]);
}
