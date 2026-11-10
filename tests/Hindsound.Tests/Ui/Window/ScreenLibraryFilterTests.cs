using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A list of clips with made-up collections and stars, and no disk behind it, which says what it was asked.</summary>
internal sealed class LabeledLibrary : ILibraryPanel
{
    public const string Folder = @"C:\Clips\";

    public ClipLines? Lines { get; set; }

    public LibraryData? Index { get; set; }

    public Dictionary<string, ClipLabels> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Path, bool On)> StarsAsked { get; } = [];

    public static string PathOf(int row) => $"{Folder}clip{row:0000}.mp3";

    /// <summary>
    /// Ten clips, a collection called pets with rows 1, 3 and 5, one called Sam with none, and stars on rows 0 and 3. More clips,
    /// to make the list scroll, have no label.
    /// </summary>
    public static LabeledLibrary Sample(int count = 10, int leftOut = 0)
    {
        var library = new LabeledLibrary
        {
            Lines = new ClipLines(Enumerable.Range(0, count).Select(i => new ClipGroup([new ClipFile(PathOf(i), 720_000, default)])).ToList(), leftOut),
            Index = new LibraryData { Collections = [new() { Id = "p", Name = "pets" }, new() { Id = "s", Name = "Sam" }] },
        };
        foreach (var row in new[] { 0, 1, 3, 5 })
        {
            library.Labels[PathOf(row)] = new ClipLabels(row is 0 or 3, row is 1 or 3 or 5 ? ["p"] : []);
        }

        return library;
    }

    public TimeSpan? LengthOf(ClipGroup line) => TimeSpan.FromSeconds(30);

    public bool IsRescued(ClipGroup line) => false;

    public void Copy(ClipGroup line)
    {
    }

    public void ShowInFolder(ClipGroup line)
    {
    }

    public void ReadAgain()
    {
    }

    public ClipLabels LabelsOf(ClipGroup line) => Labels.TryGetValue(line.Main.Path, out var labels) ? labels : new(false, []);

    public void SetFavorite(ClipGroup line, bool favorite) => StarsAsked.Add((line.Main.Path, favorite));
}

