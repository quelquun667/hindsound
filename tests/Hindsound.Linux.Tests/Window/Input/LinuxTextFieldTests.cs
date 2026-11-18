using Hindsound.Input;
using Hindsound.Linux.Window.Input;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window.Input;

/// <summary>The drawn text field of the Linux window: what it takes, what it gives back, and where it draws.</summary>
public class LinuxTextFieldTests
{
    private const uint Ctrl = HotkeyGesture.ModControl, Shift = HotkeyGesture.ModShift;

    // Every character is 7 design units wide: places on the page are then easy to tell.
    private const float Advance = 7;

    private static readonly Box FieldBox = new(100, 50, 200, 30);

    private string? _clipboard;
    private readonly List<string> _changes = [];
    private int _repaints;

    private LinuxTextField Field(string text = "hello", bool focus = true)
    {
        var field = new LinuxTextField(() => _clipboard, t => _clipboard = t)
        {
            Changed = _changes.Add,
            Repaint = () => _repaints++,
        };
        field.Show(FieldBox, text, Face.Mono, 13, resetText: true);
        if (focus)
        {
            field.Focus();
        }

        return field;
    }

    private static RecordingCanvas Paint(LinuxTextField field, double scale = 1)
    {
        var canvas = new RecordingCanvas { Scale = scale };
        field.Paint(canvas);
        return canvas;
    }

    // ---------------------------------------------------------------- keys

    [Fact]
    public void A_reset_selects_the_text_and_is_no_change_of_the_user()
    {
        var field = Field("old");
        Assert.Equal("old", field.Model.SelectedText);
        Assert.Empty(_changes);
        field.TextInput("n");
        Assert.Equal(new[] { "n" }, _changes);
    }

    [Fact]
    public void Showing_again_without_a_reset_keeps_what_was_typed()
    {
        var field = Field("abc");
        field.TextInput("x");
        field.Show(FieldBox with { Y = 80 }, "abc", Face.Mono, 13, resetText: false);
        Assert.Equal("x", field.Text);
        Assert.Equal(80, field.Box.Y);
    }

    [Theory]
    [InlineData(Vk.Tab, 0u)]
    [InlineData(Vk.Tab, Shift)]
    [InlineData(Vk.Enter, 0u)]
    [InlineData(Vk.Escape, 0u)]
    public void Tab_enter_and_escape_go_to_the_screen(uint vk, uint modifiers) =>
        Assert.False(Field().KeyDown(vk, modifiers));

    [Fact]
    public void Editing_keys_are_the_fields_and_a_change_is_told()
    {
        var field = Field("hello");
        Assert.True(field.KeyDown(Vk.End, 0));
        Assert.True(field.KeyDown(Vk.Back, 0));
        Assert.Equal(new[] { "hell" }, _changes);
        Assert.True(field.KeyDown(Vk.Left, 0));
        Assert.True(field.KeyDown(0x74, 0)); // F5: kept, as the box keeps it
        Assert.Single(_changes);
    }

    [Fact]
    public void Without_the_keyboard_the_field_takes_no_key_and_no_text()
    {
        var field = Field("hello", focus: false);
        Assert.False(field.KeyDown(Vk.Back, 0));
        field.TextInput("x");
        Assert.Equal("hello", field.Text);
        Assert.False(field.WantsTextInput);
    }

    [Fact]
    public void Hiding_takes_the_keyboard_away()
    {
        var field = Field();
        field.Hide();
        Assert.False(field.Focused);
        Assert.False(field.KeyDown(Vk.Left, 0));
        Assert.Empty(Paint(field).Calls);
    }

    [Fact]
    public void Clipboard_keys_go_through_the_clipboard_given()
    {
        var field = Field("hello");
        Assert.True(field.KeyDown('C', Ctrl));
        Assert.Equal("hello", _clipboard);
        _clipboard = "bye";
        field.KeyDown('V', Ctrl);
        Assert.Equal(new[] { "bye" }, _changes);
    }

    // ---------------------------------------------------------------- painting

    [Fact]
    public void The_box_is_placed_as_the_windows_edit_is()
    {
        var field = Field("hi");
        var canvas = Paint(field, 1.5);

        // 1.5 times the size, in whole pixels, centred on the field: 30 px tall, at 150, 82, 300 wide.
        var background = canvas.Rects[0];
        Assert.Equal(Theme.Raised, background.Rgb);
        Assert.Equal(150, background.Box.X * 1.5, 3);
        Assert.Equal(82, background.Box.Y * 1.5, 3);
        Assert.Equal(300, background.Box.W * 1.5, 3);
        Assert.Equal(30, background.Box.H * 1.5, 3);
        Assert.Equal((150, 82, 300, 30), (field.InputArea().X, field.InputArea().Y, field.InputArea().W, field.InputArea().H));
        Assert.All(canvas.Texts, t => Assert.Equal(Face.Mono, t.Face));
    }

