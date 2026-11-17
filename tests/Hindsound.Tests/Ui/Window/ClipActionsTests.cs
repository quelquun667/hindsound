using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A list of clips with an index and no disk, which says what it was asked to rename, delete, copy or show.</summary>
internal sealed class ActingLibrary : ILibraryPanel
{
    public const string Folder = @"C:\Clips\";

    public ActingLibrary(int count)
    {
        Rows = Enumerable.Range(0, count).Select(i => FakeLibrary.Clip($"clip{i:0000}.mp3")).ToList();
        Lines = new ClipLines(Rows, 0);
    }

    public List<ClipGroup> Rows { get; }

    public ClipLines? Lines { get; set; }

    public LibraryData? Index { get; } = new();

    public LibraryStatus IndexStatus => LibraryStatus.Loaded;

    public List<(ClipGroup Line, string Name)> Renamed { get; } = [];

    public List<ClipGroup> Deleted { get; } = [];

    public List<ClipGroup> Copied { get; } = [];

    public List<ClipGroup> Shown { get; } = [];

    public static ClipGroup TwoFiles(string name) =>
        new([new ClipFile(Folder + name + "_system.mp3", 1000, default), new ClipFile(Folder + name + "_mic.mp3", 1000, default)]);

    /// <summary>A new reading, with these lines.</summary>
    public void Read(IEnumerable<ClipGroup> rows) => Lines = new ClipLines(rows.ToList(), 0);

    public TimeSpan? LengthOf(ClipGroup line) => TimeSpan.FromSeconds(30);

    public bool IsRescued(ClipGroup line) => false;

    public void Copy(ClipGroup line) => Copied.Add(line);

    public void ShowInFolder(ClipGroup line) => Shown.Add(line);

    public void ReadAgain()
    {
    }

    public void Rename(ClipGroup line, string name) => Renamed.Add((line, name));

    public void Delete(ClipGroup line) => Deleted.Add(line);
}

/// <summary>An editor that only says which clip it was asked to load.</summary>
internal sealed class LoadingEditor : IEditorPanel
{
    public List<ClipGroup> Loaded { get; } = [];

    public void Load(ClipGroup clip) => Loaded.Add(clip);

    public long EstimateBytes(EditRequest request) => 0;

    public void Play(EditRequest request)
    {
    }

    public void StopPlaying()
    {
    }

    public void Save(EditRequest request, EditSave save)
    {
    }

    public void HandToDiscord(string path)
    {
    }

    public void Close()
    {
    }
}

