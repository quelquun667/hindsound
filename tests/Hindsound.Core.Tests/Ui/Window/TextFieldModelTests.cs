using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The text field of a window without a native box behaves as the Windows EDIT of the settings window.</summary>
public class TextFieldModelTests
{
    private const uint Ctrl = HotkeyGesture.ModControl, Shift = HotkeyGesture.ModShift, Alt = HotkeyGesture.ModAlt;
    private const uint Insert = 0x2D, Delete = 0x2E;

    private string? _clipboard;

    private TextFieldModel Field(string text, int caret = -1)
    {
        var field = new TextFieldModel(() => _clipboard, t => _clipboard = t);
        field.SetText(text, selectAll: false);
        if (caret >= 0)
        {
            field.Select(caret, caret);
        }

        return field;
    }

    // ---------------------------------------------------------------- keys given back

    [Theory]
    [InlineData(Vk.Tab, 0u)]
    [InlineData(Vk.Tab, Shift)]
    [InlineData(Vk.Enter, 0u)]
    [InlineData(Vk.Enter, Ctrl)]
    [InlineData(Vk.Escape, 0u)]
    public void Tab_enter_and_escape_go_back_to_the_screen(uint vk, uint modifiers)
    {
        var field = Field("abc");
        Assert.Equal(TextKeyResult.NotMine, field.Key(vk, modifiers));
        Assert.Equal("abc", field.Text);
    }

    [Theory]
    [InlineData(0x74u, 0u)] // F5
    [InlineData('S', Ctrl)]
    [InlineData('X', Alt)]
    [InlineData(Vk.PageDown, 0u)]
    [InlineData('Q', 0u)] // a letter types through TextInput, not here
    public void Every_other_key_stays_in_the_field_and_changes_nothing(uint vk, uint modifiers)
    {
        var field = Field("abc", 1);
        Assert.Equal(TextKeyResult.Handled, field.Key(vk, modifiers));
        Assert.Equal("abc", field.Text);
        Assert.Equal(1, field.Caret);
    }

    // ---------------------------------------------------------------- set by the program

    [Fact]
    public void Text_set_by_the_program_is_all_selected_when_asked_and_not_cut_to_the_limit()
    {
        var field = Field("");
        var longText = new string('x', 300);
        field.SetText(longText, selectAll: true);
        Assert.Equal(longText, field.Text);
        Assert.Equal(0, field.SelectionStart);
        Assert.Equal(300, field.SelectionEnd);
        Assert.Equal(300, field.Caret);
    }

    [Fact]
    public void Text_set_by_the_program_leaves_nothing_to_undo()
    {
        var field = Field("one");
        field.Type("!");
        field.SetText("two", selectAll: false);
        Assert.Equal(TextKeyResult.Handled, field.Key('Z', Ctrl));
        Assert.Equal("two", field.Text);
    }

    // ---------------------------------------------------------------- typing

    [Fact]
    public void Typing_inserts_at_the_caret()
    {
        var field = Field("hllo", 1);
        Assert.True(field.Type("e"));
        Assert.Equal("hello", field.Text);
        Assert.Equal(2, field.Caret);
        Assert.False(field.HasSelection);
    }

    [Fact]
    public void Typing_replaces_the_selection()
    {
        var field = Field("");
        field.SetText("old name", selectAll: true);
        Assert.True(field.Type("n"));
        Assert.Equal("n", field.Text);
        Assert.Equal(1, field.Caret);
    }

    [Fact]
    public void Typing_stops_at_the_limit_of_the_windows_box()
    {
        var field = Field(new string('a', TextFieldModel.MaxLength - 2));
        Assert.True(field.Type("bcd"));
        Assert.Equal(TextFieldModel.MaxLength, field.Text.Length);
        Assert.EndsWith("bc", field.Text);
        Assert.False(field.Type("e"));
        Assert.Equal(TextFieldModel.MaxLength, field.Text.Length);
    }

    [Fact]
    public void A_full_field_takes_text_in_place_of_its_selection()
    {
        var field = Field(new string('a', TextFieldModel.MaxLength));
        field.Select(0, 3);
        Assert.True(field.Type("xyz12"));
        Assert.Equal(TextFieldModel.MaxLength, field.Text.Length);
        Assert.StartsWith("xyza", field.Text);
    }

    [Fact]
    public void The_limit_never_splits_a_surrogate_pair()
    {
        var field = Field(new string('a', TextFieldModel.MaxLength - 1));
        Assert.False(field.Type("\U0001F600"));
        Assert.Equal(TextFieldModel.MaxLength - 1, field.Text.Length);
    }

