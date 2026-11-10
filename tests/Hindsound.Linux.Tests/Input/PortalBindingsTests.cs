using Hindsound.Input;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>What the GlobalShortcuts portal holds for the app, without a bus. Pure logic: runs everywhere.</summary>
public sealed class PortalBindingsTests
{
    private static readonly HotkeyGesture CtrlShiftF10 = new(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79);
    private static readonly HotkeyGesture CtrlShiftP = new(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 'P');

    private static HotkeyRequest Ask(HotkeyAction action, HotkeyGesture? key) => new(action, key);

    private static (PortalBindings State, PortalBatch Batch) TwoAsked()
    {
        var state = new PortalBindings();
        var (_, batch) = state.Take([Ask(HotkeyAction.SaveDefault, CtrlShiftF10), Ask(HotkeyAction.TogglePause, CtrlShiftP)]);
        Assert.NotNull(batch);
        return (state, batch);
    }

    [Fact]
    public void Keys_asked_are_pending_and_offered_together_with_their_words()
    {
        var state = new PortalBindings();
        var (answers, batch) = state.Take([Ask(HotkeyAction.TogglePause, CtrlShiftP), Ask(HotkeyAction.SaveDefault, CtrlShiftF10), Ask(HotkeyAction.CopyLast, null)]);

        Assert.Equal(new[] { HotkeyBindingState.Pending, HotkeyBindingState.Pending, HotkeyBindingState.Unbound }, answers.Select(a => a.State));
        Assert.True(state.Pending);
        Assert.NotNull(batch);
        Assert.Equal(state.Waiting, batch.Generation);
        Assert.Equal(
            new[] { new PortalOffer("save-clip", "Save a clip", "CTRL+SHIFT+F10"), new PortalOffer("pause", "Pause or resume", "CTRL+SHIFT+p") },
            batch.Offers);
    }

    [Fact]
    public void A_success_binds_each_listed_shortcut_with_the_key_the_portal_words()
    {
        var (state, batch) = TwoAsked();

        Assert.True(state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", "Save a clip", "CTRL+SHIFT+F10"), new PortalShortcut("pause", "Pause or resume", "Ctrl+Alt+P")]));

        Assert.False(state.Pending);
        Assert.Equal(new HotkeyBinding(HotkeyAction.SaveDefault, CtrlShiftF10, HotkeyBindingState.Bound), state.Current(HotkeyAction.SaveDefault));

        // The user chose another key: the one bound is the portal's, not the one proposed.
        Assert.Equal(new HotkeyGesture(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 'P'), state.BoundKey(HotkeyAction.TogglePause));
    }

    [Fact]
    public void Words_that_are_not_a_key_are_kept_as_the_text_shown()
    {
        var (state, batch) = TwoAsked();
        state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", null, " Strg+Umschalt+F10 "), new PortalShortcut("pause", null, "")]);

        Assert.Equal(new HotkeyBinding(HotkeyAction.SaveDefault, null, HotkeyBindingState.Bound, "Strg+Umschalt+F10"), state.Current(HotkeyAction.SaveDefault));
        Assert.Equal(new HotkeyBinding(HotkeyAction.TogglePause, null, HotkeyBindingState.Bound), state.Current(HotkeyAction.TogglePause));
        Assert.Null(state.BoundKey(HotkeyAction.SaveDefault));
    }

    [Fact]
    public void A_shortcut_the_answer_leaves_out_is_unavailable()
    {
        var (state, batch) = TwoAsked();
        state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("pause", null, "CTRL+SHIFT+p")]);

