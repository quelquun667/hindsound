using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>Where an update may install on the way out, and what "Save it, then update" waits for.</summary>
public class UpdateExitTests
{
    [Fact]
    public void Only_quit_from_the_menu_chooses_quit()
    {
        foreach (var command in Enum.GetValues<TrayCommand>())
        {
            Assert.Equal(command == TrayCommand.Quit, UpdateExit.ChoosesQuit(command));
        }
    }

    [Fact]
    public void Leaving_without_quit_never_asks_the_flow()
    {
        var asked = false;

        Assert.False(UpdateExit.InstallOnLeaving(false, () => asked = true));
        Assert.False(asked);
    }

    [Fact]
    public void Quit_asks_the_flow_and_says_what_it_answers()
    {
        Assert.True(UpdateExit.InstallOnLeaving(true, () => true));
        Assert.False(UpdateExit.InstallOnLeaving(true, () => false));
        Assert.False(UpdateExit.InstallOnLeaving(true, null));
    }

    [Fact]
    public void Nothing_waiting_does_nothing()
    {
        var save = new SaveThenUpdate();

        Assert.Equal(SaveThenUpdateStep.None, save.ClipEnded(saved: true, clipsInProgress: false));
        Assert.Equal(SaveThenUpdateStep.None, save.ClipEnded(saved: false, clipsInProgress: false));
        Assert.False(save.ClipFailed);
    }

    [Fact]
    public void Saved_clip_updates_once()
    {
        var save = new SaveThenUpdate();
        save.Start();

        Assert.Equal(SaveThenUpdateStep.Update, save.ClipEnded(saved: true, clipsInProgress: false));
        Assert.False(save.Waiting);
        Assert.Equal(SaveThenUpdateStep.None, save.ClipEnded(saved: true, clipsInProgress: false));
    }

    [Fact]
    public void Failed_clip_does_not_update()
    {
        var save = new SaveThenUpdate();
        save.Start();

        Assert.Equal(SaveThenUpdateStep.ClipFailed, save.ClipEnded(saved: false, clipsInProgress: false));
        Assert.True(save.ClipFailed);
        Assert.False(save.Waiting);
    }

    [Fact]
    public void Waits_for_every_clip_and_one_failure_is_enough()
    {
        var save = new SaveThenUpdate();
        save.Start();

        // A hotkey started another clip while the first was written: the first failed, the second is fine.
        Assert.Equal(SaveThenUpdateStep.Wait, save.ClipEnded(saved: false, clipsInProgress: true));
        Assert.Equal(SaveThenUpdateStep.ClipFailed, save.ClipEnded(saved: true, clipsInProgress: false));
    }

    [Fact]
    public void Waits_for_every_clip_before_updating()
    {
        var save = new SaveThenUpdate();
        save.Start();

        Assert.Equal(SaveThenUpdateStep.Wait, save.ClipEnded(saved: true, clipsInProgress: true));
        Assert.True(save.Waiting);
        Assert.Equal(SaveThenUpdateStep.Update, save.ClipEnded(saved: true, clipsInProgress: false));
    }

    [Fact]
    public void Closing_the_window_drops_it()
    {
        var save = new SaveThenUpdate();
        save.Start();
        save.Stop();

        Assert.Equal(SaveThenUpdateStep.None, save.ClipEnded(saved: true, clipsInProgress: false));
    }

    [Fact]
    public void A_new_try_forgets_the_failed_clip()
    {
        var save = new SaveThenUpdate();
        save.Start();
        save.ClipEnded(saved: false, clipsInProgress: false);

        save.Start();

        Assert.False(save.ClipFailed);
        Assert.Equal(SaveThenUpdateStep.Update, save.ClipEnded(saved: true, clipsInProgress: false));
    }
}
