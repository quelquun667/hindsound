using System.Reflection;
using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class ListWidgetsTests
{
    private const uint Accent = 0xCCFF33;
    private static readonly Box ListBox = new(200, 118, 560, 240);

    private sealed class Rig
    {
        public Rig(int count, Func<int, ListRow>? rowAt = null, bool endRow = false, string? endNote = null)
        {
            Paths = Enumerable.Range(0, count).Select(i => $@"C:\Clips\clip{i:0000}.mp3").ToArray();
            State.SetRows(count, i => Paths[i], endRow);
            RowAt = rowAt ?? (i => new ListRow($"Hindsound_Demo_{i:0000}.mp3", "0:30"));
            List = new RowList(State, ListBox, i =>
            {
                Asked++;
                return RowAt(i);
            }, () => SelectionChanges++, () => Repaints++)
            {
                Id = "lib.list",
                EndNote = endNote,
                EmptyTitle = "No clips yet",
                EmptyBody = "Save a clip with your shortcut, and it shows up here.",
            };
        }

        public string[] Paths { get; }

        public RowListState State { get; } = new();

        public Func<int, ListRow> RowAt { get; }

        public RowList List { get; }

        public int Asked { get; set; }

        public int SelectionChanges { get; set; }

        public int Repaints { get; set; }

        public UiState Ui(bool focused = false, float mouseX = -1, float mouseY = -1, bool pressed = false, bool hovered = true) =>
            new(Accent, focused ? List : null, pressed ? List : null, mouseX >= 0 && hovered ? List : null, mouseX, mouseY, 0, focused, null);
    }

    private static float RowY(int slot) => ListBox.Y + slot * RowListState.RowHeight + 20;

    // ---------------------------------------------------------------- only what shows

    [Fact]
    public void Of_2000_rows_a_paint_asks_about_the_6_on_screen()
    {
        var rig = new Rig(2000);
        rig.State.ScrollTo(1000);
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        Assert.Equal(6, rig.Asked);
        Assert.Contains("Demo_1000", canvas.Drawn);
        Assert.Contains("Demo_1005", canvas.Drawn);
        Assert.DoesNotContain("Demo_0999", canvas.Drawn);
        Assert.DoesNotContain("Demo_1006", canvas.Drawn);
    }

    [Fact]
    public void The_box_says_how_many_rows_fit()
    {
        var state = new RowListState();

        _ = new RowList(state, new Box(200, 118, 560, 280), _ => default, () => { }, () => { });

        Assert.Equal(7, state.ViewRows);
    }

    // ---------------------------------------------------------------- a row

    [Fact]
    public void A_name_is_drawn_in_three_parts_end_to_end()
    {
        var rig = new Rig(1);
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        var parts = canvas.DrawnAt.Take(3).ToArray();
        Assert.Equal(["Hindsound_", "Demo_0000", ".mp3"], parts.Select(part => part.Text));
        Assert.Equal(210, parts[0].X);
        Assert.Equal(parts[0].X + canvas.Measure("Hindsound_", Face.Body, 14), parts[1].X);
        Assert.Equal(parts[1].X + canvas.Measure("Demo_0000", Face.Body, 14), parts[2].X);
    }

    [Theory]
    [InlineData("clip.wav", "clip", ".wav")]
    [InlineData("no extension", "no extension", null)]
    [InlineData("Hindsound_", null, null)]
    [InlineData(".mp3", null, ".mp3")]
    public void A_name_of_any_shape_is_drawn_whole(string name, string? middle, string? extension)
    {
        var rig = new Rig(1, _ => new ListRow(name, "0:30"));
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        Assert.Equal(name, string.Concat(canvas.Drawn.TakeWhile(text => text != "0:30")));
        if (middle is not null)
        {
            Assert.Contains(middle, canvas.Drawn);
        }

        if (extension is not null)
        {
            Assert.Contains(extension, canvas.Drawn);
        }
    }

    [Fact]
    public void A_row_without_a_duration_draws_none_and_nothing_moves_when_it_arrives()
    {
        var read = false;
        var rig = new Rig(1, _ => new ListRow(new string('x', 200) + ".mp3", read ? "12:34" : null));
        var waiting = new FakeCanvas();
        rig.List.Paint(waiting, rig.Ui());

        read = true;
        var done = new FakeCanvas();
        rig.List.Paint(done, rig.Ui());

        Assert.DoesNotContain("12:34", waiting.Drawn);
        Assert.Contains("12:34", done.Drawn);
        Assert.Equal(waiting.DrawnAt, done.DrawnAt.Where(text => text.Text != "12:34"));
    }

    [Fact]
    public void A_long_name_is_cut_before_the_duration()
    {
        var rig = new Rig(1, _ => new ListRow(new string('x', 200) + ".mp3", "0:30"));
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        var name = canvas.Drawn[0];
        Assert.EndsWith("...", name);
        Assert.DoesNotContain(".mp3", canvas.Drawn);
        Assert.True(210 + canvas.Measure(name, Face.Body, 14) <= 628, name);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_long_name_also_stops_before_the_notes(bool rescued, bool twoFiles)
    {
        var rig = new Rig(1, _ => new ListRow(new string('x', 200) + ".mp3", "0:30", rescued, twoFiles));
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        var name = canvas.DrawnAt.First(text => text.Text.StartsWith('x'));
        var notes = canvas.DrawnAt.Where(text => text.Text is "Rescued" or "2 files").ToArray();
        Assert.Equal((rescued ? 1 : 0) + (twoFiles ? 1 : 0), notes.Length);
        Assert.True(name.X + canvas.Measure(name.Text, Face.Body, 14) <= notes.Min(note => note.X), name.Text);
    }

    [Fact]
    public void Both_notes_lie_side_by_side()
    {
        var rig = new Rig(1, _ => new ListRow("clip.mp3", "0:30", Rescued: true, TwoFiles: true));
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        var rescued = canvas.DrawnAt.Single(text => text.Text == "Rescued");
        var twoFiles = canvas.DrawnAt.Single(text => text.Text == "2 files");
        Assert.True(rescued.X + canvas.Measure("Rescued", Face.Body, 12) < twoFiles.X);
        Assert.Equal(628, twoFiles.X + canvas.Measure("2 files", Face.Body, 12), 3);
    }

    [Fact]
    public void A_plain_row_has_no_note()
    {
        var rig = new Rig(1);
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui());

        Assert.DoesNotContain("Rescued", canvas.Drawn);
        Assert.DoesNotContain("2 files", canvas.Drawn);
    }

    [Fact]
    public void A_missing_clip_is_faded_and_the_chosen_one_is_lit()
    {
        var missing = false;
        var rig = new Rig(1, _ => new ListRow("Hindsound_Demo.mp3", "0:30", Missing: missing));
        rig.State.Select(0);

        var lit = new FakeCanvas();
        rig.List.Paint(lit, rig.Ui());
        Assert.Equal(Theme.Muted, lit.TextColors["Hindsound_"]);
        Assert.Equal(Theme.Text, lit.TextColors["Demo"]);
        Assert.Equal(Theme.Text, lit.TextColors["0:30"]);

        missing = true;
        var faded = new FakeCanvas();
        rig.List.Paint(faded, rig.Ui());
        Assert.Equal(Theme.Faded, faded.TextColors["Hindsound_"]);
        Assert.Equal(Theme.Faded, faded.TextColors["Demo"]);
        Assert.Equal(Theme.Faded, faded.TextColors["0:30"]);
    }

    // ---------------------------------------------------------------- from a line of the scan

    private static ClipGroup GroupOf(params string[] paths) =>
        new(paths.Select(path => new ClipFile(path, 10, DateTime.UnixEpoch)).ToArray());

    [Fact]
    public void A_line_of_the_scan_gives_its_name_and_its_duration()
    {
        var row = ListRow.Of(GroupOf(@"C:\Clips\demo.mp3"), 300, ClipFolders.Key(@"C:\Rescue"));

        Assert.Equal(new ListRow("demo.mp3", "5:00"), row);
    }

    [Fact]
    public void A_line_not_read_yet_has_no_duration()
    {
        Assert.Null(ListRow.Of(GroupOf(@"C:\Clips\demo.mp3"), null, ClipFolders.Key(@"C:\Rescue")).Duration);
    }

    [Fact]
    public void Two_tracks_say_so()
    {
        Assert.True(ListRow.Of(GroupOf(@"C:\Clips\demo_system.mp3", @"C:\Clips\demo_mic.mp3"), 30, ClipFolders.Key(@"C:\Rescue")).TwoFiles);
    }

    [Theory]
    [InlineData(@"C:\Clips\demo_system.mp3", @"C:\Clips\demo_mic.mp3", "demo.mp3")]
    [InlineData(@"C:\Clips\demo (2)_system.mp3", @"C:\Clips\demo (2)_mic.mp3", "demo (2).mp3")]
    [InlineData(@"C:\Clips\DEMO_SYSTEM.WAV", @"C:\Clips\DEMO_MIC.WAV", "DEMO.WAV")]
    public void Two_tracks_are_named_as_the_clip_not_as_the_system_track(string system, string mic, string expected)
    {
        Assert.Equal(expected, ListRow.Of(GroupOf(system, mic), 30, ClipFolders.Key(@"C:\Rescue")).Name);
    }

    [Theory]
    [InlineData(@"C:\Clips\demo_mic.mp3")]
    [InlineData(@"C:\Clips\demo_system.mp3")]
    [InlineData(@"C:\Clips\demo.mp3")]
    public void A_single_file_keeps_its_name(string path)
    {
        Assert.Equal(Path.GetFileName(path), ListRow.Of(GroupOf(path), 30, ClipFolders.Key(@"C:\Rescue")).Name);
    }

    [Theory]
    [InlineData(@"C:\Rescue\demo.mp3", true)]
    [InlineData(@"c:\rescue\demo.mp3", true)]
    [InlineData(@"\\?\C:\Rescue\demo.mp3", true)]
    [InlineData(@"C:\Rescue\Inner\demo.mp3", false)]
    [InlineData(@"C:\Rescued\demo.mp3", false)]
    [InlineData(@"demo.mp3", false)]
    public void Only_the_rescue_folder_itself_gets_the_note(string path, bool expected)
    {
        Assert.Equal(expected, ListRow.Of(GroupOf(path), 30, ClipFolders.Key(@"C:\Rescue\")).Rescued);
    }

    [Fact]
    public void A_line_found_gone_says_so()
    {
        Assert.True(ListRow.Of(GroupOf(@"C:\Clips\demo.mp3"), 30, ClipFolders.Key(@"C:\Rescue"), missing: true).Missing);
    }

    // ---------------------------------------------------------------- the keyboard

    [Fact]
    public void A_key_chooses_and_says_so_once()
    {
        var rig = new Rig(20);
        rig.State.Select(3);

        Assert.True(rig.List.OnKey(Vk.Down));

        Assert.Equal(4, rig.State.Selected);
        Assert.Equal(1, rig.SelectionChanges);
    }

    [Fact]
    public void A_key_at_the_end_is_taken_and_tells_nothing()
    {
        var rig = new Rig(20);
        rig.State.Select(0);

        Assert.True(rig.List.OnKey(Vk.Up));

        Assert.Equal(0, rig.SelectionChanges);
        Assert.Equal(0, rig.Repaints);
    }

    [Fact]
    public void A_key_that_only_brings_the_row_back_repaints()
    {
        var rig = new Rig(20);
        rig.State.Select(0);
        rig.State.ScrollTo(10);

        Assert.True(rig.List.OnKey(Vk.Up));

        Assert.Equal(0, rig.State.Top);
        Assert.Equal(0, rig.SelectionChanges);
        Assert.Equal(1, rig.Repaints);
    }

    [Fact]
    public void A_key_the_list_does_not_use_goes_on()
    {
        var rig = new Rig(20);

        Assert.False(rig.List.OnKey(Vk.Space));
        Assert.Equal(0, rig.SelectionChanges);
    }

    // ---------------------------------------------------------------- the mouse

    [Fact]
    public void A_click_chooses_the_row_under_it()
    {
        var rig = new Rig(2000);
        rig.State.ScrollTo(100);

        rig.List.OnPress(300, RowY(2));
        rig.List.OnRelease(true);

        Assert.Equal(102, rig.State.Selected);
        Assert.Equal(1, rig.SelectionChanges);
        Assert.Equal(@"C:\Clips\clip0102.mp3", rig.State.SelectedPath);
    }

    [Fact]
    public void A_click_on_the_chosen_row_changes_nothing()
    {
        var rig = new Rig(20);
        rig.State.Select(2);

        rig.List.OnPress(300, RowY(2));

        Assert.Equal(2, rig.State.Selected);
        Assert.Equal(0, rig.SelectionChanges);
    }

    [Fact]
    public void A_click_below_the_rows_chooses_nothing()
    {
        var rig = new Rig(2);
        rig.State.Select(1);

        rig.List.OnPress(300, RowY(4));

        Assert.Equal(1, rig.State.Selected);
        Assert.Equal(0, rig.SelectionChanges);
    }

    [Fact]
    public void A_click_on_the_sentence_chooses_nothing()
    {
        var rig = new Rig(8, endRow: true, endNote: "Showing the 2,000 most recent clips.");
        rig.State.ScrollTo(rig.State.MaxTop);

        rig.List.OnPress(300, RowY(5));

        Assert.Equal(RowListState.None, rig.State.Selected);
    }

    [Fact]
    public void Dragging_the_thumb_scrolls_within_bounds_and_chooses_nothing()
    {
        var rig = new Rig(2000);

        rig.List.OnPress(750, ListBox.Y + 4 + 16);
        Assert.Equal(0, rig.State.Top);
        Assert.True(rig.State.ThumbDragging);

        rig.List.OnDrag(750, ListBox.Bottom + 500);
        Assert.Equal(1994, rig.State.Top);
        Assert.True(rig.Repaints >= 1);

        rig.List.OnDrag(750, ListBox.Y - 500);
        Assert.Equal(0, rig.State.Top);

        rig.List.OnRelease(false);
        Assert.False(rig.State.ThumbDragging);
        Assert.Equal(RowListState.None, rig.State.Selected);
        Assert.Equal(0, rig.SelectionChanges);
    }

    [Fact]
    public void Dragging_from_a_row_does_not_scroll()
    {
        var rig = new Rig(2000);

        rig.List.OnPress(300, RowY(0));
        rig.List.OnDrag(300, ListBox.Bottom + 500);

        Assert.Equal(0, rig.State.Top);
    }

    [Fact]
    public void A_press_in_the_gutter_beside_the_thumb_does_nothing()
    {
        var rig = new Rig(2000);

        rig.List.OnPress(735, RowY(3));

        Assert.False(rig.State.ThumbDragging);
        Assert.Equal(RowListState.None, rig.State.Selected);
        Assert.Equal(0, rig.State.Top);
    }

    [Fact]
    public void A_list_that_fits_has_no_thumb_to_press()
    {
        var rig = new Rig(3);

        rig.List.OnPress(750, RowY(1));

        Assert.False(rig.State.ThumbDragging);
        Assert.Equal(0, rig.Repaints);
    }

    [Fact]
    public void The_wheel_scrolls_and_repaints_only_if_it_moved()
    {
        var rig = new Rig(20);

        rig.List.OnWheel(1);
        Assert.Equal(0, rig.Repaints);

        rig.List.OnWheel(-1);
        Assert.Equal(1, rig.Repaints);
        Assert.Equal(2, rig.State.Top);
    }

    [Fact]
    public void Moving_inside_a_row_is_not_worth_a_repaint()
    {
        var rig = new Rig(20);

        Assert.True(rig.List.HoverMoved(300, RowY(1)));
        Assert.False(rig.List.HoverMoved(400, RowY(1) + 5));
        Assert.True(rig.List.HoverMoved(300, RowY(2)));
        Assert.True(rig.List.HoverMoved(750, RowY(2)));
    }

    [Fact]
    public void The_next_move_is_compared_with_the_row_that_was_lit_not_the_last_one_seen()
    {
        var rig = new Rig(20);
        Assert.True(rig.List.HoverMoved(300, RowY(5)));

        // The wheel turns under a mouse that stays put: the paint lights row 7 there.
        rig.List.OnWheel(-1);
        using (Painted(rig, rig.Ui(mouseX: 300, mouseY: RowY(5))))
        {
            Assert.Equal(2, rig.State.Top);
        }

        Assert.False(rig.List.HoverMoved(300, RowY(5)));

        // Two rows up is row 5, which the earlier move had seen: the lit row 7 still has to give way.
        Assert.True(rig.List.HoverMoved(300, RowY(3)));
    }

    // ---------------------------------------------------------------- a new reading and the focus

    [Fact]
    public void A_new_reading_never_asks_for_the_keyboard()
    {
        using var screen = new ScreenRig();
        screen.Screen.ShowSection(Section.Export);
        screen.FocusOn("export.template");
        var requests = screen.Host.FocusRequests;
        var focus = screen.Screen.FocusId;

        string[] paths = [@"C:\Clips\a.mp3", @"C:\Clips\b.mp3"];
        var state = new RowListState();
        state.SetRows(2, i => paths[i]);
        state.Select(1);
        var list = new RowList(state, ListBox, i => new ListRow(paths[i]), screen.Host.Invalidate, screen.Host.Invalidate) { Id = "lib.list" };

        paths = [@"C:\Clips\new.mp3", @"C:\Clips\a.mp3", @"C:\Clips\b.mp3"];
        var fresh = paths;
        state.SetRows(3, i => fresh[i]);
        screen.Host.Invalidate();
        list.Paint(new FakeCanvas(), new UiState(Accent, null, null, null, -1, -1, 0, false, null));

        Assert.Equal(requests, screen.Host.FocusRequests);
        Assert.Equal(focus, screen.Screen.FocusId);
        Assert.Equal(2, state.Selected);
    }

    [Theory]
    [InlineData(typeof(RowList))]
    [InlineData(typeof(RowListState))]
    public void The_list_cannot_reach_the_window(Type type)
    {
        var held = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.DoesNotContain(held, field => typeof(IWindowHost).IsAssignableFrom(field.FieldType) || field.FieldType == typeof(SettingsScreen));
    }

    [Fact]
    public void A_row_gone_from_a_new_reading_is_no_longer_lit()
    {
        var rig = new Rig(20);
        rig.State.Select(2);

        var fell = rig.State.SetRows(19, i => $@"C:\Clips\other{i}.mp3");
        var canvas = new FakeCanvas();
        rig.List.Paint(canvas, rig.Ui());

        Assert.True(fell);
        Assert.Null(rig.State.SelectedPath);
        Assert.Equal(Theme.Muted, canvas.TextColors["0:30"]);
    }

    // ---------------------------------------------------------------- empty

    [Fact]
    public void An_empty_list_shows_its_message_and_takes_no_key()
    {
        var rig = new Rig(0);
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, rig.Ui(focused: true));

        Assert.Equal(["No clips yet", "Save a clip with your shortcut, and it shows up here."], canvas.Drawn);
        Assert.Equal(0, rig.Asked);
        Assert.False(rig.List.CanFocus);
        Assert.False(rig.List.OnKey(Vk.Down));

        rig.List.OnPress(300, RowY(0));
        Assert.Equal(0, rig.SelectionChanges);
    }

    [Fact]
    public void An_empty_filter_can_have_a_title_alone()
    {
        var state = new RowListState();
        var list = new RowList(state, ListBox, _ => default, () => { }, () => { }) { EmptyTitle = "No clips in this collection" };
        var canvas = new FakeCanvas();

        list.Paint(canvas, new UiState(Accent, null, null, null, -1, -1, 0, false, null));

        Assert.Equal(["No clips in this collection"], canvas.Drawn);
    }

    [Fact]
    public void A_list_with_rows_can_take_the_focus_unless_it_is_disabled()
    {
        Assert.True(new Rig(1).List.CanFocus);
        Assert.False(new RowList(new RowListState(), ListBox, _ => default, () => { }, () => { }) { Enabled = false }.CanFocus);
    }

    // ---------------------------------------------------------------- on the real canvas

    private static unsafe uint Pixel(GdiCanvas canvas, int x, int y) => canvas.Bits[y * canvas.Width + x] & 0xFFFFFF;

    private static GdiCanvas Painted(Rig rig, UiState ui)
    {
        var canvas = new GdiCanvas(760, 488, 1.0);
        canvas.FillRect(new Box(0, 0, 760, 488), Theme.Base);
        rig.List.Paint(canvas, ui);
        return canvas;
    }

    [Fact]
    public void On_the_real_canvas_nothing_is_drawn_outside_the_list()
    {
        var rig = new Rig(2000, i => new ListRow(new string('M', 100) + i + ".mp3", "30:00", Rescued: true, TwoFiles: true));
        rig.State.Select(1003);
        rig.State.ScrollTo(1000);
        using var canvas = Painted(rig, rig.Ui(focused: true));

        var painted = 0;
        for (var y = 0; y < 488; y++)
        {
            for (var x = 0; x < 760; x++)
            {
                // The outline of the focus goes 3 pixels around the row, and its anti-aliasing one more.
                var inside = x >= ListBox.X - 4 && x < ListBox.Right && y >= ListBox.Y - 4 && y < ListBox.Bottom + 4;
                if (Pixel(canvas, x, y) != Theme.Base)
                {
                    Assert.True(inside, $"painted at {x},{y}");
                    painted++;
                }
            }
        }

        Assert.True(painted > 1000, $"{painted}");
    }

    [Fact]
    public void On_the_real_canvas_the_chosen_row_is_raised_with_its_mark()
    {
        var rig = new Rig(20);
        rig.State.Select(1);
        using var canvas = Painted(rig, rig.Ui());

        var mid = (int)RowY(1);
        Assert.Equal(Accent, Pixel(canvas, 201, mid));
        Assert.Equal(Theme.Raised, Pixel(canvas, 630, mid));
        Assert.Equal(Theme.Base, Pixel(canvas, 630, (int)RowY(0)));
    }

    [Fact]
    public void On_the_real_canvas_the_hovered_row_is_lighter_than_the_rest_and_darker_than_the_chosen()
    {
        var rig = new Rig(20);
        rig.State.Select(1);
        using var canvas = Painted(rig, rig.Ui(mouseX: 300, mouseY: RowY(3)));

        Assert.Equal(Theme.Panel, Pixel(canvas, 630, (int)RowY(3)));
        Assert.Equal(Theme.Raised, Pixel(canvas, 630, (int)RowY(1)));
        Assert.Equal(Theme.Base, Pixel(canvas, 630, (int)RowY(4)));
    }

    [Fact]
    public void On_the_real_canvas_a_row_under_the_mouse_is_not_lit_when_the_screen_says_the_mouse_is_elsewhere()
    {
        // An open menu above the list takes the hover, and the mouse may still be over a row.
        var rig = new Rig(20);
        using var canvas = Painted(rig, rig.Ui(mouseX: 300, mouseY: RowY(3), hovered: false));

        Assert.Equal(Theme.Base, Pixel(canvas, 630, (int)RowY(3)));
    }

    [Fact]
    public void On_the_real_canvas_the_thumb_is_not_lit_when_the_screen_says_the_button_is_not_held_on_it()
    {
        // The window lost the mouse capture in the middle of the drag: the state still says dragging, the screen no longer does.
        var rig = new Rig(2000);
        rig.State.GrabThumb(10, 232);
        Assert.True(rig.State.ThumbDragging);
        using var canvas = Painted(rig, rig.Ui());

        Assert.Equal(Theme.Control, Pixel(canvas, 751, (int)ListBox.Y + 20));
    }

    [Fact]
    public void On_the_real_canvas_the_mouse_on_the_chosen_row_changes_nothing()
    {
        var rig = new Rig(20);
        rig.State.Select(1);
        using var canvas = Painted(rig, rig.Ui(mouseX: 300, mouseY: RowY(1)));

        Assert.Equal(Theme.Raised, Pixel(canvas, 630, (int)RowY(1)));
    }

    [Fact]
    public void On_the_real_canvas_the_place_of_the_star_stays_free()
    {
        var rig = new Rig(20, _ => new ListRow(new string('M', 200) + ".mp3", "30:00", Rescued: true, TwoFiles: true));
        using var canvas = Painted(rig, rig.Ui());

        for (var x = 728 - 43; x < 728; x++)
        {
            Assert.Equal(Theme.Base, Pixel(canvas, x, (int)RowY(2)));
        }
    }

    [Fact]
    public void On_the_real_canvas_a_long_name_stops_before_the_duration()
    {
        var rig = new Rig(1, _ => new ListRow(new string('M', 200) + ".mp3", "30:00"));
        using var canvas = Painted(rig, rig.Ui());

        // From the end of the name's room to where the longest duration starts.
        var mid = (int)RowY(0);
        for (var y = mid - 10; y < mid + 10; y++)
        {
            for (var x = 629; x < 640; x++)
            {
                Assert.Equal(Theme.Base, Pixel(canvas, x, y));
            }
        }

        Assert.NotEqual(Theme.Base, InkOnRow(canvas, mid));
    }

    [Fact]
    public void On_the_real_canvas_a_rule_ends_each_row_but_the_chosen_one_the_one_above_it_and_the_last()
    {
        var rig = new Rig(6);
        rig.State.Select(2);
        using var canvas = Painted(rig, rig.Ui());

        uint Rule(int slot) => Pixel(canvas, 630, (int)(ListBox.Y + (slot + 1) * RowListState.RowHeight) - 1);

        Assert.Equal(Theme.Raised, Rule(0));
        Assert.Equal(Theme.Base, Rule(1));
        Assert.Equal(Theme.Raised, Rule(3));
        Assert.Equal(Theme.Base, Rule(5));
    }

    [Fact]
    public void On_the_real_canvas_the_last_row_keeps_its_rule_above_the_sentence()
    {
        var rig = new Rig(5, endRow: true, endNote: "Showing the 2,000 most recent clips.");
        using var canvas = Painted(rig, rig.Ui());

        Assert.Equal(Theme.Raised, Pixel(canvas, 630, (int)(ListBox.Y + 5 * RowListState.RowHeight) - 1));
    }

    private static uint InkOnRow(GdiCanvas canvas, int y)
    {
        for (var x = 210; x < 628; x++)
        {
            if (Pixel(canvas, x, y) != Theme.Base)
            {
                return Pixel(canvas, x, y);
            }
        }

        return Theme.Base;
    }

    [Fact]
    public void On_the_real_canvas_the_thumb_follows_the_scroll_and_lights_while_dragged()
    {
        var rig = new Rig(2000);
        using (var atTop = Painted(rig, rig.Ui()))
        {
            Assert.Equal(Theme.Control, Pixel(atTop, 751, (int)ListBox.Y + 20));
            Assert.Equal(Theme.Base, Pixel(atTop, 751, (int)ListBox.Bottom - 20));
        }

        rig.State.ScrollTo(rig.State.MaxTop);
        using (var atEnd = Painted(rig, rig.Ui()))
        {
            Assert.Equal(Theme.Base, Pixel(atEnd, 751, (int)ListBox.Y + 20));
            Assert.Equal(Theme.Control, Pixel(atEnd, 751, (int)ListBox.Bottom - 20));
        }

        rig.List.OnPress(750, ListBox.Bottom - 20);
        using var dragged = Painted(rig, rig.Ui(pressed: true));
        Assert.Equal(Theme.Muted, Pixel(dragged, 751, (int)ListBox.Bottom - 20));
    }

    [Fact]
    public void On_the_real_canvas_a_list_that_fits_has_no_thumb()
    {
        var rig = new Rig(6);
        using var canvas = Painted(rig, rig.Ui());

        for (var y = (int)ListBox.Y; y < ListBox.Bottom; y++)
        {
            Assert.Equal(Theme.Base, Pixel(canvas, 751, y));
        }
    }

    [Fact]
    public void On_the_real_canvas_the_sentence_lies_in_the_slot_after_the_last_row()
    {
        var rig = new Rig(8, endRow: true, endNote: "Showing the 2,000 most recent clips.");
        rig.State.ScrollTo(rig.State.MaxTop);
        using var canvas = Painted(rig, rig.Ui());

        var mid = (int)RowY(5);
        var inked = false;
        for (var y = mid - 8; y < mid + 8 && !inked; y++)
        {
            inked = InkOnRow(canvas, y) != Theme.Base;
        }

        Assert.True(inked);
        Assert.Equal(RowListState.None, rig.State.RowAt(mid - ListBox.Y));
    }

    [Fact]
    public void On_the_real_canvas_the_focus_outline_follows_the_chosen_row()
    {
        var rig = new Rig(20);
        rig.State.Select(2);
        using (var onRow = Painted(rig, rig.Ui(focused: true)))
        {
            Assert.Equal(Accent, Pixel(onRow, 400, (int)ListBox.Y + 80 - 3));
        }

        rig.State.ScrollTo(10);
        using var away = Painted(rig, rig.Ui(focused: true));
        Assert.Equal(Accent, Pixel(away, 400, (int)ListBox.Y - 3));
        Assert.NotEqual(Accent, Pixel(away, 400, (int)ListBox.Y + 80 - 3));
    }
}