    [Fact]
    public void Control_characters_are_dropped_and_a_line_break_ends_the_text()
    {
        var field = Field("");
        Assert.True(field.Type("a\tb\u007Fc\r\nsecond line"));
        Assert.Equal("abc", field.Text);
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public void Backspace_and_delete_take_one_character_each_side()
    {
        var field = Field("abcd", 2);
        Assert.Equal(TextKeyResult.Changed, field.Key(Vk.Back, 0));
        Assert.Equal("acd", field.Text);
        Assert.Equal(1, field.Caret);
        Assert.Equal(TextKeyResult.Changed, field.Key(Delete, 0));
        Assert.Equal("ad", field.Text);
        Assert.Equal(1, field.Caret);
    }

    [Fact]
    public void Backspace_at_the_start_and_delete_at_the_end_change_nothing()
    {
        var field = Field("ab", 0);
        Assert.Equal(TextKeyResult.Handled, field.Key(Vk.Back, 0));
        field.Select(2, 2);
        Assert.Equal(TextKeyResult.Handled, field.Key(Delete, 0));
        Assert.Equal("ab", field.Text);
    }

    [Fact]
    public void Backspace_and_delete_take_the_selection_alone()
    {
        var field = Field("abcdef");
        field.Select(4, 1);
        Assert.Equal(TextKeyResult.Changed, field.Key(Vk.Back, 0));
        Assert.Equal("aef", field.Text);
        Assert.Equal(1, field.Caret);
        field.Select(1, 2);
        Assert.Equal(TextKeyResult.Changed, field.Key(Delete, 0));
        Assert.Equal("af", field.Text);
    }

    // The native box of Windows deletes from the start of this word to its caret on Ctrl+Backspace, with the same word rule as Ctrl+Left.
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("goal", 4, 0)]
    [InlineData("goal replay", 11, 5)]
    [InlineData("goal replay", 5, 0)]
    [InlineData("goal replay", 7, 5)]
    [InlineData("goal   replay  ", 15, 7)]
    [InlineData("goal   ", 7, 0)]
    [InlineData("   ", 3, 0)]
    [InlineData("goal replay", 0, 0)]
    [InlineData("goal replay", 99, 5)]
    [InlineData("goal replay", -3, 0)]
    public void The_word_before_the_caret_starts_where_Ctrl_left_would_go(string text, int caret, int start)
    {
        Assert.Equal(start, TextFieldModel.WordStartBefore(text, caret));
        Assert.Equal(start, Field(text, Math.Clamp(caret, 0, text.Length)).WordBefore(Math.Clamp(caret, 0, text.Length)));
    }

    [Fact]
    public void Ctrl_backspace_and_ctrl_delete_take_a_word()
    {
        var field = Field("one two three", 8);
        Assert.Equal(TextKeyResult.Changed, field.Key(Vk.Back, Ctrl));
        Assert.Equal("one three", field.Text);
        Assert.Equal(4, field.Caret);
        Assert.Equal(TextKeyResult.Changed, field.Key(Delete, Ctrl));
        Assert.Equal("one ", field.Text);
    }

    // ---------------------------------------------------------------- moving

    [Fact]
    public void Arrows_move_by_one_and_stop_at_the_ends()
    {
        var field = Field("ab", 0);
        field.Key(Vk.Left, 0);
        Assert.Equal(0, field.Caret);
        field.Key(Vk.Right, 0);
        Assert.Equal(1, field.Caret);
        field.Key(Vk.Down, 0);
        field.Key(Vk.Right, 0);
        Assert.Equal(2, field.Caret);
        field.Key(Vk.Up, 0);
        Assert.Equal(1, field.Caret);
    }

    [Fact]
    public void An_arrow_without_shift_leaves_a_selection_at_its_edge()
    {
        var field = Field("abcdef");
        field.Select(1, 4);
        field.Key(Vk.Left, 0);
        Assert.False(field.HasSelection);
        Assert.Equal(1, field.Caret);
        field.Select(1, 4);
        field.Key(Vk.Right, 0);
        Assert.False(field.HasSelection);
        Assert.Equal(4, field.Caret);
    }

    [Fact]
    public void Home_and_end_go_to_the_ends_and_shift_selects_from_the_anchor()
    {
        var field = Field("hello", 2);
        field.Key(Vk.End, Shift);
        Assert.Equal("llo", field.SelectedText);
        field.Key(Vk.Home, Shift);
        Assert.Equal("he", field.SelectedText);
        Assert.Equal(0, field.Caret);
        field.Key(Vk.End, 0);
        Assert.False(field.HasSelection);
        Assert.Equal(5, field.Caret);
    }

