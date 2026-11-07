using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>An announcement waiting in a game was written for the mode it was made in: another mode takes it back.</summary>
public class UpdateFlowAnnounceModeTests
{
    private static readonly UpdateVersion Next = FlowRig.Next;
    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);

    // Auto, in a game: "Update ready / Installs when Hindsound closes." waits for the desktop.
    private static FlowRig ReadyInAGame()
    {
        var rig = new FlowRig();
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        rig.Host.Surface = Game;
        rig.Host.FinishPass();
        Assert.Equal([(Next, UpdateMode.Auto)], rig.Host.Announced);
        return rig;
    }

    [Fact]
    public void Ask_takes_back_the_auto_announcement_and_announces_again()
    {
        using var rig = ReadyInAGame();

        rig.Host.Mode = UpdateMode.Ask;
        rig.Flow.SettingsChanged();
        Assert.Equal(1, rig.Host.Withdrawn);

        rig.Host.FireLast();

        Assert.Equal([(Next, UpdateMode.Auto), (Next, UpdateMode.Ask)], rig.Host.Announced);
    }

    [Fact]
    public void Off_takes_back_the_waiting_announcement()
    {
        using var rig = ReadyInAGame();

        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();

        Assert.Equal(1, rig.Host.Withdrawn);
        Assert.Single(rig.Host.Announced);
    }

    [Fact]
    public void Same_mode_keeps_the_waiting_announcement()
    {
        using var rig = ReadyInAGame();

        rig.Flow.SettingsChanged();

        Assert.Equal(0, rig.Host.Withdrawn);
    }

    [Fact]
    public void Shown_announcement_is_not_taken_back()
    {
        using var rig = ReadyInAGame();
        rig.Flow.Shown();

        rig.Host.Mode = UpdateMode.Ask;
        rig.Flow.SettingsChanged();

        Assert.Equal(0, rig.Host.Withdrawn);
    }
}
