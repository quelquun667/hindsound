using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Writes down the shapes a widget draws, and what it says: enough to tell ticked from unticked, and where the focus is.</summary>
internal sealed class ShapeLog : ICanvas
{
    public List<(Box Box, uint Fill, uint Border)> Frames { get; } = [];

    public List<Box> Rings { get; } = [];

    public List<string> Texts { get; } = [];

    public int Glows { get; private set; }

    public int Dims { get; private set; }

    public int Warnings { get; private set; }

    public void SetOrigin(float dy) { }
    public void FillRect(Box box, uint rgb) { }
    public void FillRound(Box box, float radius, uint rgb) { }
    public void Frame(Box box, float radius, uint fill, uint border) => Frames.Add((box, fill, border));
    public void Ring(Box box, float radius, float thickness, uint rgb) => Rings.Add(box);
    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) { }
    public void Glow(Box box, float sigma, float opacity, uint rgb) => Glows++;
    public void Dim(float opacity) => Dims++;
    public void Text(string text, Face face, float size, float x, float top, uint rgb) => Texts.Add(text);
    public float Measure(string text, Face face, float size) => text.Length * size * 0.5f;
    public float LineHeight(Face face, float size) => size * 1.3f;
    public void WarningIcon(float x, float y, float size, uint rgb) => Warnings++;
    public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) { }
}

public sealed class CollectionChipsTests
{
    private const float Left = 200, Right = 728;
    private const uint Accent = 0xD4F53C;

    private static readonly ChipEntry[] Three = [new("c1", "pets", true), new("c2", "Sam", false), new("c3", "Alex", false)];

    private static ChipEntry[] Many(int count, string stem = "collection") =>
        [.. Enumerable.Range(1, count).Select(i => new ChipEntry("c" + i, stem + " " + i, false))];

    private static UiState Ui(Widget? focus = null, Widget? hover = null, float x = -1, float y = -1) =>
        new(Accent, focus, null, hover, x, y, 0, true, null);

    private sealed class Rig
    {
        public Rig(IReadOnlyList<ChipEntry> entries, ChipLineState? state = null)
        {
            Canvas = new FakeCanvas();
            State = state ?? new ChipLineState();
            Entries = entries;
            Line = Make();
        }

        public FakeCanvas Canvas { get; }

        public ChipLineState State { get; }

        public IReadOnlyList<ChipEntry> Entries { get; }

        public CollectionChips Line { get; private set; }

        public List<(string Id, bool On)> Set { get; } = [];

        public int Opened { get; private set; }

        public CollectionChips Make() =>
            new(Canvas, State, Entries, Left, ChipLine.ChipsLimit(Canvas, Right), (id, on) => Set.Add((id, on)), _ => Opened++);

        public void Rebuild() => Line = Make();

        public (float X, float Y) Centre(int stop)
        {
            var box = Line.Fit.BoxOf(stop);
            return (box.MidX, box.MidY);
        }

        public void Click(int stop)
        {
            var (x, y) = Centre(stop);
            Line.OnPress(x, y);
            Line.OnRelease(inside: true);
        }
    }

    // ---------------------------------------------------------------- what fits, on the real text engine

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_three_collections_of_the_picture_all_fit(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);

        var fit = ChipLine.Lay(canvas, Three, Left, ChipLine.ChipsLimit(canvas, Right));