    [Fact]
    public void Shift_arrows_grow_and_shrink_the_selection()
    {
        var field = Field("hello", 1);
        field.Key(Vk.Right, Shift);
        field.Key(Vk.Right, Shift);
        Assert.Equal("el", field.SelectedText);
        field.Key(Vk.Left, Shift);
        Assert.Equal("e", field.SelectedText);
    }

    [Fact]
    public void Ctrl_arrows_go_from_word_to_word()
    {
        var field = Field("one  two three", 0);
        field.Key(Vk.Right, Ctrl);
        Assert.Equal(5, field.Caret);
        field.Key(Vk.Right, Ctrl);
        Assert.Equal(9, field.Caret);
        field.Key(Vk.Right, Ctrl);
        Assert.Equal(14, field.Caret);
        field.Key(Vk.Left, Ctrl);
        Assert.Equal(9, field.Caret);
        field.Key(Vk.Left, Ctrl | Shift);
        Assert.Equal("two ", field.SelectedText);
    }

    [Fact]
    public void Ctrl_a_selects_all()
    {
        var field = Field("hello", 2);
        Assert.Equal(TextKeyResult.Handled, field.Key('A', Ctrl));
        Assert.Equal("hello", field.SelectedText);
    }

    [Fact]
    public void A_double_click_selects_the_word_under_it()
    {
        var field = Field("one two three");
        field.SelectWordAt(5);
        Assert.Equal("two", field.SelectedText);
    }

    // ---------------------------------------------------------------- clipboard

    [Fact]
    public void Copy_puts_the_selection_on_the_clipboard_and_keeps_the_text()
    {
        var field = Field("hello");
        field.Select(1, 3);
        Assert.Equal(TextKeyResult.Handled, field.Key('C', Ctrl));
        Assert.Equal("el", _clipboard);
        Assert.Equal("hello", field.Text);
        _clipboard = null;
        field.Key(Insert, Ctrl);
        Assert.Equal("el", _clipboard);
    }

    [Fact]
    public void Copy_with_nothing_selected_leaves_the_clipboard_alone()
    {
        _clipboard = "kept";
        var field = Field("hello", 2);
        field.Key('C', Ctrl);
        Assert.Equal("kept", _clipboard);
        Assert.Equal(TextKeyResult.Handled, field.Key('X', Ctrl));
        Assert.Equal("kept", _clipboard);
    }

    [Fact]
    public void Cut_moves_the_selection_to_the_clipboard()
    {
        var field = Field("hello");
        field.Select(1, 3);
        Assert.Equal(TextKeyResult.Changed, field.Key('X', Ctrl));
        Assert.Equal("el", _clipboard);
        Assert.Equal("hlo", field.Text);
        field.Select(0, 1);
        Assert.Equal(TextKeyResult.Changed, field.Key(Delete, Shift));
        Assert.Equal("h", _clipboard);
        Assert.Equal("lo", field.Text);
    }

    [Fact]
    public void Paste_replaces_the_selection_with_the_first_line_of_the_clipboard()
    {
        _clipboard = "new\nmore";
        var field = Field("an old name");
        field.Select(3, 6);
        Assert.Equal(TextKeyResult.Changed, field.Key('V', Ctrl));
        Assert.Equal("an new name", field.Text);
        Assert.Equal(6, field.Caret);
        _clipboard = "!";
        Assert.Equal(TextKeyResult.Changed, field.Key(Insert, Shift));
        Assert.Equal("an new! name", field.Text);
    }

    [Fact]
    public void Paste_of_an_empty_clipboard_changes_nothing()
    {
        _clipboard = null;
        var field = Field("abc");
        field.SelectAll();
        Assert.Equal(TextKeyResult.Handled, field.Key('V', Ctrl));
        Assert.Equal("abc", field.Text);
    }

    [Fact]
    public void Paste_is_cut_to_the_limit()
    {
        _clipboard = new string('b', 100);
        var field = Field(new string('a', TextFieldModel.MaxLength - 10));
        field.Key('V', Ctrl);
        Assert.Equal(TextFieldModel.MaxLength, field.Text.Length);
    }

    // ---------------------------------------------------------------- undo

    [Fact]
    public void Ctrl_z_undoes_the_last_change_and_a_second_one_redoes_it()
    {
        var field = Field("abc", 3);
        field.Type("d");
        Assert.Equal(TextKeyResult.Changed, field.Key('Z', Ctrl));
        Assert.Equal("abc", field.Text);
        Assert.Equal(3, field.Caret);
        Assert.Equal(TextKeyResult.Changed, field.Key('Z', Ctrl));
        Assert.Equal("abcd", field.Text);
    }
}
