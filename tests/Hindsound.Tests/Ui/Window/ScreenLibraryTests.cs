using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A list of clips with no disk behind it, which says what it was asked.</summary>
internal sealed class FakeLibrary : ILibraryPanel
{
    public const string Folder = @"C:\Clips\";

    public ClipLines? Lines { get; set; }

    public Dictionary<string, TimeSpan> Lengths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Rescued { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int LengthsAsked { get; private set; }

    public int Reads { get; private set; }

    public List<ClipGroup> Copied { get; } = [];

    public List<ClipGroup> Shown { get; } = [];

    public static ClipGroup Clip(string name, long size = 720_000) => new([new ClipFile(Folder + name, size, default)]);

    public static string PathOf(int row) => $"{Folder}clip{row:0000}.mp3";

    /// <summary>A reading of this many clips, each one 30 seconds long.</summary>
    public FakeLibrary With(int count, int leftOut = 0)
    {
        var rows = Enumerable.Range(0, count).Select(i => Clip($"clip{i:0000}.mp3")).ToList();
        foreach (var row in rows)
        {
            Lengths[row.Main.Path] = TimeSpan.FromSeconds(30);
        }

        Lines = new ClipLines(rows, leftOut);
        return this;
    }

    public TimeSpan? LengthOf(ClipGroup line)
    {
        LengthsAsked++;
        return Lengths.TryGetValue(line.Main.Path, out var length) ? length : null;
    }

    public bool IsRescued(ClipGroup line) => Rescued.Contains(line.Main.Path);

    public void Copy(ClipGroup line) => Copied.Add(line);

    public void ShowInFolder(ClipGroup line) => Shown.Add(line);

    public void ReadAgain() => Reads++;
}

/// <summary>The Library page on the real screen, with a fake panel: the list, what it does to the window, and the foot.</summary>
public sealed class ScreenLibraryTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const float ListTop = 118, RowX = 300, ThumbX = 750;

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

