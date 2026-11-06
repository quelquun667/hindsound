using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>The hotkey manager could not be created: the window applying shortcuts must not wipe out what the icon says about it.</summary>
public sealed class HotkeysWithoutManagerTests
{
    private static readonly HotkeySettings Set = new() { SaveDefault = "Ctrl+Shift+F10", CopyLast = "Ctrl+Alt+C" };

    [Fact]
    public void Every_action_gets_an_answer()
    {
        var results = HotkeyManager.WithoutManager(Set);

        Assert.Equal(Enum.GetValues<HotkeyAction>(), results.Select(r => r.Action));
    }

    [Theory]
    [InlineData(HotkeyAction.SaveDefault, "Ctrl+Shift+F10")]
    [InlineData(HotkeyAction.CopyLast, "Ctrl+Alt+C")]
    public void A_shortcut_that_is_set_does_not_work(HotkeyAction action, string gesture)
    {
        var result = HotkeyManager.WithoutManager(Set).Single(r => r.Action == action);

        Assert.Equal(HotkeyStatus.Unavailable, result.Status);
        Assert.Equal(gesture, result.Gesture);
        Assert.False(result.Ok);
    }

    [Fact]
    public void A_shortcut_that_is_not_set_is_no_problem()
    {
        var result = HotkeyManager.WithoutManager(Set).Single(r => r.Action == HotkeyAction.Save15s);

        Assert.Equal(HotkeyStatus.NotSet, result.Status);
        Assert.True(result.Ok);
    }

    [Fact]
    public void The_tip_of_the_icon_keeps_saying_it()
    {
        var (state, tip) = StatusTransitions.TrayOf(null, HotkeyManager.WithoutManager(Set), importantClipWaiting: false);

        Assert.Equal(Hindsound.Ui.TrayState.Error, state);
        Assert.Contains(HotkeyResult.UnavailableMessage, tip);
    }

    [Fact]
    public void A_save_shortcut_turned_off_takes_the_problem_away()
    {
        var results = HotkeyManager.WithoutManager(new HotkeySettings { SaveDefault = null });

        var (state, tip) = StatusTransitions.TrayOf(null, results, importantClipWaiting: false);

        Assert.NotEqual(Hindsound.Ui.TrayState.Error, state);
        Assert.DoesNotContain(HotkeyResult.UnavailableMessage, tip);
    }
}