/// <summary>The top of the Library page: the menu over the list, the stars of the rows, and Rename and Delete.</summary>
public sealed class ScreenLibraryFilterTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const float ListTop = 118, RowX = 300, StarX = 710;
    private const uint StarOutline = 0x8E95A3;

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private RowList List => (RowList)Screen.WidgetById(SettingsScreen.ClipListId)!;

    private Dropdown Filter => (Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

    private LabeledLibrary Open(LabeledLibrary library)
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        return library;
    }

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    private void ClickOn(string id)
    {
        var box = Screen.WidgetById(id)!.Bounds;
        Click(box.X + box.W / 2, box.MidY);
    }

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Canvas.Strokes.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    /// <summary>Each star with no fill is ten strokes of its outline colour.</summary>
    private int EmptyStars()
    {
        Painted();
        return _rig.Canvas.Strokes.Count(s => s.Rgb == StarOutline) / CanvasShapes.StarCorners;
    }

    // ---------------------------------------------------------------- the place of things

    [Fact]
    public void The_menu_is_on_the_line_of_the_side_bar_and_the_list_shows_six_rows_under_it()
    {
        Open(LabeledLibrary.Sample(20));

        Assert.Equal(new Box(200, 82, 220, 30), Filter.Bounds);
        Assert.Equal(118, List.Bounds.Y);
        Assert.Equal(6 * RowListState.RowHeight, List.Bounds.H);
        Assert.Equal(6, SettingsScreen.ClipRowsShown);
    }

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void The_menu_holds_every_clip_the_favorites_then_the_collections_in_the_order_of_the_file()
    {
        Open(LabeledLibrary.Sample());

        Assert.Equal(["All clips", "Favorites", "pets", "Sam"], Filter.Items.Select(i => i.Label));
        Assert.Equal("All clips", Filter.Items[Filter.SelectedIndex].Label);
    }

    [Fact]
    public void Without_an_index_the_menu_has_only_every_clip_and_the_favorites()
    {
        var library = LabeledLibrary.Sample();
        library.Index = null;
        Open(library);

        Assert.Equal(["All clips", "Favorites"], Filter.Items.Select(i => i.Label));
    }

    [Fact]
    public void All_clips_shows_every_row()
    {
        Open(LabeledLibrary.Sample());

        var drawn = Painted();

        Assert.Contains("clip0000", drawn);
        Assert.Contains("clip0002", drawn);
        Assert.Equal(LibraryChoice.All, Screen.ClipChoice);
    }

    [Fact]
    public void Favorites_shows_only_the_rows_with_a_star()
    {
        Open(LabeledLibrary.Sample());

        Filter.PickValue(LibraryChoice.Favorites.Value);
        var drawn = Painted();

        Assert.Contains("clip0000", drawn);
        Assert.Contains("clip0003", drawn);
        Assert.DoesNotContain("clip0001", drawn);
        Assert.DoesNotContain("clip0002", drawn);
    }

    [Fact]
    public void A_collection_shows_only_its_clips()
    {
        Open(LabeledLibrary.Sample());

        Filter.PickValue(LibraryChoice.Of("p").Value);
        var drawn = Painted();

        Assert.Contains("clip0001", drawn);
        Assert.Contains("clip0003", drawn);
        Assert.Contains("clip0005", drawn);
        Assert.DoesNotContain("clip0000", drawn);
        Assert.DoesNotContain("clip0002", drawn);
        Assert.Equal("pets", Filter.Items[Filter.SelectedIndex].Label);
    }

    [Fact]
    public void An_empty_collection_says_so_and_nothing_more()
    {
        Open(LabeledLibrary.Sample());

        Filter.PickValue(LibraryChoice.Of("s").Value);
        var drawn = Painted();

        Assert.Contains("No clips in this collection", drawn);
        Assert.DoesNotContain(SettingsScreen.NoClipsBody, drawn);
        Assert.DoesNotContain("clip0000", drawn);
    }

    [Fact]
    public void No_favorite_says_so()
    {
        var library = LabeledLibrary.Sample();
        library.Labels.Clear();
        Open(library);

        Filter.PickValue(LibraryChoice.Favorites.Value);

        Assert.Contains("No favorite clips", Painted());
    }

    [Fact]
    public void Only_every_clip_ends_on_the_sentence_of_the_cap()
    {
        Open(LabeledLibrary.Sample(4, leftOut: 500));
        Assert.Contains(SettingsScreen.ClipsCappedNote, Painted());

        Filter.PickValue(LibraryChoice.Favorites.Value);

        Assert.DoesNotContain(SettingsScreen.ClipsCappedNote, Painted());
    }

    [Fact]
    public void A_new_view_starts_at_its_head_and_keeps_the_chosen_clip_if_it_is_still_a_row()
    {
        var library = Open(LabeledLibrary.Sample(40));
        foreach (var row in new[] { 30, 31, 32, 33, 34, 35, 36, 37 })
        {
            library.Labels[LabeledLibrary.PathOf(row)] = new ClipLabels(true, []);
        }

        Screen.SelectClip(LabeledLibrary.PathOf(37));
        Assert.DoesNotContain("clip0000", Painted());

        Filter.PickValue(LibraryChoice.Favorites.Value);

        // Rows 0, 3, then 30 onwards: the head of the view, though the chosen clip is its last row and off screen. The foot names
        // the whole file; a row draws its name in parts.
        var drawn = Painted();
        Assert.Contains("clip0000", drawn);
        Assert.DoesNotContain("clip0037", drawn);
        Assert.Contains("clip0037.mp3", drawn);
        Assert.Contains(SettingsScreen.ClipCopyId, Screen.StopIds());
    }

    [Fact]
    public void A_new_view_without_the_chosen_clip_leaves_nothing_chosen()
    {
        Open(LabeledLibrary.Sample());
        Screen.SelectClip(LabeledLibrary.PathOf(2));
        Assert.Contains(SettingsScreen.ClipCopyId, Screen.StopIds());

        Filter.PickValue(LibraryChoice.Of("p").Value);

        Assert.DoesNotContain(SettingsScreen.ClipCopyId, Screen.StopIds());
        Assert.Contains(SettingsScreen.NothingSelected, Painted());
    }

    [Fact]
    public void A_collection_gone_from_the_next_reading_leaves_every_clip()
    {
        var library = Open(LabeledLibrary.Sample());
        Filter.PickValue(LibraryChoice.Of("p").Value);

        library.Index = new LibraryData { Collections = [new() { Id = "s", Name = "Sam" }] };
        Screen.LibraryReadingArrived();

        Assert.Equal(LibraryChoice.All, Screen.ClipChoice);
        Assert.Contains("clip0002", Painted());
    }

    [Fact]
    public void A_clip_just_saved_brings_back_every_clip_to_be_chosen()
    {
        Open(LabeledLibrary.Sample());
        Filter.PickValue(LibraryChoice.Favorites.Value);

        Screen.SelectClip(LabeledLibrary.PathOf(2));

        Assert.Equal(LibraryChoice.All, Screen.ClipChoice);
        Assert.Contains(SettingsScreen.ClipCopyId, Screen.StopIds());
    }

    [Fact]
    public void The_keyboard_steps_through_the_menu_and_opens_it()
    {
        Open(LabeledLibrary.Sample());
        Screen.TabTo(SettingsScreen.ClipFilterId);

        _rig.Press(Vk.Down);
        Assert.Equal(LibraryChoice.Favorites, Screen.ClipChoice);

        _rig.Press(Vk.Home, Vk.Enter, Vk.Down, Vk.Down, Vk.Enter);
        Assert.Equal(LibraryChoice.Of("p"), Screen.ClipChoice);
        Assert.Equal(SettingsScreen.ClipFilterId, Screen.FocusId);
    }

    // ---------------------------------------------------------------- Rename and Delete

    [Fact]
    public void Rename_and_Delete_are_there_only_for_a_collection()
    {
        Open(LabeledLibrary.Sample());
        Assert.Null(Screen.WidgetById(CollectionActions.RenameId));

        Filter.PickValue(LibraryChoice.Favorites.Value);
        Assert.Null(Screen.WidgetById(CollectionActions.DeleteId));

        Filter.PickValue(LibraryChoice.Of("s").Value);
        Assert.NotNull(Screen.WidgetById(CollectionActions.RenameId));
        Assert.NotNull(Screen.WidgetById(CollectionActions.DeleteId));
        Assert.True(Screen.WidgetById(CollectionActions.RenameId)!.Bounds.X > Filter.Bounds.Right);
    }

    [Fact]
    public void Rename_and_Delete_open_the_box_of_the_collection_shown()
    {
        Open(LabeledLibrary.Sample());
        Filter.PickValue(LibraryChoice.Of("p").Value);

        ClickOn(CollectionActions.RenameId);
        var rename = Screen.OpenBoxState!;
        Screen.Key(Vk.Escape, 0);
        ClickOn(CollectionActions.DeleteId);
        var delete = Screen.OpenBoxState!;

        Assert.Equal((NameBoxKind.Rename, "p", "pets"), (rename.Kind, rename.CollectionId, rename.Name));
        Assert.Equal((NameBoxKind.Delete, "p", "pets"), (delete.Kind, delete.CollectionId, delete.Name));
    }

    [Fact]
    public void Tab_goes_from_the_menu_to_Rename_and_Delete_then_to_the_list()
    {
        Open(LabeledLibrary.Sample());
        Filter.PickValue(LibraryChoice.Of("p").Value);
        Screen.SelectClip(LabeledLibrary.PathOf(1));

        Assert.Equal(
            ["band.pause", "band.save", "nav", SettingsScreen.ClipFilterId, CollectionActions.RenameId, CollectionActions.DeleteId, SettingsScreen.ClipListId, ChipLine.ChipsId, ChipLine.NewId, SettingsScreen.ClipCopyId, SettingsScreen.ClipShowId, SettingsScreen.ClipPlayId],
            Screen.StopIds());
    }

    [Fact]
    public void An_empty_collection_keeps_Rename_and_Delete_and_the_list_is_no_stop()
    {
        Open(LabeledLibrary.Sample());
        Filter.PickValue(LibraryChoice.Of("s").Value);

        Assert.Equal(["band.pause", "band.save", "nav", SettingsScreen.ClipFilterId, CollectionActions.RenameId, CollectionActions.DeleteId], Screen.StopIds());
    }

    // ---------------------------------------------------------------- the stars

    [Fact]
    public void Every_row_has_a_star_and_only_the_favorites_are_filled()
    {
        Open(LabeledLibrary.Sample());

        // Six rows on screen, two of them favorites.
        Assert.Equal(4, EmptyStars());
    }

    [Fact]
    public void Without_an_index_no_row_has_a_star()
    {
        var library = LabeledLibrary.Sample();
        library.Index = null;
        Open(library);

        Assert.Equal(0, EmptyStars());
    }

    [Fact]
    public void A_press_on_the_star_asks_for_it_and_does_not_choose_the_row()
    {
        var library = Open(LabeledLibrary.Sample());

        Click(StarX, RowY(2));

        Assert.Equal([(LabeledLibrary.PathOf(2), true)], library.StarsAsked);
        Assert.DoesNotContain(SettingsScreen.ClipCopyId, Screen.StopIds());
        Assert.Equal(3, EmptyStars());
    }

    [Fact]
    public void A_press_on_a_filled_star_takes_it_off_and_leaves_the_chosen_row()
    {
        var library = Open(LabeledLibrary.Sample());
        Screen.SelectClip(LabeledLibrary.PathOf(1));

        Click(StarX, RowY(0));

        Assert.Equal([(LabeledLibrary.PathOf(0), false)], library.StarsAsked);
        Assert.Equal(5, EmptyStars());
        Assert.Contains(SettingsScreen.ClipCopyId, Screen.StopIds());
        Click(RowX, RowY(1));
        Assert.Single(library.StarsAsked);
    }

    [Fact]
    public void Without_an_index_a_press_where_the_star_would_be_chooses_the_row()
    {
        var library = LabeledLibrary.Sample();
        library.Index = null;
        Open(library);

        Click(StarX, RowY(2));

        Assert.Empty(library.StarsAsked);
        Assert.Contains(SettingsScreen.ClipCopyId, Screen.StopIds());
    }

    [Fact]
    public void F_puts_the_star_on_the_chosen_row_and_takes_it_off_again()
    {
        var library = Open(LabeledLibrary.Sample());
        Screen.SelectClip(LabeledLibrary.PathOf(2));
        Screen.TabTo(SettingsScreen.ClipListId);

        _rig.Press(RowList.StarKey);
        _rig.Press(RowList.StarKey);

        Assert.Equal([(LabeledLibrary.PathOf(2), true), (LabeledLibrary.PathOf(2), false)], library.StarsAsked);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void F_with_nothing_chosen_asks_for_nothing()
    {
        var library = Open(LabeledLibrary.Sample());
        Screen.TabTo(SettingsScreen.ClipListId);

        _rig.Press(RowList.StarKey);

        Assert.Empty(library.StarsAsked);
    }

    [Fact]
    public void F_with_ctrl_held_is_not_the_star()
    {
        var library = Open(LabeledLibrary.Sample());
        Screen.SelectClip(LabeledLibrary.PathOf(2));
        Screen.TabTo(SettingsScreen.ClipListId);

        Screen.Key(RowList.StarKey, Hindsound.Input.HotkeyGesture.ModControl);

        Assert.Empty(library.StarsAsked);
    }

    [Fact]
    public void The_star_asked_for_shows_until_a_reading_tells_how_the_file_has_it()
    {
        var library = Open(LabeledLibrary.Sample());
        Click(StarX, RowY(2));
        Assert.Equal(3, EmptyStars());

        // The write failed: the file has no star there, and the reading says so.
        Screen.LibraryReadingArrived();

        Assert.Equal(4, EmptyStars());
        Assert.Single(library.StarsAsked);
    }

    [Fact]
    public void A_reading_that_comes_before_the_one_of_the_star_leaves_the_star_lit()
    {
        Open(LabeledLibrary.Sample());
        Click(StarX, RowY(2));
        Assert.Equal(3, EmptyStars());

        // A look made before the star was written: its labels don't have the star yet, and an action is still on its way.
        Screen.LibraryReadingArrived(actionsStillWaiting: true);
        Assert.Equal(3, EmptyStars());

        // The reading of the action itself is the one that says how the file has it.
        Screen.LibraryReadingArrived();
        Assert.Equal(4, EmptyStars());
    }

    // ---------------------------------------------------------------- on the real canvas

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void On_the_real_canvas_a_favorite_has_a_filled_star_and_the_others_an_empty_one(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        Screen.SetCanvas(canvas);
        Open(LabeledLibrary.Sample());

        Screen.Paint(canvas);

        // The centre of a star: filled on row 0, the ground of the page through an empty one on row 2.
        Assert.NotEqual(Theme.Base, Pixel(canvas, scale, StarX, RowY(0)));
        Assert.Equal(Theme.Base, Pixel(canvas, scale, StarX, RowY(2)));
        Screen.SetCanvas(_rig.Canvas);
    }

    private static unsafe uint Pixel(GdiCanvas canvas, double scale, float x, float y) =>
        canvas.Bits[(int)(y * scale) * canvas.Width + (int)(x * scale)] & 0xFFFFFF;
}