    [Fact]
    public void The_body_face_stands_for_every_face_but_mono()
    {
        var field = Field();
        field.Show(FieldBox, "a", Face.Title, 13, resetText: true);
        Assert.All(Paint(field).Texts, t => Assert.Equal(Face.Body, t.Face));
    }

    [Fact]
    public void The_selection_shows_only_while_the_field_has_the_keyboard()
    {
        var field = Field("hello", focus: false);
        var canvas = Paint(field);
        Assert.DoesNotContain(canvas.Rects, r => r.Rgb == LinuxTextField.Highlight);
        Assert.Equal("hello", Assert.Single(canvas.Texts).Text);

        field.Focus();
        canvas = Paint(field);
        var highlight = Assert.Single(canvas.Rects, r => r.Rgb == LinuxTextField.Highlight);
        Assert.Equal(5 * Advance, highlight.Box.W, 3);
        Assert.Equal(LinuxTextField.HighlightText, Assert.Single(canvas.Texts).Rgb);
    }

    [Fact]
    public void A_partial_selection_draws_three_runs_side_by_side()
    {
        var field = Field("hello");
        field.Model.Select(1, 3);
        var texts = Paint(field).Texts;
        Assert.Equal(new[] { "h", "el", "lo" }, texts.Select(t => t.Text));
        Assert.Equal(Advance, texts[1].X - texts[0].X, 3);
        Assert.Equal(2 * Advance, texts[2].X - texts[1].X, 3);
        Assert.Equal(new[] { Theme.Text, LinuxTextField.HighlightText, Theme.Text }, texts.Select(t => t.Rgb));
    }

    [Fact]
    public void The_caret_blinks_and_any_key_shows_it_again()
    {
        var field = Field("hello");
        field.KeyDown(Vk.End, 0);
        Assert.Single(Paint(field).CaretRects());

        field.Blink();
        Assert.Empty(Paint(field).CaretRects());

        field.KeyDown(Vk.Left, 0);
        var caret = Assert.Single(Paint(field).CaretRects());
        var text = Paint(field).Texts[0];
        Assert.Equal(text.X + 4 * Advance, caret.Box.X, 3);
    }

    [Fact]
    public void Without_the_keyboard_the_caret_neither_shows_nor_blinks()
    {
        var field = Field("hello", focus: false);
        _repaints = 0;
        field.Blink();
        Assert.Equal(0, _repaints);
        Assert.Empty(Paint(field).CaretRects());
    }

    [Fact]
    public void A_long_text_scrolls_to_keep_the_caret_in_view_and_draws_only_what_fits()
    {
        // 200 wide with a margin of 2 each side: 196, so 28 characters of 7.
        var field = Field(new string('a', 60));
        field.KeyDown(Vk.End, 0);
        var canvas = Paint(field);
        var drawn = string.Concat(canvas.Texts.Select(t => t.Text));
        Assert.InRange(drawn.Length, 15, 28);
        Assert.EndsWith("a", drawn);
        var caret = Assert.Single(canvas.CaretRects());
        Assert.InRange(caret.Box.X, FieldBox.X, FieldBox.Right);

        field.KeyDown(Vk.Home, 0);
        canvas = Paint(field);
        Assert.Equal(FieldBox.X + 2, Assert.Single(canvas.CaretRects()).Box.X, 3);
        Assert.Equal(28, string.Concat(canvas.Texts.Select(t => t.Text)).Length);
    }

    [Fact]
    public void A_scroll_to_the_right_moves_by_a_third_of_the_width()
    {
        var field = Field(new string('a', 60));
        field.KeyDown(Vk.Home, 0);
        Paint(field);
        for (var i = 0; i < 29; i++)
        {
            field.KeyDown(Vk.Right, 0);
        }

        var caret = Assert.Single(Paint(field).CaretRects());

        // The caret left the box by one character: the text moved so that it stands two thirds of the way along.
        Assert.InRange(caret.Box.X - (FieldBox.X + 2), 196 * 2 / 3f - Advance, 196 * 2 / 3f);
    }

    [Fact]
    public void A_composition_left_behind_by_a_lost_focus_does_not_hold_the_keys_when_the_focus_comes_back()
    {
        var field = Field("abc");
        field.KeyDown(Vk.End, 0);
        field.Composition("ni", -1);

        field.Blur();
        field.Composition("", 0);
        field.Focus();

        Assert.Equal("", field.ComposingText);
        Assert.True(field.KeyDown(Vk.Back, 0));
        Assert.Equal("ab", field.Text);
    }