    private FakeLibrary Open(FakeLibrary library)
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        return library;
    }

    private FakeLibrary OpenWith(int count, int leftOut = 0) => Open(new FakeLibrary().With(count, leftOut));

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Canvas.TextColors.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    private bool ButtonEnabled(string id) => ((Button)Screen.WidgetById(id)!).Enabled;

    private RowList List => (RowList)Screen.WidgetById(SettingsScreen.ClipListId)!;

    // ---------------------------------------------------------------- the side bar

    [Fact]
    public void Without_a_panel_the_side_bar_has_no_line_for_it()
    {
        Screen.SetLibrary(null);
        Assert.Equal(196, Screen.WidgetById("nav")!.Bounds.H);

        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(null);
        Assert.Equal(260, Screen.WidgetById("nav")!.Bounds.H);
        Assert.DoesNotContain("Library", Painted());
    }

    [Fact]
    public void The_side_bar_has_seven_lines_that_fit_above_the_foot()
    {
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(new FakeLibrary());

        var nav = Screen.WidgetById("nav")!;
        Assert.Equal(300, nav.Bounds.H);
        Assert.True(nav.Bounds.Bottom <= SettingsScreen.ViewBottom);
        Assert.Equal(7, NavRows.WithLibrary.Rows.Count);
        Assert.Equal([Section.Now, Section.Library, Section.Hotkeys, Section.Durations, Section.Audio, Section.Export, Section.General], NavRows.WithLibrary.Rows.Select(r => r.Section));
        Assert.Contains("Library", Painted());
    }

    [Fact]
    public void Library_alone_comes_above_the_settings_with_the_caption()
    {
        Assert.Equal([Section.Library, Section.Hotkeys, Section.Durations, Section.Audio, Section.Export, Section.General], NavRows.LibraryOnly.Rows.Select(r => r.Section));

        Screen.SetLibrary(new FakeLibrary());

        Assert.Equal(Section.Hotkeys, Screen.Section);
        Assert.Equal(260, Screen.WidgetById("nav")!.Bounds.H);
    }

    [Fact]
    public void A_click_on_its_line_and_the_arrows_lead_to_the_page_and_past_the_caption()
    {
        var library = new FakeLibrary();
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Assert.Equal(Section.Now, Screen.Section);

        // Now at 88, Library at 128, Hotkeys at 192: the heights of the picture.
        Click(40, 128 + 18);
        Assert.Equal(Section.Library, Screen.Section);

        Screen.Key(Vk.Down, 0);
        Assert.Equal(Section.Hotkeys, Screen.Section);
        Screen.Key(Vk.Up, 0);
        Assert.Equal(Section.Library, Screen.Section);
        Screen.Key(Vk.Up, 0);
        Assert.Equal(Section.Now, Screen.Section);
    }

    [Fact]
    public void The_panel_arriving_does_not_take_the_user_anywhere()
    {
        Screen.SetNow(new FakeNow());
        Screen.ShowSection(Section.Audio);

        Screen.SetLibrary(new FakeLibrary());

        Assert.Equal(Section.Audio, Screen.Section);
    }

    [Theory]
    [InlineData(true, nameof(Section.Now))]
    [InlineData(false, nameof(Section.Hotkeys))]
    public void Taking_the_panel_away_leaves_the_page(bool withNow, string lands)
    {
        if (withNow)
        {
            Screen.SetNow(new FakeNow());
        }

        Screen.SetLibrary(new FakeLibrary().With(3));
        Screen.ShowSection(Section.Library);

        Screen.SetLibrary(null);

        Assert.Equal(lands, Screen.Section.ToString());
        Assert.Null(Screen.WidgetById(SettingsScreen.ClipListId));
    }

    [Fact]
    public void A_panel_arriving_on_the_page_is_read_at_once_and_starts_anew()
    {
        var first = OpenWith(5);
        Screen.SelectClip(FakeLibrary.PathOf(2));

        var second = new FakeLibrary().With(3);
        Screen.SetLibrary(second);

        Assert.Equal(1, second.Reads);
        Assert.Contains(SettingsScreen.NothingSelected, Painted());
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
        Assert.Equal(1, first.Reads);
    }

    [Fact]
    public void The_page_with_no_panel_draws_nothing_wrong()
    {
        Screen.ShowSection(Section.Library);

        var drawn = Painted();

        Assert.DoesNotContain(SettingsScreen.NoClipsTitle, drawn);
        Screen.ReadLibraryAgain();
        Screen.Wheel(1);
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
    }

    // ---------------------------------------------------------------- when it reads

    [Fact]
    public void Coming_to_the_page_asks_for_a_reading_and_other_pages_do_not()
    {
        var library = new FakeLibrary();
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Assert.Equal(0, library.Reads);

        Screen.ShowSection(Section.Audio);
        Screen.ReadLibraryAgain();
        Assert.Equal(0, library.Reads);

        Screen.ShowSection(Section.Library);
        Assert.Equal(1, library.Reads);

        Screen.ShowSection(Section.Library);
        Assert.Equal(1, library.Reads);

        Screen.ReadLibraryAgain();
        Assert.Equal(2, library.Reads);
    }

    [Fact]
    public void A_reading_or_a_length_on_another_page_repaints_nothing()
    {
        var library = new FakeLibrary().With(3);
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Audio);
        var before = _rig.Host.Invalidated;

        Screen.LibraryReadingArrived();
        Screen.LibraryLengthsKnown();

        Assert.Equal(before, _rig.Host.Invalidated);

        // And what it brought is there on the way back.
        Screen.ShowSection(Section.Library);
        Assert.Contains("clip0002", Painted());
    }

    [Fact]
    public void A_length_that_arrives_repaints_the_page_and_moves_nothing()
    {
        var library = new FakeLibrary();
        library.Lines = new ClipLines([FakeLibrary.Clip("clip.mp3")], 0);
        Open(library);
        var before = _rig.Host.Invalidated;
        var drawn = Painted();
        Assert.DoesNotContain("0:30", drawn);
        var nameX = _rig.Canvas.DrawnAt.Last(t => t.Text == "clip").X;

        library.Lengths[FakeLibrary.Folder + "clip.mp3"] = TimeSpan.FromSeconds(30);
        Screen.LibraryLengthsKnown();

        Assert.Equal(before + 1, _rig.Host.Invalidated);
        Assert.Contains("0:30", Painted());
        Assert.Equal(nameX, _rig.Canvas.DrawnAt.Last(t => t.Text == "clip").X);
    }

    // ---------------------------------------------------------------- the list

    [Fact]
    public void Of_2000_rows_a_paint_asks_about_the_six_on_screen()
    {
        var library = OpenWith(2000);

        var drawn = Painted();

        Assert.Equal(6, library.LengthsAsked);
        Assert.Contains("clip0000", drawn);
        Assert.Contains("clip0005", drawn);
        Assert.DoesNotContain("clip0006", drawn);
    }

    [Fact]
    public void The_list_scrolls_by_itself_and_the_page_never_does()
    {
        OpenWith(2000);

        Assert.Equal(0, Screen.MaxScroll);
        Assert.False(List.Scrolls);
        Assert.Equal(new Box(SettingsScreen.Left, ListTop, 560, 240), List.Bounds);
    }

    [Fact]
    public void A_page_after_a_long_one_gets_its_own_scrolling_back()
    {
        OpenWith(2000);

        Screen.ShowSection(Section.General);

        Assert.True(Screen.MaxScroll > 0);
    }

    [Fact]
    public void The_keyboard_walks_the_rows_and_the_foot_follows()
    {
        OpenWith(2000);
        Assert.True(Screen.TabTo(SettingsScreen.ClipListId));
        Assert.DoesNotContain("clip0000.mp3", Painted());

        _rig.Press(Vk.Down);
        Assert.Contains("clip0000.mp3", Painted());

        _rig.Press(Vk.Down, Vk.Down);
        Assert.Contains("clip0002.mp3", Painted());

        _rig.Press(Vk.PageDown);
        Assert.Contains("clip0007.mp3", Painted());

        _rig.Press(Vk.End);
        var drawn = Painted();
        Assert.Contains("clip1999.mp3", drawn);
        Assert.Contains("clip1999", drawn);
        Assert.DoesNotContain("clip0000", drawn);

        _rig.Press(Vk.Home);
        Assert.Contains("clip0000.mp3", Painted());
        Assert.True(ButtonEnabled(SettingsScreen.ClipCopyId));
    }

    [Fact]
    public void A_held_arrow_goes_on_moving()
    {
        OpenWith(10);
        Screen.TabTo(SettingsScreen.ClipListId);
        _rig.Press(Vk.Down);

        Assert.True(Screen.Key(Vk.Down, 0, repeat: true));
        Assert.True(Screen.Key(Vk.Down, 0, repeat: true));

        Assert.Contains("clip0002.mp3", Painted());
    }

    [Fact]
    public void A_click_chooses_the_row_under_the_mouse_and_takes_the_focus()
    {
        OpenWith(2000);

        Click(RowX, RowY(3));

        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Assert.Contains("clip0003.mp3", Painted());
        Assert.True(ButtonEnabled(SettingsScreen.ClipShowId));
    }

    [Fact]
    public void A_click_below_the_last_row_chooses_nothing()
    {
        OpenWith(2);

        Click(RowX, RowY(5));

        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
    }

    [Fact]
    public void The_wheel_moves_the_rows_and_not_the_choice()
    {
        var library = OpenWith(2000);
        Click(RowX, RowY(0));

        Screen.Wheel(-1);

        var drawn = Painted();
        Assert.DoesNotContain("clip0001", drawn);
        Assert.Contains("clip0002", drawn);
        Assert.Contains("clip0007", drawn);

        // The chosen row has scrolled away: the foot still names it.
        Assert.Contains("clip0000.mp3", drawn);
        Assert.Equal(0, Screen.Scroll);
        Assert.Empty(library.Copied);
    }

    [Fact]
    public void The_wheel_on_another_page_still_scrolls_it()
    {
        OpenWith(5);
        Screen.ShowSection(Section.General);

        Screen.Wheel(-1);

        Assert.True(Screen.Scroll > 0);
    }

    [Fact]
    public void Page_keys_with_the_focus_elsewhere_move_the_rows_and_not_the_choice()
    {
        OpenWith(2000);
        Assert.Equal("nav", Screen.FocusId);
        Screen.SelectClip(FakeLibrary.PathOf(1));

        Assert.True(Screen.Key(Vk.PageDown, 0));

        var drawn = Painted();
        Assert.Equal("nav", Screen.FocusId);
        Assert.DoesNotContain("clip0004", drawn);
        Assert.Contains("clip0005", drawn);
        Assert.Contains("clip0001.mp3", drawn);

        Assert.True(Screen.Key(Vk.PageUp, 0));
        Assert.Contains("clip0000", Painted());
    }

    [Fact]
    public void Page_keys_on_another_page_still_scroll_it()
    {
        OpenWith(5);
        Screen.ShowSection(Section.General);

        Assert.True(Screen.Key(Vk.PageDown, 0));

        Assert.True(Screen.Scroll > 0);
    }

    [Fact]
    public void The_thumb_of_the_list_gets_its_presses_and_the_page_has_none_to_take_them()
    {
        OpenWith(2000);

        Screen.MouseDown(ThumbX, ListTop + 238);
        Screen.MouseMove(ThumbX, ListTop + 239);
        Screen.MouseUp(ThumbX, ListTop + 239);

        var drawn = Painted();
        Assert.DoesNotContain("clip0000", drawn);
        Assert.Contains("clip1999", drawn);
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
        Assert.False(_rig.Host.MouseHeld);
    }

    [Fact]
    public void The_mouse_repaints_only_when_it_reaches_another_row()
    {
        OpenWith(2000);
        Screen.MouseMove(RowX, RowY(1));
        Screen.Paint(_rig.Canvas);
        var before = _rig.Host.Invalidated;

        Screen.MouseMove(RowX + 5, RowY(1) + 3);
        Screen.MouseMove(RowX - 5, RowY(1) - 3);
        Assert.Equal(before, _rig.Host.Invalidated);

        Screen.MouseMove(RowX, RowY(2));
        Assert.Equal(before + 1, _rig.Host.Invalidated);

        Screen.MouseMove(RowX, RowY(2) + 1);
        Assert.Equal(before + 1, _rig.Host.Invalidated);
    }

    // ---------------------------------------------------------------- a new reading

    [Fact]
    public void A_new_reading_keeps_the_chosen_clip_by_its_path()
    {
        var library = OpenWith(20);
        Click(RowX, RowY(4));

        var rows = library.Lines!.Lines.ToList();
        rows.Insert(0, FakeLibrary.Clip("clip-new.mp3"));
        library.Lines = new ClipLines(rows, 0);
        Screen.LibraryReadingArrived();

        // The list stays at its head, so the new clip shows, and the choice stays on its clip, now one row lower.
        var drawn = Painted();
        Assert.Contains("clip-new", drawn);
        Assert.Contains("clip0004.mp3", drawn);

        _rig.Press(Vk.Down);
        Assert.Contains("clip0005.mp3", Painted());
    }

    [Fact]
    public void A_new_reading_without_the_chosen_clip_leaves_nothing_selected()
    {
        var library = OpenWith(5);
        Click(RowX, RowY(1));

        library.Lines = new ClipLines(library.Lines!.Lines.Where(r => !r.Main.Path.EndsWith("clip0001.mp3", StringComparison.Ordinal)).ToList(), 0);
        Screen.LibraryReadingArrived();

        Assert.Contains(SettingsScreen.NothingSelected, Painted());
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
    }

    [Fact]
    public void A_reading_never_asks_for_the_keyboard_with_the_focus_on_the_list()
    {
        var library = OpenWith(5);
        Click(RowX, RowY(1));
        _rig.Host.IsForeground = true;
        var before = _rig.Host.FocusRequests;

        for (var i = 0; i < 5; i++)
        {
            library.With(5 + i);
            Screen.LibraryReadingArrived();
            Screen.LibraryLengthsKnown();
        }

        Assert.Equal(before, _rig.Host.FocusRequests);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void A_reading_never_asks_for_the_keyboard_nor_touches_a_field_being_typed_in()
    {
        var library = OpenWith(5);
        Screen.ShowSection(Section.Export);
        Assert.True(Screen.TabTo("export.template"));
        Assert.True(_rig.Host.EditVisible);
        var focus = _rig.Host.FocusRequests;
        var resets = _rig.Host.EditResets;

        library.With(6);
        Screen.LibraryReadingArrived();
        Screen.LibraryLengthsKnown();
        Screen.ReadLibraryAgain();

        Assert.Equal(focus, _rig.Host.FocusRequests);
        Assert.Equal(resets, _rig.Host.EditResets);
        Assert.Equal("export.template", Screen.FocusId);
    }

    [Fact]
    public void A_reading_that_empties_the_list_sends_the_focus_to_the_side_bar_without_the_keyboard()
    {
        var library = OpenWith(3);
        Click(RowX, RowY(0));
        var before = _rig.Host.FocusRequests;

        library.With(0);
        Screen.LibraryReadingArrived();

        Assert.Equal("nav", Screen.FocusId);
        Assert.Equal(before, _rig.Host.FocusRequests);
    }

    [Fact]
    public void A_clip_not_listed_yet_is_chosen_when_a_reading_brings_it()
    {
        var library = OpenWith(3);

        Screen.SelectClip(FakeLibrary.PathOf(7));
        Assert.Contains(SettingsScreen.NothingSelected, Painted());

        library.With(4);
        Screen.LibraryReadingArrived();
        Assert.Contains(SettingsScreen.NothingSelected, Painted());

        library.With(10);
        Screen.LibraryReadingArrived();
        Assert.Contains("clip0007.mp3", Painted());
    }

    [Fact]
    public void A_clip_already_listed_is_chosen_and_brought_on_screen()
    {
        OpenWith(2000);

        Screen.SelectClip(FakeLibrary.PathOf(1500));

        var drawn = Painted();
        Assert.Contains("clip1500.mp3", drawn);
        Assert.Contains("clip1500", drawn);
    }

    [Fact]
    public void Choosing_a_row_drops_a_clip_still_awaited()
    {
        var library = OpenWith(3);
        Screen.SelectClip(FakeLibrary.PathOf(7));
        Click(RowX, RowY(1));

        library.With(10);
        Screen.LibraryReadingArrived();

        Assert.Contains("clip0001.mp3", Painted());
    }

    // ---------------------------------------------------------------- the order of Tab

    [Fact]
    public void Tab_goes_from_the_band_to_the_side_bar_to_the_list_to_the_foot()
    {
        OpenWith(5);
        Screen.SelectClip(FakeLibrary.PathOf(1));

        Assert.Equal(["band.pause", "band.save", "nav", SettingsScreen.ClipFilterId, SettingsScreen.ClipListId, SettingsScreen.ClipCopyId, SettingsScreen.ClipShowId], Screen.StopIds());
    }

    [Fact]
    public void With_nothing_chosen_Tab_stops_on_the_list_and_skips_the_grey_buttons()
    {
        OpenWith(5);

        Assert.Equal(["band.pause", "band.save", "nav", SettingsScreen.ClipFilterId, SettingsScreen.ClipListId], Screen.StopIds());
    }

    [Fact]
    public void An_empty_list_is_no_stop_for_Tab()
    {
        OpenWith(0);

        Assert.Equal(["band.pause", "band.save", "nav", SettingsScreen.ClipFilterId], Screen.StopIds());
    }

    [Fact]
    public void Tab_wraps_around_the_stops_both_ways()
    {
        OpenWith(5);
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipShowId);

        Screen.Key(Vk.Tab, 0);
        Assert.Equal("band.pause", Screen.FocusId);

        Screen.Key(Vk.Tab, Hindsound.Input.HotkeyGesture.ModShift);
        Assert.Equal(SettingsScreen.ClipShowId, Screen.FocusId);
    }

    // ---------------------------------------------------------------- the foot

    [Fact]
    public void With_nothing_chosen_the_foot_says_so_and_its_buttons_are_grey()
    {
        OpenWith(5);

        var drawn = Painted();

        Assert.Contains(SettingsScreen.SelectedClipCaption, drawn);
        Assert.Contains(SettingsScreen.NothingSelected, drawn);
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
        Assert.False(ButtonEnabled(SettingsScreen.ClipShowId));
    }

    [Fact]
    public void A_chosen_clip_gives_its_length_its_size_and_its_name_in_place_of_nothing()
    {
        OpenWith(5);

        Screen.SelectClip(FakeLibrary.PathOf(2));

        var drawn = Painted();
        Assert.DoesNotContain(SettingsScreen.NothingSelected, drawn);
        Assert.Contains(SettingsScreen.SelectedClipCaption, drawn);
        Assert.Contains("720 KB", drawn);
        Assert.Contains("clip0002.mp3", drawn);

        // Five rows, and the foot.
        Assert.Equal(6, drawn.Count(t => t == "0:30"));
    }

    [Fact]
    public void A_length_not_read_yet_leaves_only_the_size()
    {
        var library = new FakeLibrary { Lines = new ClipLines([FakeLibrary.Clip("clip.mp3")], 0) };
        Open(library);

        Screen.SelectClip(FakeLibrary.Folder + "clip.mp3");

        var drawn = Painted();
        Assert.Contains("720 KB", drawn);
        Assert.DoesNotContain("0:00", drawn);
    }

    [Fact]
    public void Two_tracks_are_named_as_the_clip_with_their_sizes_added()
    {
        var two = new ClipGroup([new ClipFile(FakeLibrary.Folder + "demo_system.mp3", 400_000, default), new ClipFile(FakeLibrary.Folder + "demo_mic.mp3", 300_000, default)]);
        var library = new FakeLibrary { Lines = new ClipLines([two], 0) };
        Open(library);

        Screen.SelectClip(two.Main.Path);

        var drawn = Painted();
        Assert.Contains("demo.mp3", drawn);
        Assert.DoesNotContain("demo_system.mp3", drawn);
        Assert.Contains("700 KB", drawn);
        Assert.Contains("2 files", drawn);
    }

    [Fact]
    public void A_clip_in_the_rescue_folder_says_so()
    {
        var library = new FakeLibrary().With(3);
        library.Rescued.Add(FakeLibrary.PathOf(1));
        Open(library);

        Assert.Single(Painted(), t => t == "Rescued");
    }

    [Fact]
    public void A_long_name_stops_before_the_buttons()
    {
        var name = "Hindsound_" + new string('W', 200) + ".mp3";
        var library = new FakeLibrary { Lines = new ClipLines([FakeLibrary.Clip(name)], 0) };
        Open(library);
        Screen.SelectClip(FakeLibrary.Folder + name);

        Painted();

        var copy = Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds;
        var cut = _rig.Canvas.DrawnAt.Single(t => t.Text.EndsWith("...", StringComparison.Ordinal) && t.X == 24);
        Assert.True(cut.X + _rig.Canvas.Measure(cut.Text, Face.Body, 12) <= copy.X);
    }

    [Fact]
    public void The_buttons_act_on_the_chosen_clip_by_mouse_and_by_key()
    {
        var library = OpenWith(5);
        Screen.SelectClip(FakeLibrary.PathOf(3));
        var copy = Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds;
        var show = Screen.WidgetById(SettingsScreen.ClipShowId)!.Bounds;

        Click(copy.MidX, copy.MidY);
        Assert.Equal(FakeLibrary.PathOf(3), Assert.Single(library.Copied).Main.Path);

        Click(show.MidX, show.MidY);
        Assert.Equal(FakeLibrary.PathOf(3), Assert.Single(library.Shown).Main.Path);

        Screen.TabTo(SettingsScreen.ClipCopyId);
        _rig.Press(Vk.Enter);
        Assert.Equal(2, library.Copied.Count);

        // Neither of them is the foot of the other pages, which acts on the last clip.
        Assert.Equal(0, _rig.Backend.Copies);
        Assert.Equal(0, _rig.Backend.Folders);
    }

    [Fact]
    public void Grey_buttons_do_nothing()
    {
        var library = OpenWith(5);
        var copy = Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds;

        Click(copy.MidX, copy.MidY);

        Assert.Empty(library.Copied);
    }

    [Fact]
    public void The_buttons_keep_their_place_and_stay_within_the_window()
    {
        OpenWith(5);
        var empty = Screen.WidgetById(SettingsScreen.ClipShowId)!.Bounds;

        Screen.SelectClip(FakeLibrary.PathOf(3));

        var show = Screen.WidgetById(SettingsScreen.ClipShowId)!.Bounds;
        var copy = Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds;
        Assert.Equal(empty, show);
        Assert.Equal(SettingsScreen.WindowWidth - 24, show.Right);
        Assert.Equal(show.X - 12, copy.Right);
    }

    [Fact]
    public void A_clip_found_gone_says_so_in_place_of_its_name_with_its_row_dimmed_until_the_next_reading()
    {
        var library = OpenWith(5);
        var path = FakeLibrary.PathOf(1);
        Screen.SelectClip(path);
        var chosenColor = ColorOfName("clip0001");

        Screen.ClipGestureEnded(path, ShellOutcome.Gone);

        var drawn = Painted();
        Assert.Contains(SettingsScreen.ClipGoneMessage, drawn);
        Assert.DoesNotContain("clip0001.mp3", drawn);
        Assert.Equal(Theme.Faded, _rig.Canvas.TextColors["clip0001"]);
        Assert.NotEqual(Theme.Faded, chosenColor);
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
        Assert.False(ButtonEnabled(SettingsScreen.ClipShowId));

        // Coming to the page asked for one reading, and finding the clip gone asks for none: the row would go before the message is read.
        Assert.Equal(1, library.Reads);

        Screen.LibraryReadingArrived();

        drawn = Painted();
        Assert.DoesNotContain(SettingsScreen.ClipGoneMessage, drawn);
        Assert.Contains("clip0001.mp3", drawn);
        Assert.True(ButtonEnabled(SettingsScreen.ClipCopyId));
    }

    private uint ColorOfName(string text)
    {
        Painted();
        return _rig.Canvas.TextColors[text];
    }

    [Fact]
    public void A_gone_clip_whose_button_had_the_focus_leaves_it_on_the_list()
    {
        OpenWith(5);
        var path = FakeLibrary.PathOf(1);
        Screen.SelectClip(path);
        Screen.TabTo(SettingsScreen.ClipCopyId);
        var before = _rig.Host.FocusRequests;

        Screen.ClipGestureEnded(path, ShellOutcome.Gone);

        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Assert.Equal(before, _rig.Host.FocusRequests);
    }

    [Fact]
    public void Another_clip_chosen_after_a_gone_one_keeps_its_buttons_and_its_name()
    {
        OpenWith(5);
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.ClipGestureEnded(FakeLibrary.PathOf(1), ShellOutcome.Gone);

        Click(RowX, RowY(2));

        var drawn = Painted();
        Assert.Contains("clip0002.mp3", drawn);
        Assert.DoesNotContain(SettingsScreen.ClipGoneMessage, drawn);
        Assert.True(ButtonEnabled(SettingsScreen.ClipCopyId));

        // The first one is still dimmed, as nothing has looked again yet.
        Assert.Equal(Theme.Faded, _rig.Canvas.TextColors["clip0001"]);
    }

    [Fact]
    public void A_failure_is_said_and_the_next_try_clears_it()
    {
        var library = OpenWith(5);
        var path = FakeLibrary.PathOf(1);
        Screen.SelectClip(path);

        Screen.ClipGestureEnded(path, ShellOutcome.Failed);

        var drawn = Painted();
        Assert.Contains(SettingsScreen.ClipFailedMessage, drawn);
        Assert.DoesNotContain("clip0001.mp3", drawn);
        Assert.True(ButtonEnabled(SettingsScreen.ClipCopyId));

        var before = _rig.Host.Invalidated;
        var copy = Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds;
        Click(copy.MidX, copy.MidY);

        Assert.True(_rig.Host.Invalidated > before);
        Assert.Single(library.Copied);
        drawn = Painted();
        Assert.DoesNotContain(SettingsScreen.ClipFailedMessage, drawn);
        Assert.Contains("clip0001.mp3", drawn);
    }

    [Fact]
    public void A_gesture_that_went_well_changes_nothing()
    {
        OpenWith(5);
        var path = FakeLibrary.PathOf(1);
        Screen.SelectClip(path);
        var before = _rig.Host.Invalidated;

        Screen.ClipGestureEnded(path, ShellOutcome.Done);

        Assert.Equal(before, _rig.Host.Invalidated);
        Assert.Contains("clip0001.mp3", Painted());
    }

    [Theory]
    [InlineData(nameof(Section.Now))]
    [InlineData(nameof(Section.Hotkeys))]
    [InlineData(nameof(Section.General))]
    public void Now_and_the_settings_keep_the_foot_of_the_last_clip(string page)
    {
        OpenWith(5);
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.ShowSection(Enum.Parse<Section>(page));

        var drawn = Painted();

        Assert.Contains("Last clip", drawn);
        Assert.DoesNotContain(SettingsScreen.SelectedClipCaption, drawn);
        Assert.NotNull(Screen.WidgetById("foot.open"));
        Assert.Null(Screen.WidgetById(SettingsScreen.ClipShowId));
    }

    [Fact]
    public void The_foot_of_the_last_clip_is_gone_on_the_page()
    {
        OpenWith(5);
        Screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), new DateTime(2026, 11, 6, 14, 32, 0), 720_000));

        var drawn = Painted();

        Assert.DoesNotContain("Last clip", drawn);
        Assert.Null(Screen.WidgetById("foot.open"));
    }

    [Fact]
    public void A_focus_on_the_foot_of_another_page_finds_its_way_when_the_page_changes()
    {
        OpenWith(5);
        Screen.ShowSection(Section.Now);
        Screen.TabTo("foot.open");

        Screen.ShowSection(Section.Library);

        Assert.Equal("nav", Screen.FocusId);
    }

    // ---------------------------------------------------------------- empty, not read yet, capped

    [Fact]
    public void Before_the_first_reading_the_page_does_not_say_there_are_no_clips()
    {
        Open(new FakeLibrary());

        var drawn = Painted();

        Assert.DoesNotContain(SettingsScreen.NoClipsTitle, drawn);
        Assert.DoesNotContain(SettingsScreen.NoClipsBody, drawn);
        Assert.Contains(SettingsScreen.NothingSelected, drawn);
    }

    [Fact]
    public void An_empty_reading_says_so()
    {
        var library = Open(new FakeLibrary());

        library.With(0);
        Screen.LibraryReadingArrived();

        var drawn = Painted();
        Assert.Contains(SettingsScreen.NoClipsTitle, drawn);
        Assert.Contains(SettingsScreen.NoClipsBody, drawn);
        Assert.Contains(SettingsScreen.NothingSelected, drawn);
        Assert.False(ButtonEnabled(SettingsScreen.ClipCopyId));
    }

    [Fact]
    public void The_empty_message_centres_in_the_page_as_on_the_picture()
    {
        OpenWith(0);

        Painted();

        var title = _rig.Canvas.DrawnAt.Single(t => t.Text == SettingsScreen.NoClipsTitle);
        Assert.Equal(SettingsScreen.Left + SettingsScreen.Width / 2, title.X + _rig.Canvas.Measure(title.Text, Face.Title, 22) / 2);
    }

    [Fact]
    public void A_capped_list_ends_with_the_sentence()
    {
        OpenWith(ClipScanner.MaxLines, leftOut: 37);
        Screen.TabTo(SettingsScreen.ClipListId);
        Assert.DoesNotContain(SettingsScreen.ClipsCappedNote, Painted());

        _rig.Press(Vk.End);

        var drawn = Painted();
        Assert.Contains(SettingsScreen.ClipsCappedNote, drawn);
        Assert.Contains("clip1999", drawn);
    }

    [Fact]
    public void The_sentence_says_the_number_of_the_cap()
    {
        Assert.Contains(ClipScanner.MaxLines.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), SettingsScreen.ClipsCappedNote);
    }

    [Fact]
    public void A_list_under_the_cap_has_no_sentence()
    {
        OpenWith(1000);
        Screen.TabTo(SettingsScreen.ClipListId);

        _rig.Press(Vk.End);

        Assert.DoesNotContain(SettingsScreen.ClipsCappedNote, Painted());
    }

    // ---------------------------------------------------------------- on the real canvas

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void On_the_real_canvas_the_chosen_row_and_the_foot_are_drawn_where_they_should_be(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        Screen.SetCanvas(canvas);
        OpenWith(2000);
        Screen.SelectClip(FakeLibrary.PathOf(1));

        Screen.Paint(canvas);

        // The chosen row has its ground and the next one has not. The foot is its own colour, down to the window's edge.
        Assert.Equal(Theme.Raised, Pixel(canvas, scale, 640, ListTop + 45));
        Assert.Equal(Theme.Base, Pixel(canvas, scale, 640, ListTop + 85));
        Assert.Equal(Theme.Panel, Pixel(canvas, scale, 5, 480));
        Screen.SetCanvas(_rig.Canvas);
    }

    private static unsafe uint Pixel(GdiCanvas canvas, double scale, float x, float y) =>
        canvas.Bits[(int)(y * scale) * canvas.Width + (int)(x * scale)] & 0xFFFFFF;
}