        Assert.Equal(HotkeyBindingState.Unavailable, state.Current(HotkeyAction.SaveDefault).State);
        Assert.Equal(HotkeyBindingState.Bound, state.Current(HotkeyAction.TogglePause).State);
    }

    [Fact]
    public void A_cancelled_dialog_leaves_every_key_unavailable_and_the_same_keys_are_asked_again()
    {
        var (state, batch) = TwoAsked();
        Assert.True(state.Answered(batch.Generation, 1, null));

        Assert.All(new[] { HotkeyAction.SaveDefault, HotkeyAction.TogglePause }, a => Assert.Equal(HotkeyBindingState.Unavailable, state.Current(a).State));
        var (answers, again) = state.Take([Ask(HotkeyAction.SaveDefault, CtrlShiftF10)]);
        Assert.NotNull(again);
        Assert.Equal(HotkeyBindingState.Pending, answers[0].State);
    }

    [Fact]
    public void The_same_keys_once_bound_send_nothing_new()
    {
        var (state, batch) = TwoAsked();
        state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", null, "CTRL+SHIFT+F10"), new PortalShortcut("pause", null, "CTRL+SHIFT+p")]);

        var (answers, next) = state.Take([Ask(HotkeyAction.SaveDefault, CtrlShiftF10), Ask(HotkeyAction.TogglePause, CtrlShiftP)]);

        Assert.Null(next);
        Assert.All(answers, a => Assert.Equal(HotkeyBindingState.Bound, a.State));
    }

    [Fact]
    public void An_answer_overtaken_by_a_newer_batch_changes_nothing()
    {
        var (state, first) = TwoAsked();
        var (_, second) = state.Take([Ask(HotkeyAction.Save15s, CtrlShiftF10)]);
        Assert.NotNull(second);
        Assert.True(second.Generation > first.Generation);

        Assert.False(state.Answered(first.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", null, "CTRL+SHIFT+F10")]));
        Assert.True(state.Pending);
        Assert.Equal(HotkeyBindingState.Pending, state.Current(HotkeyAction.SaveDefault).State);
        Assert.Equal(3, second.Offers.Count);
    }

    [Fact]
    public void A_released_key_is_unbound_at_once_and_the_rest_offered_again()
    {
        var (state, batch) = TwoAsked();
        state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", null, "CTRL+SHIFT+F10"), new PortalShortcut("pause", null, "CTRL+SHIFT+p")]);

        var (answers, next) = state.Take([Ask(HotkeyAction.TogglePause, null)]);

        Assert.Equal(HotkeyBindingState.Unbound, answers[0].State);
        Assert.NotNull(next);
        Assert.Equal(new[] { "save-clip" }, next.Offers.Select(o => o.Id));

        var (_, last) = state.Take([Ask(HotkeyAction.SaveDefault, null)]);
        Assert.NotNull(last);
        Assert.Empty(last.Offers);
        Assert.False(state.Pending);
    }

    [Fact]
    public void Keys_the_desktop_changes_itself_are_followed()
    {
        var (state, batch) = TwoAsked();
        state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", null, "CTRL+SHIFT+F10"), new PortalShortcut("pause", null, "CTRL+SHIFT+p")]);

        Assert.True(state.Changed([new PortalShortcut("save-clip", null, "ALT+F9"), new PortalShortcut("unknown", null, "ALT+F8")]));
        Assert.Equal(new HotkeyGesture(HotkeyGesture.ModAlt, 0x78), state.BoundKey(HotkeyAction.SaveDefault));
        Assert.False(state.Changed([new PortalShortcut("save-clip", null, "ALT+F9")]));
    }

    [Fact]
    public void A_lost_session_offers_every_wanted_key_again()
    {
        var (state, batch) = TwoAsked();
        state.Answered(batch.Generation, PortalBindings.Success, [new PortalShortcut("save-clip", null, "CTRL+SHIFT+F10"), new PortalShortcut("pause", null, "CTRL+SHIFT+p")]);

        var again = state.Lost();

        Assert.NotNull(again);
        Assert.Equal(2, again.Offers.Count);
        Assert.True(state.Pending);
        Assert.Null(new PortalBindings().Lost());
    }

    [Fact]
    public void Each_action_has_its_own_id_that_reads_back()
    {
        var actions = Enum.GetValues<HotkeyAction>();
        Assert.Equal(actions.Length, actions.Select(PortalBindings.IdOf).Distinct().Count());
        foreach (var action in actions)
        {
            Assert.True(PortalBindings.TryActionOf(PortalBindings.IdOf(action), out var back));
            Assert.Equal(action, back);
            Assert.False(string.IsNullOrWhiteSpace(PortalBindings.DescriptionOf(action)));
        }

        Assert.False(PortalBindings.TryActionOf("SaveDefault", out _));
        Assert.False(PortalBindings.TryActionOf(null, out _));
    }
}
