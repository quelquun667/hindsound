using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// While the action has left and not answered, it cannot be called back: the box must not close as if nothing had been asked, nor
/// let the text change under the answer that is coming.
/// </summary>
public sealed class NameBoxWaitingTests
{
    private static readonly LibraryCollection[] Taken = [new LibraryCollection { Id = "c1", Name = "pets" }];

    private static NameBoxState Waiting(NameBoxState state)
    {
        Assert.True(state.TryConfirm());
        return state;
    }

    private static NameBoxState WaitingCreate()
    {
        var state = NameBoxState.ForCreate(ChipLine.NewId, () => Taken);
        var field = Assert.Single(NameBox.Build(new FakeCanvas(), state, () => { }, () => { }).OfType<TextField>());
        field.Typed("memes", null);
        return Waiting(state);
    }

    private static IReadOnlyList<Widget> Widgets(NameBoxState state) => NameBox.Build(new FakeCanvas(), state, () => { }, () => { });

    [Theory]
    [InlineData(Vk.Escape, NameBox.NameId)]
    [InlineData(Vk.Escape, NameBox.CancelId)]
    [InlineData(Vk.Escape, NameBox.ConfirmId)]
    [InlineData(Vk.Enter, NameBox.NameId)]
    public void The_keys_that_close_or_confirm_are_swallowed_while_waiting(uint vk, string focus)
    {
        Assert.Equal(NameBoxKey.Ignore, WaitingCreate().Key(vk, plain: true, focus));
    }

    [Fact]
    public void A_deletion_waiting_swallows_escape_too()
    {
        var state = Waiting(NameBoxState.ForDelete("c1", "pets", CollectionActions.DeleteId));

        Assert.Equal(NameBoxKey.Ignore, state.Key(Vk.Escape, plain: true, NameBox.CancelId));
    }

    [Fact]
    public void The_keys_are_the_box_s_again_once_the_answer_has_come()
    {
        var state = WaitingCreate();

        state.Answered(LibraryOutcome.NameTaken);

        Assert.Equal(NameBoxKey.Cancel, state.Key(Vk.Escape, plain: true, NameBox.NameId));
        Assert.Equal(NameBoxKey.Confirm, state.Key(Vk.Enter, plain: true, NameBox.NameId));
    }

    [Fact]
    public void Other_keys_stay_with_the_widgets_while_waiting()
    {
        Assert.Equal(NameBoxKey.PassOn, WaitingCreate().Key(Vk.Left, plain: true, NameBox.NameId));
        Assert.Equal(NameBoxKey.PassOn, WaitingCreate().Key(Vk.Escape, plain: false, NameBox.NameId));
    }

    [Fact]
    public void Cancel_and_the_field_cannot_be_used_while_waiting()
    {
        var cancelled = 0;
        var state = WaitingCreate();
        var widgets = NameBox.Build(new FakeCanvas(), state, () => { }, () => cancelled++);
        var cancel = widgets.Single(w => w.Id == NameBox.CancelId);
        var field = widgets.Single(w => w.Id == NameBox.NameId);

        cancel.OnRelease(inside: true);
        cancel.OnKey(Vk.Enter);

        Assert.Equal(0, cancelled);
        Assert.False(cancel.CanFocus);
        Assert.False(field.CanFocus);
    }

    [Fact]
    public void Cancel_and_the_field_work_again_when_the_answer_keeps_the_box_open()
    {
        var state = WaitingCreate();
        state.Answered(LibraryOutcome.NameTaken);

        var widgets = Widgets(state);

        Assert.True(widgets.Single(w => w.Id == NameBox.CancelId).CanFocus);
        Assert.True(widgets.Single(w => w.Id == NameBox.NameId).CanFocus);
    }
}