        Assert.Equal(3, fit.Chips.Count);
        Assert.Null(fit.Plus);
        Assert.Empty(fit.Hidden);
        Assert.Equal(["pets", "Sam", "Alex"], fit.Chips.Select(chip => chip.Text));
    }

    [Theory]
    [InlineData(1.0, 8)]
    [InlineData(1.25, 8)]
    [InlineData(1.5, 8)]
    [InlineData(2.0, 8)]
    [InlineData(1.0, 120)]
    [InlineData(1.25, 120)]
    [InlineData(1.5, 120)]
    [InlineData(2.0, 120)]
    public void What_does_not_fit_goes_behind_the_plus_at_any_scale(double scale, int count)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var limit = ChipLine.ChipsLimit(canvas, Right);
        var entries = Many(count);

        var fit = ChipLine.Lay(canvas, entries, Left, limit);

        Assert.NotNull(fit.Plus);
        Assert.True(fit.Chips.Count >= 1, "not one chip fits");
        Assert.Equal(count, fit.Chips.Count + fit.Hidden.Count);
        Assert.Equal("+" + fit.Hidden.Count, fit.PlusText);
        Assert.Equal(entries.Skip(fit.Chips.Count), fit.Hidden);
        Assert.True(fit.Plus!.Value.Right <= limit, $"+N ends at {fit.Plus.Value.Right}, over {limit}");
        Assert.True(canvas.Measure(fit.PlusText, Face.Medium, 13) < fit.Plus.Value.W);
        var x = canvas.Measure(ChipLine.Label, Face.Body, 12) + Left;
        foreach (var chip in fit.Chips)
        {
            Assert.True(chip.Box.X >= x, $"{chip.Text} starts at {chip.Box.X}, over what is before it at {x}");
            Assert.True(chip.Box.Right <= limit);
            x = chip.Box.Right + ChipLine.Gap;
        }

        Assert.True(fit.Plus.Value.X >= x);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_new_collection_button_keeps_its_place_and_its_text_fits(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var clicks = 0;

        var button = ChipLine.NewButton(canvas, Right, () => clicks++);
        button.OnRelease(inside: true);

        Assert.Equal(1, clicks);
        Assert.Equal(ChipLine.NewId, button.Id);
        Assert.Equal(Right, button.Bounds.Right);
        Assert.Equal(ChipLine.Top, button.Bounds.Y);
        Assert.Equal(ChipLine.Height, button.Bounds.H);
        Assert.True(canvas.Measure(ChipLine.NewLabel, Face.Medium, 13) < button.Bounds.W);
        Assert.Equal(button.Bounds.X - ChipLine.Gap, ChipLine.ChipsLimit(canvas, Right));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void A_long_name_is_cut_at_96_pixels_and_a_short_one_is_not(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var entries = new[] { new ChipEntry("c1", "Best of the green room", true), new ChipEntry("c2", "pets", false), new ChipEntry("c3", new string('W', 40), false) };

        var fit = ChipLine.Lay(canvas, entries, Left, ChipLine.ChipsLimit(canvas, Right));

        Assert.EndsWith("...", fit.Chips[0].Text);
        Assert.Equal("pets", fit.Chips[1].Text);
        foreach (var chip in fit.Chips)
        {
            var width = canvas.Measure(chip.Text, Face.Medium, 13);
            Assert.True(width <= ChipLine.MaxName, $"{chip.Text} is {width} wide");

            // The name stays within its chip: the box to tick on its left, a margin on its right.
            Assert.True(26 + width + 8 <= chip.Box.W + 0.01f, $"{chip.Text} leaves its chip");
        }
    }

    [Fact]
    public void The_chips_are_on_the_line_of_the_picture()
    {
        var fit = ChipLine.Lay(new FakeCanvas(), Three, Left, Right);

        Assert.All(fit.Chips, chip =>
        {
            Assert.Equal(370, chip.Box.Y);
            Assert.Equal(28, chip.Box.H);
        });
    }

    [Fact]
    public void Nothing_at_all_lays_nothing()
    {
        var fit = ChipLine.Lay(new FakeCanvas(), [], Left, Right);

        Assert.Empty(fit.Chips);
        Assert.Null(fit.Plus);
        Assert.Equal(0, fit.StopCount);
    }

    [Fact]
    public void A_line_too_short_for_one_chip_holds_them_all_behind_the_plus()
    {
        var canvas = new FakeCanvas();
        var limit = Left + canvas.Measure(ChipLine.Label, Face.Body, 12) + 40;

        var fit = ChipLine.Lay(canvas, Many(3), Left, limit);

        Assert.Empty(fit.Chips);
        Assert.Equal("+3", fit.PlusText);
        Assert.Equal(3, fit.Hidden.Count);
    }

    [Fact]
    public void A_last_chip_does_not_keep_room_for_a_plus_it_does_not_need()
    {
        var canvas = new FakeCanvas();
        var entries = Many(2);
        var first = ChipLine.Lay(canvas, entries, Left, 10_000);
        var exact = first.Chips[1].Box.Right;

        var fit = ChipLine.Lay(canvas, entries, Left, exact);

        Assert.Equal(2, fit.Chips.Count);
        Assert.Null(fit.Plus);
    }

    [Fact]
    public void A_chip_stays_on_the_line_only_if_the_plus_fits_after_it()
    {
        var canvas = new FakeCanvas();
        var entries = Many(3);
        var wide = ChipLine.Lay(canvas, entries, Left, 10_000);
        var justTwo = wide.Chips[1].Box.Right;

        var fit = ChipLine.Lay(canvas, entries, Left, justTwo);

        Assert.Single(fit.Chips);
        Assert.Equal("+2", fit.PlusText);
        Assert.True(fit.Plus!.Value.Right <= justTwo);
    }

    // ---------------------------------------------------------------- the order

    [Fact]
    public void The_ticked_ones_come_first_each_group_in_its_order()
    {
        var sorted = ChipLine.TickedFirst([new("a", "a", false), new("b", "b", true), new("c", "c", false), new("d", "d", true)]);

        Assert.Equal(["b", "d", "a", "c"], sorted.Select(entry => entry.Id));
    }

    private static ChipEntry[] Ticked(params string[] on) =>
        [.. new[] { "a", "b", "c" }.Select(id => new ChipEntry(id, id, on.Contains(id)))];

    private static string[] Ids(IEnumerable<ChipEntry> entries) => [.. entries.Select(entry => entry.Id)];

    [Fact]
    public void A_clip_comes_up_with_its_collections_first()
    {
        Assert.Equal(["c", "a", "b"], Ids(new ChipLineState().Arrange("clip", Ticked("c"))));
    }

    [Fact]
    public void A_tick_moves_no_chip_under_the_mouse()
    {
        var state = new ChipLineState();
        state.Arrange("clip", Ticked());

        var after = state.Arrange("clip", Ticked("c"));

        Assert.Equal(["a", "b", "c"], Ids(after));
        Assert.True(after[2].Ticked);
    }

    [Fact]
    public void Another_clip_sorts_the_line_again_and_so_does_coming_back()
    {
        var state = new ChipLineState();
        state.Arrange("one", Ticked());
        state.Arrange("one", Ticked("c"));

        Assert.Equal(["b", "a", "c"], Ids(state.Arrange("two", Ticked("b"))));
        Assert.Equal(["c", "a", "b"], Ids(state.Arrange("one", Ticked("c"))));
    }

    [Fact]
    public void The_same_clip_spelled_otherwise_is_the_same_clip()
    {
        var state = new ChipLineState();
        state.Arrange(@"D:\Clips\A.mp3", Ticked());

        Assert.Equal(["a", "b", "c"], Ids(state.Arrange(@"d:\clips\a.mp3", Ticked("c"))));
    }

    [Fact]
    public void Closing_the_list_of_the_plus_sorts_what_was_ticked_there()
    {
        var state = new ChipLineState();
        state.Arrange("clip", Ticked());
        state.Arrange("clip", Ticked("c"));

        state.SortNext();

        Assert.Equal(["c", "a", "b"], Ids(state.Arrange("clip", Ticked("c"))));
        Assert.Equal(["c", "a", "b"], Ids(state.Arrange("clip", Ticked("a", "c"))));
    }

    [Fact]
    public void A_collection_that_comes_goes_last_and_one_that_leaves_takes_nothing_with_it()
    {
        var state = new ChipLineState();
        state.Arrange("clip", [new("b", "b", true), new("c", "c", false)]);

        Assert.Equal(["b", "c", "a"], Ids(state.Arrange("clip", [new("a", "a", false), new("b", "b", true), new("c", "c", false)])));
        Assert.Equal(["c", "a"], Ids(state.Arrange("clip", [new("a", "a", false), new("c", "c", false)])));
    }

    [Fact]
    public void A_renamed_collection_keeps_its_place_with_its_new_name()
    {
        var state = new ChipLineState();
        state.Arrange("clip", [new("a", "pets", false), new("b", "Sam", false)]);

        var after = state.Arrange("clip", [new("a", "dogs", false), new("b", "Sam", false)]);

        Assert.Equal(["dogs", "Sam"], after.Select(entry => entry.Name));
    }

    // ---------------------------------------------------------------- the keyboard

    [Theory]
    [InlineData(1, Vk.Left, 0)]
    [InlineData(1, Vk.Up, 0)]
    [InlineData(0, Vk.Left, 0)]
    [InlineData(1, Vk.Right, 2)]
    [InlineData(1, Vk.Down, 2)]
    [InlineData(3, Vk.Right, 3)]
    [InlineData(2, Vk.Home, 0)]
    [InlineData(1, Vk.End, 3)]
    [InlineData(1, Vk.Space, -1)]
    [InlineData(1, Vk.Tab, -1)]
    [InlineData(1, Vk.PageDown, -1)]
    public void The_keys_move_along_the_stops(int current, uint vk, int expected)
    {
        Assert.Equal(expected, ChipLine.Step(current, vk, 4));
    }

    [Fact]
    public void The_line_starts_on_its_first_chip_and_the_arrows_stop_at_its_ends()
    {
        var rig = new Rig(Three);

        Assert.Equal(0, rig.Line.CursorIndex);
        Assert.True(rig.Line.OnKey(Vk.Left));
        Assert.Equal(0, rig.Line.CursorIndex);
        rig.Line.OnKey(Vk.Right);
        rig.Line.OnKey(Vk.Right);
        Assert.Equal(2, rig.Line.CursorIndex);
        Assert.True(rig.Line.OnKey(Vk.Right));
        Assert.Equal(2, rig.Line.CursorIndex);
        rig.Line.OnKey(Vk.Home);
        Assert.Equal(0, rig.Line.CursorIndex);
        rig.Line.OnKey(Vk.End);
        Assert.Equal(2, rig.Line.CursorIndex);
    }

    [Fact]
    public void Space_and_Enter_tick_and_untick_the_chip_of_the_keyboard_and_it_stays_there()
    {
        var rig = new Rig(Three);

        Assert.True(rig.Line.OnKey(Vk.Space));
        rig.Line.OnKey(Vk.Right);
        Assert.True(rig.Line.OnKey(Vk.Enter));

        Assert.Equal([("c1", false), ("c2", true)], rig.Set);
        Assert.Equal(1, rig.Line.CursorIndex);
    }

    [Fact]
    public void The_plus_is_the_last_stop_and_Space_opens_it()
    {
        var rig = new Rig(Many(40));
        rig.Line.OnKey(Vk.End);

        Assert.Equal(rig.Line.Fit.Chips.Count, rig.Line.CursorIndex);
        rig.Line.OnKey(Vk.Space);

        Assert.Equal(1, rig.Opened);
        Assert.Empty(rig.Set);
    }

    [Fact]
    public void The_keyboard_keeps_its_collection_when_the_widgets_are_made_again()
    {
        var rig = new Rig(Three);
        rig.Line.OnKey(Vk.Right);

        rig.Rebuild();

        Assert.Equal(1, rig.Line.CursorIndex);
        rig.Line.OnKey(Vk.Space);
        Assert.Equal([("c2", true)], rig.Set);
    }

    [Fact]
    public void A_chip_of_the_keyboard_that_is_now_behind_the_plus_leaves_the_keyboard_on_the_plus()
    {
        var state = new ChipLineState { Cursor = new ChipStop("c40") };

        var rig = new Rig(Many(40), state);

        Assert.Equal(rig.Line.Fit.Chips.Count, rig.Line.CursorIndex);
    }

    [Fact]
    public void A_chip_of_the_keyboard_that_has_gone_leaves_the_keyboard_on_the_first_one()
    {
        var rig = new Rig(Three, new ChipLineState { Cursor = new ChipStop("gone") });

        Assert.Equal(0, rig.Line.CursorIndex);
    }

    [Fact]
    public void Other_keys_are_left_to_the_screen()
    {
        var rig = new Rig(Three);

        Assert.False(rig.Line.OnKey(Vk.Tab));
        Assert.False(rig.Line.OnKey(Vk.Escape));
        Assert.False(rig.Line.OnKey(Vk.PageDown));
        Assert.Empty(rig.Set);
    }

    [Fact]
    public void With_no_collection_the_line_takes_no_focus_and_no_key()
    {
        var rig = new Rig([]);

        Assert.False(rig.Line.CanFocus);
        Assert.False(rig.Line.OnKey(Vk.Space));
        Assert.Equal(0, rig.Line.Bounds.W);
        Assert.False(new CollectionChips(rig.Canvas, new ChipLineState(), Three, Left, Right, (_, _) => { }, _ => { }) { Enabled = false }.CanFocus);
    }

    // ---------------------------------------------------------------- the mouse

    [Fact]
    public void A_click_ticks_the_chip_under_it_and_a_second_undoes_it_at_the_same_place()
    {
        var entries = Ticked();
        var state = new ChipLineState();
        var rig = new Rig(state.Arrange("clip", entries), state);

        rig.Click(1);
        Assert.Equal([("b", true)], rig.Set);

        // The answer of the action: the same place, ticked now.
        var rig2 = new Rig(state.Arrange("clip", Ticked("b")), state);
        rig2.Click(1);

        Assert.Equal([("b", false)], rig2.Set);
    }

    [Fact]
    public void A_click_on_the_plus_opens_it()
    {
        var rig = new Rig(Many(40));

        rig.Click(rig.Line.Fit.Chips.Count);

        Assert.Equal(1, rig.Opened);
    }

    [Fact]
    public void A_click_puts_the_keyboard_where_it_was()
    {
        var rig = new Rig(Three);

        rig.Click(2);

        Assert.Equal(2, rig.Line.CursorIndex);
    }

    [Fact]
    public void A_press_in_the_gap_between_chips_does_nothing()
    {
        var rig = new Rig(Three);
        var box = rig.Line.Fit.BoxOf(0);

        rig.Line.OnPress(box.Right + 1, box.MidY);
        rig.Line.OnRelease(inside: true);

        Assert.Empty(rig.Set);
    }

    [Fact]
    public void A_press_dragged_to_another_chip_does_nothing()
    {
        var rig = new Rig(Three);
        var (x, y) = rig.Centre(0);
        var (toX, toY) = rig.Centre(1);

        rig.Line.OnPress(x, y);
        rig.Line.OnDrag(toX, toY);
        rig.Line.OnRelease(inside: true);

        Assert.Empty(rig.Set);
    }

    [Fact]
    public void A_press_dragged_away_and_back_still_counts()
    {
        var rig = new Rig(Three);
        var (x, y) = rig.Centre(0);
        var (toX, toY) = rig.Centre(1);

        rig.Line.OnPress(x, y);
        rig.Line.OnDrag(toX, toY);
        rig.Line.OnDrag(x, y);
        rig.Line.OnRelease(inside: true);

        Assert.Equal([("c1", false)], rig.Set);
    }

    [Fact]
    public void A_release_outside_the_line_does_nothing()
    {
        var rig = new Rig(Three);
        var (x, y) = rig.Centre(0);

        rig.Line.OnPress(x, y);
        rig.Line.OnRelease(inside: false);

        Assert.Empty(rig.Set);
    }

    [Fact]
    public void A_press_survives_the_widgets_being_made_again_before_the_release()
    {
        var rig = new Rig(Three);
        var (x, y) = rig.Centre(1);

        rig.Line.OnPress(x, y);
        rig.Rebuild();
        rig.Line.OnRelease(inside: true);

        Assert.Equal([("c2", true)], rig.Set);
    }

    [Fact]
    public void A_release_with_no_press_does_nothing()
    {
        var rig = new Rig(Three);

        rig.Line.OnRelease(inside: true);

        Assert.Empty(rig.Set);
        Assert.Equal(0, rig.Opened);
    }

    [Fact]
    public void A_stale_press_does_not_act_on_a_chip_that_has_gone()
    {
        var state = new ChipLineState { Pressed = new ChipStop("gone"), PressStaysOver = true };
        var rig = new Rig(Three, state);

        rig.Line.OnRelease(inside: true);

        Assert.Empty(rig.Set);
    }

    [Fact]
    public void The_bounds_hold_the_stops_and_not_the_label()
    {
        var rig = new Rig(Many(40));
        var fit = rig.Line.Fit;

        Assert.Equal(fit.Chips[0].Box.X, rig.Line.Bounds.X);
        Assert.Equal(fit.Plus!.Value.Right, rig.Line.Bounds.Right);
        Assert.Equal(ChipLine.Top, rig.Line.Bounds.Y);
        Assert.Equal(ChipLine.ChipsId, rig.Line.Id);
    }

    [Fact]
    public void A_move_is_worth_a_repaint_only_over_another_stop()
    {
        var rig = new Rig(Three);
        var (x, y) = rig.Centre(0);

        Assert.True(rig.Line.HoverMoved(x, y));
        Assert.False(rig.Line.HoverMoved(x + 1, y));
        var (otherX, otherY) = rig.Centre(1);
        Assert.True(rig.Line.HoverMoved(otherX, otherY));
        Assert.True(rig.Line.HoverMoved(rig.Line.Fit.BoxOf(0).Right + 1, y));
    }

    // ---------------------------------------------------------------- what is painted

    [Fact]
    public void A_ticked_chip_has_the_accent_on_its_edge_and_an_unticked_one_does_not()
    {
        var rig = new Rig(Three);
        var log = new ShapeLog();

        rig.Line.Paint(log, Ui());

        var chip = rig.Line.Fit.Chips[0].Box;
        var other = rig.Line.Fit.Chips[1].Box;
        Assert.Equal(Accent, log.Frames.Single(frame => frame.Box == chip).Border);
        Assert.Equal(Theme.Control, log.Frames.Single(frame => frame.Box == other).Border);
    }

    [Fact]
    public void The_line_says_its_label_its_names_and_its_plus()
    {
        var rig = new Rig(Many(40));
        var log = new ShapeLog();

        rig.Line.Paint(log, Ui());

        Assert.Contains(ChipLine.Label, log.Texts);
        Assert.Contains("collection 1", log.Texts);
        Assert.Contains(rig.Line.Fit.PlusText, log.Texts);
        Assert.DoesNotContain("collection 40", log.Texts);
    }

    [Fact]
    public void The_focus_outline_is_on_the_chip_of_the_keyboard_only_with_the_focus_here()
    {
        var rig = new Rig(Three);
        rig.Line.OnKey(Vk.Right);

        var without = new ShapeLog();
        rig.Line.Paint(without, Ui());
        Assert.Empty(without.Rings);

        var with = new ShapeLog();
        rig.Line.Paint(with, Ui(focus: rig.Line));

        Assert.Equal(rig.Line.Fit.Chips[1].Box.Inflate(3), Assert.Single(with.Rings));
    }

    [Fact]
    public void The_chip_under_the_mouse_lights_up_and_only_that_one()
    {
        var rig = new Rig(Three);
        var (x, y) = rig.Centre(1);
        var log = new ShapeLog();

        rig.Line.Paint(log, Ui(hover: rig.Line, x: x, y: y));

        Assert.Equal(Theme.Hover, log.Frames.Single(frame => frame.Box == rig.Line.Fit.Chips[1].Box).Fill);
        Assert.Equal(Theme.Raised, log.Frames.Single(frame => frame.Box == rig.Line.Fit.Chips[2].Box).Fill);
    }

    [Theory]
    [InlineData(LibraryStatus.Corrupt, "Your collections file has an error. A copy is kept before anything changes.")]
    [InlineData(LibraryStatus.Unreadable, "Collections can't be read right now. Your clips are safe.")]
    [InlineData(LibraryStatus.TooNew, "A newer version wrote your collections. Update to change them.")]
    public void A_file_in_trouble_says_so_in_the_place_of_the_line(LibraryStatus status, string text)
    {
        Assert.Equal(text, ChipLine.ReadOnlyMessage(status));
        var log = new ShapeLog();

        ChipLine.Notice(text, Left, Right - Left).Paint(log, Ui());

        Assert.Equal(1, log.Warnings);
        Assert.Contains(text, log.Texts);
    }

    [Theory]
    [InlineData(LibraryStatus.Loaded)]
    [InlineData(LibraryStatus.Missing)]
    public void A_sound_file_has_nothing_to_say(LibraryStatus status)
    {
        Assert.Null(ChipLine.ReadOnlyMessage(status));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_sentences_of_a_file_in_trouble_fit_the_width_of_the_line(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);

        foreach (var status in new[] { LibraryStatus.Corrupt, LibraryStatus.Unreadable })
        {
            var text = ChipLine.ReadOnlyMessage(status)!;
            Assert.Equal(text, canvas.Fit(text, Face.Body, 13, Right - Left - 18));
        }
    }
}