    [Fact]
    public void A_composition_shows_at_the_caret_underlined_and_holds_the_keys()
    {
        var field = Field("ab");
        field.KeyDown(Vk.End, 0);
        field.Composition("xyz", 1);
        var canvas = Paint(field);
        Assert.Equal("abxyz", string.Concat(canvas.Texts.Select(t => t.Text)));
        var underline = Assert.Single(canvas.Rects, r => r.Rgb == Theme.Text && r.Box.W > 1);
        Assert.Equal(3 * Advance, underline.Box.W, 3);
        Assert.Equal(canvas.Texts[0].X + 3 * Advance, Assert.Single(canvas.CaretRects()).Box.X, 3);
        Assert.Equal(field.InputArea().Cursor, (int)MathF.Round(2 + 3 * Advance));

        Assert.True(field.KeyDown(Vk.Back, 0));
        Assert.Equal("ab", field.Text);
        Assert.Empty(_changes);

        field.TextInput("XYZ");
        Assert.Equal("abXYZ", field.Text);
        Assert.Equal("", field.ComposingText);
        Assert.Equal(new[] { "abXYZ" }, _changes);
    }

    [Fact]
    public void A_composition_cursor_counts_characters_as_sdl_does()
    {
        var field = Field("");
        field.Composition("\U0001F600b", 1);
        var canvas = Paint(field);
        Assert.Equal(canvas.Texts[0].X + 2 * Advance, Assert.Single(canvas.CaretRects()).Box.X, 3);
    }

    // ---------------------------------------------------------------- mouse

    [Fact]
    public void A_click_places_the_caret_at_the_nearest_place_and_takes_the_keyboard()
    {
        var field = Field("hello", focus: false);
        Paint(field);
        var textLeft = FieldBox.X + 2;
        Assert.True(field.MouseDown(textLeft + 2 * Advance + 2, FieldBox.MidY, shift: false, clicks: 1));
        Assert.True(field.Focused);
        Assert.Equal(2, field.Model.Caret);
        Assert.False(field.Model.HasSelection);

        Assert.True(field.MouseDown(textLeft + 4 * Advance - 2, FieldBox.MidY, shift: true, clicks: 1));
        Assert.Equal("ll", field.Model.SelectedText);
    }

    [Fact]
    public void A_drag_selects_and_a_double_click_takes_a_word()
    {
        var field = Field("one two");
        Paint(field);
        var textLeft = FieldBox.X + 2;
        field.MouseDown(textLeft + 1, FieldBox.MidY, false, 1);
        Assert.True(field.MouseMove(textLeft + 3 * Advance));
        Assert.Equal("one", field.Model.SelectedText);
        field.MouseUp();
        Assert.False(field.MouseMove(textLeft));

        field.MouseDown(textLeft + 5 * Advance, FieldBox.MidY, false, 2);
        Assert.Equal("two", field.Model.SelectedText);
    }

    [Fact]
    public void A_click_off_the_box_is_not_the_fields()
    {
        var field = Field("hello", focus: false);
        Paint(field);
        Assert.False(field.MouseDown(FieldBox.X - 1, FieldBox.MidY, false, 1));
        Assert.False(field.MouseDown(FieldBox.X + 10, FieldBox.Y - 5, false, 1));
        Assert.False(field.Focused);
    }

    /// <summary>Records what is drawn. Every character is <see cref="Advance"/> wide.</summary>
    private sealed class RecordingCanvas : ICanvas
    {
        public double Scale { get; set; } = 1;

        public List<object> Calls { get; } = [];

        public List<(Box Box, uint Rgb)> Rects { get; } = [];

        public List<(string Text, Face Face, float X, float Top, uint Rgb)> Texts { get; } = [];

        // The caret is the only rectangle one physical pixel wide.
        public IEnumerable<(Box Box, uint Rgb)> CaretRects() => Rects.Where(r => Math.Abs(r.Box.W * Scale - 1) < 0.001);

        public void SetOrigin(float dy) => Calls.Add(dy);

        public void FillRect(Box box, uint rgb)
        {
            Rects.Add((box, rgb));
            Calls.Add(box);
        }

        public void FillRound(Box box, float radius, uint rgb) => Calls.Add(box);

        public void Frame(Box box, float radius, uint fill, uint border) => Calls.Add(box);

        public void Ring(Box box, float radius, float thickness, uint rgb) => Calls.Add(box);

        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Calls.Add(ax);

        public void Glow(Box box, float sigma, float opacity, uint rgb) => Calls.Add(box);

        public void Text(string text, Face face, float size, float x, float top, uint rgb)
        {
            Texts.Add((text, face, x, top, rgb));
            Calls.Add(text);
        }

        public float Measure(string text, Face face, float size) => text.Length * Advance;

        public float LineHeight(Face face, float size) => size * 4 / 3;

        public void WarningIcon(float x, float y, float size, uint rgb) => Calls.Add(x);

        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => Calls.Add(content);
    }
}