/// <summary>The menu of a clip, its "..." button, F2 and Delete, and the boxes that rename a clip or send it to the Recycle Bin.</summary>
public sealed class ClipActionsTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const float ListTop = 118, RowX = 300;

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

    private ActingLibrary Open(int count = 5, bool editor = false)
    {
        var library = new ActingLibrary(count);
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        if (editor)
        {
            Screen.SetEditor(new LoadingEditor());
        }

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
        Click(box.MidX, box.MidY);
    }

    private string PaintedText()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return string.Join(" ", _rig.Canvas.Drawn);
    }

    private bool Enabled(string id) => Screen.WidgetById(id) is { Enabled: true };

    /// <summary>The chosen clip, as Copy sees it.</summary>
    private string? Chosen(ActingLibrary library)
    {
        if (!Enabled(SettingsScreen.ClipCopyId))
        {
            return null;
        }

        ClickOn(SettingsScreen.ClipCopyId);
        return library.Copied[^1].Main.Path;
    }

    private void Pick(ClipMenuItem item)
    {
        var menu = Screen.OpenClipMenu!;
        var row = menu.RowBox(menu.Items.ToList().IndexOf(item));
        Click(row.MidX, row.MidY);
    }

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void The_dots_come_first_in_the_foot_and_only_with_a_clip_chosen()
    {
        Open();
        Assert.False(Enabled(SettingsScreen.ClipMoreId));

        Screen.SelectClip(FakeLibrary.PathOf(1));

        var stops = Screen.StopIds();
        Assert.Equal([SettingsScreen.ClipMoreId, SettingsScreen.ClipCopyId, SettingsScreen.ClipShowId, SettingsScreen.ClipPlayId], stops.TakeLast(4));
        Assert.True(Screen.WidgetById(SettingsScreen.ClipMoreId)!.Bounds.Right < Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds.X);
    }

    [Fact]
    public void A_right_click_on_a_row_chooses_it_and_opens_its_menu_at_the_mouse()
    {
        var library = Open(editor: true);

        Screen.RightClick(RowX, RowY(2));

        var menu = Screen.OpenClipMenu;
        Assert.NotNull(menu);
        Assert.Equal(FakeLibrary.PathOf(2), menu.Clip.Main.Path);
        Assert.Equal(RowX, menu.Bounds.X);
        Assert.Equal(RowY(2), menu.Bounds.Y);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Assert.Contains("Send to soundboard...", PaintedText());
        Screen.Key(Vk.Escape, 0);
        Assert.Equal(FakeLibrary.PathOf(2), Chosen(library));
    }

    [Fact]
    public void Near_the_bottom_right_corner_the_menu_turns_back_into_the_window()
    {
        Open(6, editor: true);

        Screen.RightClick(700, RowY(5));

        var box = Screen.OpenClipMenu!.Bounds;
        Assert.True(box.Right <= SettingsScreen.WindowWidth);
        Assert.True(box.Bottom <= SettingsScreen.WindowHeight);
        Assert.Equal(700, box.Right);
        Assert.Equal(RowY(5), box.Bottom);
    }

    [Theory]
    [InlineData(RowX, 400)]
    [InlineData(RowX, 300)]
    [InlineData(80, 200)]
    [InlineData(300, 98)]
    public void A_right_click_anywhere_but_a_row_does_nothing(float x, float y)
    {
        var library = Open(3);
        Screen.SelectClip(FakeLibrary.PathOf(1));

        Screen.RightClick(x, y);

        Assert.Null(Screen.OpenClipMenu);
        Assert.Equal(FakeLibrary.PathOf(1), Chosen(library));
    }

    [Fact]
    public void A_right_click_over_an_open_box_or_the_editor_does_nothing()
    {
        Open(editor: true);
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);
        Assert.True(Screen.BoxOpen);

        Screen.RightClick(RowX, RowY(3));
        Assert.Null(Screen.OpenClipMenu);

        Screen.Key(Vk.Escape, 0);
        Screen.OpenEditor(FakeLibrary.Clip("clip0001.mp3"), EditorMode.Normal);
        Screen.RightClick(RowX, RowY(3));
        Assert.Null(Screen.OpenClipMenu);
    }

    [Fact]
    public void The_dots_open_the_same_menu_above_them()
    {
        Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));

        ClickOn(SettingsScreen.ClipMoreId);

        var menu = Screen.OpenClipMenu!;
        var dots = Screen.WidgetById(SettingsScreen.ClipMoreId)!.Bounds;
        Assert.Equal(FakeLibrary.PathOf(1), menu.Clip.Main.Path);
        Assert.True(menu.Bounds.Bottom <= dots.Y);
        Assert.Equal(-1, menu.Index);
        Assert.Equal([ClipMenuItem.Rename, ClipMenuItem.Copy, ClipMenuItem.ShowInFolder, ClipMenuItem.Delete], menu.Items);
    }

    [Fact]
    public void By_the_keyboard_the_dots_light_the_first_line_and_the_arrows_go_round()
    {
        Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Assert.True(Screen.TabTo(SettingsScreen.ClipMoreId));

        Screen.Key(Vk.Enter, 0);
        Assert.Equal(ClipMenuItem.Rename, Screen.OpenClipMenu!.Current);

        Screen.Key(Vk.Up, 0);
        Assert.Equal(ClipMenuItem.Delete, Screen.OpenClipMenu!.Current);
        Screen.Key(Vk.Down, 0);
        Screen.Key(Vk.Down, 0);
        Assert.Equal(ClipMenuItem.Copy, Screen.OpenClipMenu!.Current);
    }

    [Fact]
    public void Escape_a_press_outside_and_the_window_losing_the_keyboard_close_it_and_do_nothing_else()
    {
        var library = Open();

        Screen.RightClick(RowX, RowY(1));
        Screen.Key(Vk.Escape, 0);
        Assert.Null(Screen.OpenClipMenu);

        Screen.RightClick(RowX, RowY(1));
        Click(700, 60);
        Assert.Null(Screen.OpenClipMenu);

        Screen.RightClick(RowX, RowY(1));
        Screen.Deactivated();
        Assert.Null(Screen.OpenClipMenu);

        Assert.Empty(library.Copied);
        Assert.Empty(library.Renamed);
        Assert.Empty(library.Deleted);
        Assert.False(Screen.BoxOpen);
    }

    [Fact]
    public void Copy_and_Show_in_folder_from_the_menu_act_on_its_clip()
    {
        var library = Open();

        Screen.RightClick(RowX, RowY(2));
        Pick(ClipMenuItem.Copy);
        Screen.RightClick(RowX, RowY(3));
        Pick(ClipMenuItem.ShowInFolder);

        Assert.Equal(FakeLibrary.PathOf(2), Assert.Single(library.Copied).Main.Path);
        Assert.Equal(FakeLibrary.PathOf(3), Assert.Single(library.Shown).Main.Path);
        Assert.Null(Screen.OpenClipMenu);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Edit_and_Send_to_soundboard_open_the_editor_on_the_clip(bool soundboard)
    {
        Open(editor: true);

        Screen.RightClick(RowX, RowY(2));
        Pick(soundboard ? ClipMenuItem.Soundboard : ClipMenuItem.Edit);

        Assert.True(Screen.EditorOpen);
        Assert.Equal(FakeLibrary.PathOf(2), Screen.Editing!.Value.Clip.Main.Path);
        Assert.Equal(soundboard ? EditorMode.Soundboard : EditorMode.Normal, Screen.Editing!.Value.Mode);
    }

    // ---------------------------------------------------------------- F2 and renaming

    [Fact]
    public void F2_on_the_list_opens_the_box_with_the_name_and_its_extension_beside_it()
    {
        Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);

        Screen.Key(ClipKeys.F2, 0);

        var box = Screen.OpenBoxState!;
        Assert.Equal(NameBoxKind.RenameClip, box.Kind);
        Assert.Equal("clip0001", box.Field.Draft);
        Assert.Equal(NameBox.NameId, Screen.FocusId);
        Assert.Equal("clip0001", _rig.Host.EditText);
        Assert.True(_rig.Host.EditResets > 0);
        var text = PaintedText();
        Assert.Contains("Rename clip", text);
        Assert.Contains("Clip name", text);
        Assert.True(Enabled(NameBox.ConfirmId));

        // The extension stands in grey inside the frame of the field, at its right, where the native box can't cover it.
        var frame = NameBox.Frame(box);
        var field = (TextField)Screen.WidgetById(NameBox.NameId)!;
        Assert.Equal(frame.Right - 24, field.Bounds.Right);
        Assert.Equal(".mp3", field.Suffix);
        Assert.True(field.TextBox.Right + 4 * 7 <= field.Bounds.Right);
        Assert.Contains(".mp3", text);
    }

    [Theory]
    [InlineData("nav")]
    [InlineData(SettingsScreen.ClipFilterId)]
    [InlineData("band.save")]
    public void F2_and_Delete_elsewhere_than_the_list_or_the_foot_open_nothing(string focus)
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        _rig.FocusOn(focus);

        Screen.Key(ClipKeys.F2, 0);
        Screen.Key(ClipKeys.Delete, 0);

        Assert.False(Screen.BoxOpen);
        Assert.Empty(library.Deleted);
    }

    [Fact]
    public void F2_with_nothing_chosen_or_with_a_modifier_opens_nothing()
    {
        Open();
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);
        Assert.False(Screen.BoxOpen);

        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.Key(ClipKeys.F2, Hindsound.Input.HotkeyGesture.ModShift);
        Assert.False(Screen.BoxOpen);
    }

    [Fact]
    public void F2_on_a_button_of_the_foot_renames_the_chosen_clip()
    {
        Open();
        Screen.SelectClip(FakeLibrary.PathOf(3));
        Assert.True(Screen.TabTo(SettingsScreen.ClipPlayId));

        Screen.Key(ClipKeys.F2, 0);

        Assert.Equal("clip0003", Screen.OpenBoxState!.Field.Draft);
    }

    [Fact]
    public void The_name_is_checked_again_at_each_key()
    {
        Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);

        Screen.EditChanged("   ");
        Assert.False(Enabled(NameBox.ConfirmId));
        Assert.Contains(NameBoxState.ClipEmptyText, PaintedText());

        Screen.EditChanged("a new name");
        Assert.True(Enabled(NameBox.ConfirmId));
        Assert.DoesNotContain(NameBoxState.ClipEmptyText, PaintedText());
    }

    [Theory]
    [InlineData(ClipRenameOutcome.Empty, NameBoxState.ClipEmptyText)]
    [InlineData(ClipRenameOutcome.BadCharacters, SettingsEditor.TemplateCharsError)]
    [InlineData(ClipRenameOutcome.Reserved, NameBoxState.ClipReservedText)]
    [InlineData(ClipRenameOutcome.TrailingDotOrSpace, NameBoxState.ClipTrailingText)]
    [InlineData(ClipRenameOutcome.TooLong, NameBoxState.ClipTooLongText)]
    [InlineData(ClipRenameOutcome.Taken, NameBoxState.ClipTakenText)]
    public void Each_refusal_says_its_sentence_and_greys_Rename(ClipRenameOutcome refusal, string text)
    {
        var clip = FakeLibrary.Clip("clip0001.mp3");
        var state = NameBoxState.ForClipRename(clip, SettingsScreen.ClipListId, name => name == "bad" ? refusal : null);

        Assert.True(state.CanConfirm);
        state.Field.Draft = "bad";
        Assert.Equal(text, state.Check("bad").Message);
        Assert.False(state.CanConfirm);
        Assert.False(state.TryConfirm());
    }

    [Fact]
    public void Enter_asks_the_renaming_once_and_a_name_refused_by_the_disk_keeps_the_box_open()
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);
        Screen.EditChanged("best moment");

        Screen.Key(Vk.Enter, 0);
        Screen.Key(Vk.Enter, 0);

        var (line, name) = Assert.Single(library.Renamed);
        Assert.Equal(FakeLibrary.PathOf(1), line.Main.Path);
        Assert.Equal("best moment", name);

        Screen.ClipRenameAnswered(line, ClipRenameOutcome.Taken);

        Assert.True(Screen.BoxOpen);
        Assert.False(Screen.OpenBoxState!.Waiting);
        Assert.Contains(NameBoxState.ClipTakenText, PaintedText());
    }

    [Fact]
    public void The_same_name_to_the_letter_closes_the_box_and_asks_nothing()
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);

        Screen.Key(Vk.Enter, 0);

        Assert.False(Screen.BoxOpen);
        Assert.Empty(library.Renamed);
    }

    [Fact]
    public void Once_renamed_the_reading_chooses_the_clip_at_its_new_path()
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);
        Screen.EditChanged("best moment");
        Screen.Key(Vk.Enter, 0);
        var line = library.Renamed[0].Line;

        Screen.ClipRenameAnswered(line, ClipRenameOutcome.Done);
        Assert.False(Screen.BoxOpen);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);

        var renamed = FakeLibrary.Clip("best moment.mp3");
        library.Read([.. library.Rows.Where(row => row != line), renamed]);
        Screen.LibraryReadingArrived();

        Assert.Equal(renamed.Main.Path, Chosen(library));
    }

    [Theory]
    [InlineData(ClipRenameOutcome.Failed, SettingsScreen.ClipFailedMessage)]
    [InlineData(ClipRenameOutcome.TooLong, NameBoxState.ClipTooLongText)]
    public void A_renaming_that_failed_says_it_on_the_line_through_the_reading_it_brings(ClipRenameOutcome outcome, string text)
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);
        Screen.EditChanged("best moment");
        Screen.Key(Vk.Enter, 0);

        // A failure closes the box: there is nothing in it to change. A reason of the name only closes it when the box went with the page.
        if (outcome == ClipRenameOutcome.TooLong)
        {
            Screen.ShowSection(Section.General);
            Screen.ShowSection(Section.Library);
        }

        Screen.ClipRenameAnswered(library.Renamed[0].Line, outcome);
        Screen.LibraryReadingArrived();

        Assert.False(Screen.BoxOpen);
        Assert.Contains(text, PaintedText());

        Screen.LibraryReadingArrived();
        Assert.DoesNotContain(text, PaintedText());
    }

    [Fact]
    public void A_clip_gone_before_its_renaming_is_said_with_nothing_chosen()
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.F2, 0);
        Screen.EditChanged("best moment");
        Screen.Key(Vk.Enter, 0);
        var line = library.Renamed[0].Line;

        Screen.ClipRenameAnswered(line, ClipRenameOutcome.Gone);
        library.Read(library.Rows.Where(row => row != line));
        Screen.LibraryReadingArrived();

        Assert.Contains(SettingsScreen.ClipGoneMessage, PaintedText());
        Assert.Null(Chosen(library));
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public void Delete_asks_first_and_Cancel_deletes_nothing()
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipListId);

        Screen.Key(ClipKeys.Delete, 0);

        Assert.Empty(library.Deleted);
        Assert.Equal(NameBoxKind.DeleteClip, Screen.OpenBoxState!.Kind);
        Assert.Equal(NameBox.CancelId, Screen.FocusId);
        var text = PaintedText();
        Assert.Contains("Move \"clip0001.mp3\" to the Recycle Bin?", text);
        Assert.DoesNotContain("Both files", text);

        Screen.Key(Vk.Enter, 0);

        Assert.False(Screen.BoxOpen);
        Assert.Empty(library.Deleted);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void The_Delete_of_the_box_sends_the_clip_once()
    {
        var library = Open();
        Screen.RightClick(RowX, RowY(2));
        Pick(ClipMenuItem.Delete);
        Assert.Empty(library.Deleted);

        ClickOn(NameBox.ConfirmId);
        ClickOn(NameBox.ConfirmId);

        Assert.Equal(FakeLibrary.PathOf(2), Assert.Single(library.Deleted).Main.Path);
    }

    [Fact]
    public void A_clip_in_two_files_says_both_go()
    {
        var library = new ActingLibrary(2);
        library.Read([ActingLibrary.TwoFiles("duo"), .. library.Rows]);
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        Screen.SelectClip(ActingLibrary.Folder + "duo_system.mp3");
        Screen.TabTo(SettingsScreen.ClipListId);

        Screen.Key(ClipKeys.Delete, 0);

        var text = PaintedText();
        Assert.Contains("Move \"duo.mp3\" to the Recycle Bin?", text);
        Assert.Contains("Both files go", text);
        Assert.True(NameBox.Frame(Screen.OpenBoxState!).H > NameBox.Frame(NameBoxState.ForClipDelete(FakeLibrary.Clip("a.mp3"), "x")).H);

        Screen.Key(Vk.Escape, 0);
        Screen.Key(ClipKeys.F2, 0);
        Assert.Equal("duo", Screen.OpenBoxState!.Field.Draft);
    }

    [Fact]
    public void Once_in_the_Recycle_Bin_the_next_clip_is_chosen()
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(2));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.Delete, 0);
        ClickOn(NameBox.ConfirmId);
        var line = library.Deleted[0];

        Screen.ClipDeleteAnswered(line, ClipDeleteOutcome.Recycled);
        library.Read(library.Rows.Where(row => row != line));
        Screen.LibraryReadingArrived();

        Assert.False(Screen.BoxOpen);
        Assert.Equal(FakeLibrary.PathOf(3), Chosen(library));
    }

    [Fact]
    public void The_last_clip_sent_to_the_Recycle_Bin_leaves_the_one_above_chosen()
    {
        var library = Open(3);
        Screen.SelectClip(FakeLibrary.PathOf(2));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.Delete, 0);
        ClickOn(NameBox.ConfirmId);

        Screen.ClipDeleteAnswered(library.Deleted[0], ClipDeleteOutcome.Recycled);

        Assert.Equal(FakeLibrary.PathOf(1), Chosen(library));
    }

    [Theory]
    [InlineData(ClipDeleteOutcome.CannotRecycle, NameBoxState.CannotRecycleText)]
    [InlineData(ClipDeleteOutcome.Failed, SettingsScreen.ClipFailedMessage)]
    public void A_deletion_that_did_not_happen_says_why_and_keeps_the_clip_chosen(ClipDeleteOutcome outcome, string text)
    {
        var library = Open();
        Screen.SelectClip(FakeLibrary.PathOf(2));
        Screen.TabTo(SettingsScreen.ClipListId);
        Screen.Key(ClipKeys.Delete, 0);
        ClickOn(NameBox.ConfirmId);

        Screen.ClipDeleteAnswered(library.Deleted[0], outcome);
        Screen.LibraryReadingArrived();

        Assert.False(Screen.BoxOpen);
        Assert.Contains(text, PaintedText());
        Assert.Equal(FakeLibrary.PathOf(2), Chosen(library));
    }

    [Fact]
    public void A_double_click_on_a_row_still_plays_it_and_opens_no_menu()
    {
        var library = new PlayingLibraryFake();
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);

        Screen.MouseDown(RowX, RowY(1));
        Screen.MouseUp(RowX, RowY(1));
        Screen.MouseDoubleClick(RowX, RowY(1));

        Assert.Single(library.Played);
        Assert.Null(Screen.OpenClipMenu);
    }
}
