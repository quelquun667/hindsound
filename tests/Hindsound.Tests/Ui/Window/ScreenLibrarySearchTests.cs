using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The search field of the Library: its place, what it filters, how it keeps company with the menu, and its keys.</summary>
public sealed class ScreenLibrarySearchTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const uint Ctrl = HotkeyGesture.ModControl;
    private const uint KeyF = 'F';
    private const string Cafe = "Café night", Discord = "Discord goal Été", Goal = "Goal replay", Plain = "Plain clip", Zoom = "Zoom call";

    private static readonly string[] Stems = [Cafe, Goal, Discord, Plain, Zoom];

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private Dropdown Filter => (Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!;

    private Widget Search => Screen.WidgetById(SettingsScreen.ClipSearchId)!;

    private static string Clip(string name) => LabeledLibrary.Folder + name + ".mp3";

    /// <summary>Clips with capitals, accents and several words in their names, and stars and collections as the sample has.</summary>
    private static LabeledLibrary Named(int leftOut = 0, params string[] names)
    {
        var all = names.Length > 0 ? names : Stems;
        var library = new LabeledLibrary
        {
            Lines = new ClipLines(all.Select(name => new ClipGroup([new ClipFile(Clip(name), 720_000, default)])).ToList(), leftOut),
            Index = new LibraryData { Collections = [new() { Id = "p", Name = "pets" }, new() { Id = "s", Name = "Sam" }] },
        };
        library.Labels[Clip(all[0])] = new ClipLabels(true, ["p"]);
        library.Labels[Clip(all[1])] = new ClipLabels(false, ["p"]);
        library.Labels[Clip(all[2])] = new ClipLabels(true, []);
        return library;
    }

    private LabeledLibrary Open(LabeledLibrary library)
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        return library;
    }

    private void Type(string text)
    {
        Screen.TabTo(SettingsScreen.ClipSearchId);
        Screen.EditChanged(text);
    }

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Canvas.Strokes.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    /// <summary>The names of the rows on screen, in the order of <see cref="Stems"/>: the stem of each, without its extension, which is drawn apart.</summary>
    private string[] Rows()
    {
        var drawn = Painted();
        return [.. Stems.Where(drawn.Contains)];
    }

    // ---------------------------------------------------------------- the place

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_field_has_a_fixed_place_to_the_right_of_the_menu_the_height_of_it(bool collection)
    {
        Open(Named());
        if (collection)
        {
            Filter.PickValue(LibraryChoice.Of("p").Value);
        }

        var field = Search.Bounds;

        Assert.Equal(new Box(488, 82, 240, 30), field);
        Assert.Equal(Filter.Bounds.Y, field.Y);
        Assert.Equal(Filter.Bounds.H, field.H);
        Assert.Equal(SettingsScreen.Right, field.Right);
        Assert.True(field.W >= 200);
        Assert.Equal(new Box(200, 82, 220, 30), Filter.Bounds);
    }

    [Fact]
    public void The_field_is_a_text_field_with_a_placeholder_while_empty_and_not_in_use()
    {
        Open(Named());

        var drawn = Painted();

        Assert.Contains(SettingsScreen.SearchPlaceholder, drawn);
        Assert.Equal("Search", SettingsScreen.SearchPlaceholder);
        Assert.IsType<TextField>(Search);
    }

    [Fact]
    public void Typed_text_replaces_the_placeholder_and_brings_the_clear_button()
    {
        Open(Named());

        // With the keyboard elsewhere, the field draws its own text: the native box is over it only while it has the focus.
        // Every name holds an a: the rows are the same with and without the text, so only the x differs.
        Type("a");
        Screen.TabTo(SettingsScreen.ClipListId);
        var drawn = Painted();
        var withText = _rig.Canvas.Strokes.Count;
        Assert.DoesNotContain(SettingsScreen.SearchPlaceholder, drawn);
        Assert.Contains("a", drawn);

        Screen.TabTo(SettingsScreen.ClipSearchId);
        Screen.EditChanged(string.Empty);
        Screen.TabTo(SettingsScreen.ClipListId);
        Painted();

        Assert.Equal(withText - 2, _rig.Canvas.Strokes.Count);
    }

    // ---------------------------------------------------------------- what it matches

    [Fact]
    public void Each_key_filters_the_list()
    {
        Open(Named());
        Assert.Equal(Stems, Rows());

        Type("g");
        Assert.Equal([Cafe, Goal, Discord], Rows());
        Screen.EditChanged("go");
        Screen.EditChanged("goa");
        Assert.Equal([Goal, Discord], Rows());
        Screen.EditChanged("goal rep");
        Assert.Equal([Goal], Rows());
        Screen.EditChanged(string.Empty);
        Assert.Equal(Stems, Rows());
    }

    [Theory]
    [InlineData("cafe", Cafe)]
    [InlineData("CAFÉ", Cafe)]
    [InlineData("ete", Discord)]
    [InlineData("été DISCORD", Discord)]
    public void An_accent_or_a_capital_makes_no_difference(string typed, string found)
    {
        Open(Named());

        Type(typed);

        Assert.Equal([found], Rows());
    }

    [Fact]
    public void The_search_reads_the_name_without_its_extension()
    {
        Open(Named());

        Type("mp3");

        Assert.Empty(Rows());
    }

    [Fact]
    public void White_space_alone_keeps_every_row_and_says_no_empty_view()
    {
        Open(Named());

        Type("   ");
        var drawn = Painted();

        Assert.Equal(Stems, Rows());
        Assert.DoesNotContain(drawn, text => text.StartsWith("No clips match", StringComparison.Ordinal));
        Assert.False(Screen.SearchActive);
    }

    // ---------------------------------------------------------------- with the menu

    [Fact]
    public void The_search_works_inside_favorites_and_inside_a_collection()
    {
        Open(Named());

        Type("goal");
        Filter.PickValue(LibraryChoice.Favorites.Value);
        Assert.Equal([Discord], Rows());

        Filter.PickValue(LibraryChoice.Of("p").Value);
        Assert.Equal([Goal], Rows());

        Filter.PickValue(LibraryChoice.Of("s").Value);
        Assert.Empty(Rows());

        Filter.PickValue(LibraryChoice.All.Value);
        Assert.Equal([Goal, Discord], Rows());
        Assert.Equal("goal", Screen.SearchText);
    }

    [Fact]
    public void The_text_stays_while_the_window_is_open_through_other_pages_and_choices_of_the_menu()
    {
        Open(Named());
        Type("goal");

        Screen.ShowSection(Section.Audio);
        Screen.ShowSection(Section.Library);

        Assert.Equal("goal", Screen.SearchText);
        Assert.Equal([Goal, Discord], Rows());
    }

    [Fact]
    public void A_new_panel_clears_the_search()
    {
        Open(Named());
        Type("goal");
        Screen.TabTo(SettingsScreen.ClipListId);

        Screen.SetLibrary(Named());

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(Stems, Rows());
        Assert.Contains(SettingsScreen.SearchPlaceholder, Painted());
    }

    [Fact]
    public void A_new_panel_clears_the_search_the_native_box_holds_too()
    {
        Open(Named());
        Type("goal");
        var resets = _rig.Host.EditResets;

        Screen.SetLibrary(Named());

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(resets + 1, _rig.Host.EditResets);
        Assert.Equal(string.Empty, _rig.Host.EditText);
    }

    [Fact]
    public void Changing_the_text_scrolls_the_list_to_its_top()
    {
        var names = Enumerable.Range(0, 30).Select(i => $"take {i:00}").ToArray();
        Open(Named(0, names));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(Vk.End, 0);
        Assert.DoesNotContain("take 00", Painted());

        Type("take");

        Assert.Contains("take 00", Painted());
    }

    [Fact]
    public void The_clip_chosen_stays_chosen_while_it_is_still_a_row_and_goes_when_it_is_not()
    {
        Open(Named());
        Screen.SelectClip(Clip(Goal));
        Assert.Equal(Clip(Goal), Screen.EditorChosenPath);

        Type("goal");
        Assert.Equal(Clip(Goal), Screen.EditorChosenPath);

        Screen.EditChanged("zoom");
        Assert.Null(Screen.EditorChosenPath);

        // The clip is a row again once the words fit it, but it is no longer the chosen one: a menu lets it go the same way.
        Screen.EditChanged("goal");
        Assert.Null(Screen.EditorChosenPath);
    }

    [Fact]
    public void A_clip_the_search_and_the_menu_hide_is_shown_when_it_is_asked_for()
    {
        Open(Named());
        Filter.PickValue(LibraryChoice.Of("p").Value);
        Type("goal");

        Screen.SelectClip(Clip(Zoom));

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(LibraryChoice.All, Screen.ClipChoice);
        Assert.Equal(Clip(Zoom), Screen.EditorChosenPath);
        Assert.Equal(Stems, Rows());
    }

    [Fact]
    public void A_clip_only_the_search_hides_is_shown_when_it_is_asked_for_with_the_menu_on_all_clips_too()
    {
        Open(Named());
        Type("goal");

        Screen.SelectClip(Clip(Zoom));

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(Clip(Zoom), Screen.EditorChosenPath);
    }

    [Fact]
    public void A_clip_the_search_shows_is_chosen_with_the_search_left_as_it_is()
    {
        Open(Named());
        Type("goal");

        Screen.SelectClip(Clip(Goal));

        Assert.Equal("goal", Screen.SearchText);
        Assert.Equal(Clip(Goal), Screen.EditorChosenPath);
    }

    [Fact]
    public void A_clip_just_saved_that_the_search_hides_is_chosen_when_the_reading_brings_it()
    {
        var library = Open(Named());
        Type("goal");
        var saved = Clip("Fresh save");

        Screen.SelectClip(saved);
        Assert.Equal(string.Empty, Screen.SearchText);

        // Nothing is typed before the reading comes: it lists the clip, and it is chosen. Words typed meanwhile would be the person's own.
        library.Lines = new ClipLines([.. library.Lines!.Lines, new ClipGroup([new ClipFile(saved, 1000, default)])], 0);
        Screen.LibraryReadingArrived();

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(saved, Screen.EditorChosenPath);
    }

    private static ClipLines WithSaved(LabeledLibrary library, string saved) =>
        new([.. library.Lines!.Lines, new ClipGroup([new ClipFile(saved, 1000, default)])], 0);

    [Fact]
    public void A_clip_waiting_to_be_chosen_is_given_up_when_another_choice_of_the_menu_is_picked_and_no_reading_empties_the_search()
    {
        var library = Open(Named());
        var saved = Clip(Goal + " saved");
        Screen.SelectClip(saved);

        // The person moves on before the reading comes: Favorites, then words that the saved clip's name holds.
        Filter.PickValue(LibraryChoice.Favorites.Value);
        Type("goal");
        library.Lines = WithSaved(library, saved);
        library.Labels[saved] = new ClipLabels(true, []);
        Screen.LibraryReadingArrived();

        Assert.Equal("goal", Screen.SearchText);
        Type("goal");
        Screen.LibraryReadingArrived();
        Assert.Equal("goal", Screen.SearchText);
    }

    [Fact]
    public void Typing_gives_up_a_clip_waiting_to_be_chosen_even_on_all_clips()
    {
        var library = Open(Named());
        var saved = Clip("Fresh save");
        Screen.SelectClip(saved);

        Type("fresh");
        library.Lines = WithSaved(library, saved);
        Screen.LibraryReadingArrived();
        Type("fresh x");
        Screen.LibraryReadingArrived();

        Assert.Equal("fresh x", Screen.SearchText);
        Assert.NotEqual(saved, Screen.EditorChosenPath);
    }

    [Fact]
    public void A_clip_waiting_to_be_chosen_is_chosen_when_the_reading_brings_it_and_nothing_was_typed()
    {
        var library = Open(Named());
        var saved = Clip("Fresh save");
        Screen.SelectClip(saved);

        library.Lines = WithSaved(library, saved);
        Screen.LibraryReadingArrived();

        Assert.Equal(saved, Screen.EditorChosenPath);
        Assert.Equal(string.Empty, Screen.SearchText);
    }

    // ---------------------------------------------------------------- nothing found

    [Fact]
    public void A_search_with_no_result_says_so_with_the_text_and_what_to_try()
    {
        Open(Named());

        Type("zebra");
        var drawn = Painted();

        Assert.Contains("No clips match \"zebra\"", drawn);
        Assert.Contains("Try other words, or clear the search.", drawn);
    }

    [Fact]
    public void A_text_too_long_for_the_line_is_cut_inside_the_quotes()
    {
        Open(Named());

        Type(new string('w', 200));
        var shown = Painted().Single(text => text.StartsWith("No clips match \"", StringComparison.Ordinal));

        Assert.EndsWith("\"", shown);
        Assert.True(shown.Length < 100, shown);
    }

    [Fact]
    public void The_empty_view_of_a_menu_choice_is_said_when_nothing_is_searched()
    {
        Open(Named());
        Filter.PickValue(LibraryChoice.Of("s").Value);

        Assert.Contains("No clips in this collection", Painted());

        Type("zebra");
        Assert.Contains("No clips match \"zebra\"", Painted());
        Assert.DoesNotContain("No clips in this collection", Painted());
    }

    [Fact]
    public void The_end_note_says_what_was_searched_only_on_all_clips_with_clips_left_out()
    {
        Open(Named(leftOut: 7));
        Assert.Contains(SettingsScreen.ClipsCappedNote, Painted());

        Type("o");
        var drawn = Painted();
        Assert.DoesNotContain(SettingsScreen.ClipsCappedNote, drawn);
        Assert.Contains(drawn, text => text.StartsWith("Searching the 2,000 most recent", StringComparison.Ordinal));
        Assert.Contains("star", SettingsScreen.ClipsCappedSearchNote, StringComparison.Ordinal);
        Assert.Contains("collection", SettingsScreen.ClipsCappedSearchNote, StringComparison.Ordinal);
        Assert.True(SettingsScreen.ClipsCappedSearchNote.Length < 80);

        Filter.PickValue(LibraryChoice.Favorites.Value);
        Assert.DoesNotContain(Painted(), text => text.Contains("2,000 most recent", StringComparison.Ordinal));

        Screen.TabTo(SettingsScreen.ClipSearchId);
        Screen.EditChanged(string.Empty);
        Filter.PickValue(LibraryChoice.All.Value);
        Assert.Contains(SettingsScreen.ClipsCappedNote, Painted());
    }

    [Fact]
    public void The_end_note_of_a_search_names_no_star_or_collection_when_the_collections_file_cannot_be_read()
    {
        var library = Named(leftOut: 7);
        library.IndexStatus = LibraryStatus.Unreadable;
        Open(library);

        Type("o");
        var drawn = Painted();

        Assert.Contains(SettingsScreen.ClipsCappedSearchPlainNote, drawn);
        Assert.DoesNotContain(SettingsScreen.ClipsCappedSearchNote, drawn);
        Assert.DoesNotContain("star", SettingsScreen.ClipsCappedSearchPlainNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Marks_typed_alone_are_no_search_in_the_window()
    {
        Open(Named(leftOut: 7));

        Type("́");

        Assert.False(Screen.SearchActive);
        Assert.Equal(Stems, Rows());
        Assert.Contains(SettingsScreen.ClipsCappedNote, Painted());
    }

    // ---------------------------------------------------------------- the keys

    [Fact]
    public void Tab_goes_from_the_menu_to_the_field_to_the_list()
    {
        Open(Named());
        Screen.TabTo(SettingsScreen.ClipFilterId);

        Screen.Key(Vk.Tab, 0);
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal(SettingsScreen.ClipFilterId, Screen.FocusId);
    }

    [Fact]
    public void Ctrl_F_puts_the_focus_in_the_field_and_selects_its_text()
    {
        Open(Named());
        Type("goal");
        Screen.TabTo(SettingsScreen.ClipListId);
        var resets = _rig.Host.EditResets;

        Assert.True(Screen.Key(KeyF, Ctrl));

        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
        Assert.True(_rig.Host.EditVisible);
        Assert.Equal("goal", _rig.Host.EditText);
        Assert.Equal(resets + 1, _rig.Host.EditResets);

        // Again, from inside the field: the text is selected again, as the box is told with a reset.
        Assert.True(Screen.Key(KeyF, Ctrl));
        Assert.Equal(resets + 2, _rig.Host.EditResets);
        Assert.Equal("goal", Screen.SearchText);
    }

    [Fact]
    public void Ctrl_F_works_from_the_menu_and_from_the_button_of_a_collection()
    {
        Open(Named());
        Filter.PickValue(LibraryChoice.Of("p").Value);

        Screen.TabTo(SettingsScreen.ClipFilterId);
        Screen.Key(KeyF, Ctrl);
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);

        Screen.TabTo(CollectionActions.MoreId);
        Screen.Key(KeyF, Ctrl);
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
    }

    [Fact]
    public void Ctrl_F_does_nothing_under_a_box_or_a_menu_or_on_another_page()
    {
        Open(Named());
        Filter.PickValue(LibraryChoice.Of("p").Value);
        CollectionMenuClicks.Pick(Screen, CollectionMenuItem.Rename);
        Assert.True(Screen.BoxOpen);

        Screen.Key(KeyF, Ctrl);
        Assert.NotEqual(SettingsScreen.ClipSearchId, Screen.FocusId);
        Screen.Key(Vk.Escape, 0);

        CollectionMenuClicks.OpenFrom(Screen);
        Screen.Key(KeyF, Ctrl);
        Assert.NotEqual(SettingsScreen.ClipSearchId, Screen.FocusId);
        Screen.Key(Vk.Escape, 0);

        Screen.ShowSection(Section.Audio);
        Screen.Key(KeyF, Ctrl);
        Assert.NotEqual(SettingsScreen.ClipSearchId, Screen.FocusId);
        Assert.Null(Screen.WidgetById(SettingsScreen.ClipSearchId));
    }

    [Fact]
    public void The_window_hands_Down_and_Ctrl_F_to_the_screen_only_from_the_field()
    {
        Open(Named());

        Screen.TabTo(SettingsScreen.ClipFilterId);
        Assert.False(Screen.TakesKeyFromField(Vk.Down, 0));
        Assert.False(Screen.TakesKeyFromField(KeyF, Ctrl));

        Screen.TabTo(SettingsScreen.ClipSearchId);
        Assert.True(Screen.TakesKeyFromField(Vk.Down, 0));
        Assert.True(Screen.TakesKeyFromField(KeyF, Ctrl));
        Assert.False(Screen.TakesKeyFromField(Vk.Down, HotkeyGesture.ModShift));
        Assert.False(Screen.TakesKeyFromField(Vk.Up, 0));
        Assert.False(Screen.TakesKeyFromField('A', Ctrl));
    }

    [Fact]
    public void Esc_in_the_field_empties_it_and_keeps_the_focus_there()
    {
        Open(Named());
        Type("goal");

        Assert.True(Screen.Key(Vk.Escape, 0));

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
        Assert.Equal(Stems, Rows());
        Assert.Equal(string.Empty, _rig.Host.EditText);
    }

    [Fact]
    public void Esc_in_an_empty_field_does_what_it_does_anywhere_else_on_the_page()
    {
        Open(Named());
        Screen.TabTo(SettingsScreen.ClipFilterId);
        var elsewhere = Screen.Key(Vk.Escape, 0);
        Screen.TabTo(SettingsScreen.ClipSearchId);

        Assert.Equal(elsewhere, Screen.Key(Vk.Escape, 0));
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
    }

    [Fact]
    public void Esc_empties_white_space_too()
    {
        Open(Named());
        Type("  ");

        Assert.True(Screen.Key(Vk.Escape, 0));

        Assert.Equal(string.Empty, Screen.SearchText);
    }

    [Theory]
    [InlineData(Vk.Down)]
    [InlineData(Vk.Enter)]
    public void Down_or_Enter_in_the_field_goes_to_the_list_and_chooses_its_first_row_if_none_is_chosen(uint key)
    {
        Open(Named());
        Type("goal");
        Assert.Null(Screen.EditorChosenPath);

        Assert.True(Screen.Key(key, 0));

        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Assert.Equal(Clip(Goal), Screen.EditorChosenPath);
    }

    [Fact]
    public void Down_in_the_field_keeps_the_clip_that_was_chosen()
    {
        Open(Named());
        Screen.SelectClip(Clip(Discord));
        Type("goal");

        Screen.Key(Vk.Down, 0);

        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Assert.Equal(Clip(Discord), Screen.EditorChosenPath);
    }

    [Fact]
    public void Down_in_the_field_of_a_search_with_no_result_stays_in_the_field()
    {
        Open(Named());
        Type("zebra");

        Screen.Key(Vk.Down, 0);

        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
    }

    [Fact]
    public void F_on_the_list_stars_the_chosen_clip_and_is_no_search()
    {
        var library = Open(Named());
        Screen.SelectClip(Clip(Plain));
        Screen.TabTo(SettingsScreen.ClipListId);

        Screen.Key('F', 0);

        Assert.Equal([(Clip(Plain), true)], library.StarsAsked);
        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    // ---------------------------------------------------------------- the x

    [Fact]
    public void A_click_on_the_x_empties_the_field_and_brings_every_row_back()
    {
        Open(Named());
        Type("goal");
        var field = Search.Bounds;

        Click(field.Right - 15, field.MidY);

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(Stems, Rows());
        Assert.Equal(string.Empty, _rig.Host.EditText);
    }

    [Fact]
    public void A_click_where_the_x_would_be_in_an_empty_field_changes_nothing_but_the_focus()
    {
        Open(Named());
        var field = Search.Bounds;

        Click(field.Right - 15, field.MidY);

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(SettingsScreen.ClipSearchId, Screen.FocusId);
    }

    [Fact]
    public void The_native_box_leaves_room_for_the_magnifier_and_the_x()
    {
        Open(Named());
        Type("goal");

        var box = _rig.Host.EditBox;
        var field = Search.Bounds;

        Assert.True(box.X >= field.X + 26);
        Assert.True(box.Right <= field.Right - 26);
    }

    // ---------------------------------------------------------------- the sentence of the playback, in the foot

    private const string Sentence = ClipPreview.CanEndUpInClipText;

    private void Plays(string name)
    {
        var line = new ClipGroup([new ClipFile(Clip(name), 720_000, default)]);
        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false });
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, line));
    }

    [Fact]
    public void The_sentence_stands_in_the_foot_in_place_of_the_name_and_not_beside_the_menu()
    {
        Open(Named());
        Screen.SelectClip(Clip(Plain));
        Assert.Contains("Plain clip.mp3", Painted());

        Plays(Plain);
        var drawn = Painted();

        Assert.Contains(Sentence, drawn);
        Assert.DoesNotContain("Plain clip.mp3", drawn);
        Assert.NotNull(Screen.WidgetById(SettingsScreen.ClipSearchId));
    }

    [Fact]
    public void The_sentence_is_in_the_foot_when_nothing_is_chosen_while_something_plays()
    {
        Open(Named());
        Plays(Plain);

        var drawn = Painted();

        Assert.Contains(Sentence, drawn);
        Assert.Contains(SettingsScreen.NothingSelected, drawn);
        Assert.Contains(SettingsScreen.SelectedClipCaption, drawn);
    }

    [Fact]
    public void Nothing_chosen_and_nothing_playing_keeps_the_one_line_foot()
    {
        Open(Named());

        var drawn = Painted();

        Assert.DoesNotContain(Sentence, drawn);
        Assert.Contains(SettingsScreen.NothingSelected, drawn);
    }

    [Fact]
    public void A_clip_gone_or_failed_is_said_before_the_sentence_and_the_sentence_before_the_name()
    {
        var library = Open(Named());
        var plain = Clip(Plain);
        Screen.SelectClip(plain);
        Plays(Plain);
        Assert.Contains(Sentence, Painted());

        Screen.ClipGestureEnded(plain, ShellOutcome.Failed);
        var failed = Painted();
        Assert.Contains(SettingsScreen.ClipFailedMessage, failed);
        Assert.DoesNotContain(Sentence, failed);

        library.Lines = new ClipLines([.. library.Lines!.Lines], 0);
        Screen.LibraryReadingArrived();
        Screen.SelectClip(plain);
        Assert.Contains(Sentence, Painted());

        Screen.ClipGestureEnded(plain, ShellOutcome.Gone);
        var gone = Painted();
        Assert.Contains(SettingsScreen.ClipGoneMessage, gone);
        Assert.DoesNotContain(Sentence, gone);
    }

    [Fact]
    public void The_sentence_goes_when_the_playback_ends_and_the_name_comes_back()
    {
        Open(Named());
        var plain = Clip(Plain);
        Screen.SelectClip(plain);
        Plays(Plain);

        Screen.PreviewChanged(new PreviewChange(PreviewState.Ended, new ClipGroup([new ClipFile(plain, 720_000, default)])));
        var drawn = Painted();

        Assert.DoesNotContain(Sentence, drawn);
        Assert.Contains("Plain clip.mp3", drawn);
    }
}
