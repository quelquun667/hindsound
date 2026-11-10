using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Ten clips and made-up collections, with no disk behind them, which say what they were asked.</summary>
internal sealed class CollectionLibrary : ILibraryPanel
{
    private static readonly string[] Names = ["pets", "Sam", "Alex", "dogs", "cats", "trips", "games", "music"];

    public ClipLines? Lines { get; set; }

    public LibraryData? Index { get; set; }

    public LibraryStatus IndexStatus { get; set; } = LibraryStatus.Loaded;

    public Dictionary<string, ClipLabels> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Path, string Id, bool On)> SetIns { get; } = [];

    /// <summary>What the box sent, in words.</summary>
    public List<string> Asked { get; } = [];

    public static string PathOf(int row) => $@"C:\Clips\clip{row:0000}.mp3";

    /// <summary>The first collections of pets, Sam, Alex, dogs, cats, trips, games, music, as c0, c1...; row 1 is in c1.</summary>
    public static CollectionLibrary Sample(int collections = 3)
    {
        var library = new CollectionLibrary
        {
            Lines = new ClipLines(Enumerable.Range(0, 10).Select(i => new ClipGroup([new ClipFile(PathOf(i), 720_000, default)])).ToList(), 0),
            Index = new LibraryData { Collections = Names.Take(collections).Select((name, i) => new LibraryCollection { Id = "c" + i, Name = name }).ToList() },
        };
        library.Labels[PathOf(1)] = new ClipLabels(false, ["c1"]);
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

    /// <summary>What the window gets back when it asks for a new reading. Null: the panel never answers.</summary>
    public Action? OnReadAgain { get; set; }

    public void ReadAgain() => OnReadAgain?.Invoke();

    public ClipLabels LabelsOf(ClipGroup line) => Labels.TryGetValue(line.Main.Path, out var labels) ? labels : new(false, []);

    public void SetIn(ClipGroup line, string collectionId, bool inCollection) => SetIns.Add((line.Main.Path, collectionId, inCollection));

    public void CreateCollection(string name) => Asked.Add("create " + name);

    public void RenameCollection(string collectionId, string name) => Asked.Add($"rename {collectionId} {name}");

    public void DeleteCollection(string collectionId) => Asked.Add("delete " + collectionId);

    public void StartOver() => Asked.Add("start over");
}

/// <summary>A canvas that counts the dimming, and the texts.</summary>
internal sealed class DimCountingCanvas : ICanvas
{
    public int Dims { get; private set; }

    public List<string> Texts { get; } = [];

    /// <summary>A real canvas that measures the text, when the lines must break where they would on screen.</summary>
    public ICanvas? Measurer { get; init; }

    public void SetOrigin(float dy) { }
    public void FillRect(Box box, uint rgb) { }
    public void FillRound(Box box, float radius, uint rgb) { }
    public void Frame(Box box, float radius, uint fill, uint border) { }
    public void Ring(Box box, float radius, float thickness, uint rgb) { }
    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) { }
    public void Glow(Box box, float sigma, float opacity, uint rgb) { }
    public void Text(string text, Face face, float size, float x, float top, uint rgb) => Texts.Add(text);
    public float Measure(string text, Face face, float size) => Measurer?.Measure(text, face, size) ?? text.Length * size * 0.5f;
    public float LineHeight(Face face, float size) => Measurer?.LineHeight(face, size) ?? size * 1.3f;
    public void WarningIcon(float x, float y, float size, uint rgb) { }
    public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) { }
    public void Dim(float opacity) => Dims++;
}

/// <summary>The bottom of the Library page: the line of collections, its +N, the box of a collection and the sentences in amber.</summary>
public sealed class ScreenCollectionsTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const string NotWrittenText = "Couldn't save that. Nothing changed.";
    private const string FolderNotReadText = "Can't check this clip's folder right now. Try again.";
    private const string CorruptText = "Your collections file has an error. A copy is kept before anything changes.";

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private CollectionChips? Chips => Screen.WidgetById(ChipLine.ChipsId) as CollectionChips;

    private CollectionLibrary Open(CollectionLibrary library, int? chosen = 2)
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        if (chosen is { } row)
        {
            Screen.SelectClip(CollectionLibrary.PathOf(row));
        }

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

    private void ClickChip(int index)
    {
        var box = Chips!.Fit.Chips[index].Box;
        Click(box.X + 4, box.MidY);
    }

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    private IEnumerable<string> ChipIds() => Chips!.Fit.Chips.Select(chip => chip.Entry.Id);

    private void OpenNewBox(string? typed = null)
    {
        ClickOn(ChipLine.NewId);
        if (typed is not null)
        {
            Screen.EditChanged(typed);
        }
    }

    // ---------------------------------------------------------------- the line

    [Fact]
    public void The_line_sits_under_the_six_rows_with_New_collection_on_the_right()
    {
        Open(CollectionLibrary.Sample());

        Assert.Equal(ChipLine.Top, Chips!.Bounds.Y);
        Assert.True(ChipLine.Top >= 118 + SettingsScreen.ClipRowsShown * RowListState.RowHeight);
        Assert.Equal(SettingsScreen.Right, Screen.WidgetById(ChipLine.NewId)!.Bounds.Right);
        Assert.Contains(ChipLine.Label, Painted());
        Assert.Contains(ChipLine.NewLabel, Painted());
    }

    [Fact]
    public void With_no_clip_chosen_there_is_no_line()
    {
        Open(CollectionLibrary.Sample(), chosen: null);

        Assert.Null(Chips);
        Assert.Null(Screen.WidgetById(ChipLine.NewId));
        Assert.DoesNotContain(ChipLine.Label, Painted());
    }

    [Fact]
    public void Tab_goes_from_the_list_to_the_line_then_to_New_collection_then_to_the_foot()
    {
        Open(CollectionLibrary.Sample());

        Assert.Equal(
            ["band.pause", "band.save", "nav", SettingsScreen.ClipFilterId, SettingsScreen.ClipListId, ChipLine.ChipsId, ChipLine.NewId, SettingsScreen.ClipCopyId, SettingsScreen.ClipShowId, SettingsScreen.ClipPlayId],
            Screen.StopIds());
    }

    [Fact]
    public void A_click_on_a_chip_asks_and_a_second_before_the_answer_takes_the_request_back()
    {
        var library = Open(CollectionLibrary.Sample());

        ClickChip(0);
        ClickChip(0);

        var path = CollectionLibrary.PathOf(2);
        Assert.Equal([(path, "c0", true), (path, "c0", false)], library.SetIns);
    }

    [Fact]
    public void Once_answered_the_line_starts_again_from_the_file()
    {
        var library = Open(CollectionLibrary.Sample());

        ClickChip(0);
        Screen.CollectionTickAnswered("c0", LibraryOutcome.Done);
        ClickChip(0);

        // The file still has the clip out of pets: without the answer, the second click would have asked to take it out.
        Assert.True(library.SetIns[1].On);
    }

    [Fact]
    public void Ticked_collections_come_first_when_the_clip_changes_and_a_tick_moves_nothing()
    {
        var library = Open(CollectionLibrary.Sample());
        Screen.SelectClip(CollectionLibrary.PathOf(1));
        Assert.Equal(["c1", "c0", "c2"], ChipIds());

        ClickChip(2);
        library.Labels[CollectionLibrary.PathOf(1)] = new ClipLabels(false, ["c1", "c2"]);
        Screen.LibraryReadingArrived();
        Screen.CollectionTickAnswered("c2", LibraryOutcome.Done);

        Assert.Equal(["c1", "c0", "c2"], ChipIds());
    }

    [Fact]
    public void A_failed_tick_is_said_in_amber_in_place_of_the_chips_until_the_next_reading()
    {
        Open(CollectionLibrary.Sample());

        ClickChip(0);
        Screen.CollectionTickAnswered("c0", LibraryOutcome.NotWritten);

        Assert.Contains(NotWrittenText, Painted());
        Assert.Null(Chips);
        Assert.NotNull(Screen.WidgetById(ChipLine.NewId));

        Screen.LibraryReadingArrived();

        Assert.NotNull(Chips);
        Assert.DoesNotContain(NotWrittenText, Painted());
    }

    [Fact]
    public void A_folder_not_read_is_said_without_calling_the_clip_gone_and_another_clip_takes_it_away()
    {
        Open(CollectionLibrary.Sample());

        ClickChip(0);
        Screen.CollectionTickAnswered("c0", LibraryOutcome.FolderNotRead);
        Assert.Contains(FolderNotReadText, Painted());
        Assert.DoesNotContain(SettingsScreen.ClipGoneMessage, Painted());

        Screen.SelectClip(CollectionLibrary.PathOf(3));

        Assert.DoesNotContain(FolderNotReadText, Painted());
        Assert.NotNull(Chips);
    }

    // ---------------------------------------------------------------- the +N

    [Fact]
    public void The_plus_opens_a_list_whose_boxes_are_asked_from_the_last_request()
    {
        var library = Open(CollectionLibrary.Sample(8));
        var plus = Chips!.Fit.Plus!.Value;
        var hidden = Chips.Fit.Hidden;

        Click(plus.X + plus.W / 2, plus.MidY);
        Assert.True(Screen.PopupOpen);
        Assert.Contains(hidden[0].Name, Painted());

        var list = Screen.MoreList!.Bounds;
        Click(list.X + 40, list.Y + CheckedListPopup.Pad + CheckedListPopup.RowHeight / 2);
        Click(list.X + 40, list.Y + CheckedListPopup.Pad + CheckedListPopup.RowHeight / 2);

        var path = CollectionLibrary.PathOf(2);
        Assert.Equal([(path, hidden[0].Id, true), (path, hidden[0].Id, false)], library.SetIns);
        Assert.True(Screen.PopupOpen);

        Screen.Key(Vk.Escape, 0);
        Assert.False(Screen.PopupOpen);
        Assert.Equal(ChipLine.ChipsId, Screen.FocusId);
    }

    [Fact]
    public void What_was_ticked_in_the_list_of_the_plus_comes_to_the_front_once_it_closes()
    {
        var library = Open(CollectionLibrary.Sample(8));
        var last = Chips!.Fit.Hidden[^1].Id;
        library.Labels[CollectionLibrary.PathOf(2)] = new ClipLabels(false, [last]);
        var plus = Chips.Fit.Plus!.Value;
        Click(plus.X + plus.W / 2, plus.MidY);

        Screen.Key(Vk.Escape, 0);

        Assert.Equal(last, ChipIds().First());
    }

    // ---------------------------------------------------------------- the box

    [Fact]
    public void New_collection_opens_the_box_on_its_field_and_Tab_stays_inside()
    {
        Open(CollectionLibrary.Sample());

        OpenNewBox();
        Assert.True(Screen.BoxOpen);
        Assert.Equal(NameBox.NameId, Screen.FocusId);

        // Create is grey with nothing typed: Tab goes round the field and Cancel.
        Screen.Key(Vk.Tab, 0);
        Assert.Equal(NameBox.CancelId, Screen.FocusId);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal(NameBox.NameId, Screen.FocusId);

        Screen.EditChanged("dogs");
        Screen.Key(Vk.Tab, 0);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal(NameBox.ConfirmId, Screen.FocusId);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal(NameBox.NameId, Screen.FocusId);
        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal(NameBox.ConfirmId, Screen.FocusId);
    }

    [Fact]
    public void Enter_sends_the_name_once_and_the_box_waits_for_the_answer_then_gives_the_focus_back()
    {
        var library = Open(CollectionLibrary.Sample());
        OpenNewBox("dogs");

        Screen.Key(Vk.Enter, 0);
        Screen.Key(Vk.Enter, 0);
        Screen.Key(Vk.Escape, 0);

        Assert.Equal(["create dogs"], library.Asked);
        Assert.True(Screen.BoxOpen);
        Assert.True(Screen.OpenBoxState!.Waiting);
        Assert.Equal(NameBox.NameId, Screen.FocusId);

        Screen.CollectionBoxAnswered(LibraryOutcome.Done);

        Assert.False(Screen.BoxOpen);
        Assert.Equal(ChipLine.NewId, Screen.FocusId);
    }

    [Theory]
    [InlineData("pets", NameBoxState.TakenText)]
    [InlineData("PETS ", NameBoxState.TakenText)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", NameBoxState.TooLongText)]
    public void A_name_that_cannot_be_kept_says_why_under_the_field_and_Create_stays_grey(string typed, string reason)
    {
        var library = Open(CollectionLibrary.Sample());
        OpenNewBox(typed);

        Assert.Equal(reason, Screen.OpenBoxState!.Field.Message);
        Assert.False(Screen.WidgetById(NameBox.ConfirmId)!.Enabled);
        Assert.Contains(reason, Painted());

        Screen.Key(Vk.Enter, 0);
        Assert.Empty(library.Asked);
    }

    [Fact]
    public void A_name_refused_by_the_file_keeps_the_box_open_with_the_reason()
    {
        Open(CollectionLibrary.Sample());
        OpenNewBox("dogs");
        Screen.Key(Vk.Enter, 0);

        Screen.CollectionBoxAnswered(LibraryOutcome.NameTaken);

        Assert.True(Screen.BoxOpen);
        Assert.False(Screen.OpenBoxState!.Waiting);
        Assert.Equal(NameBoxState.TakenText, Screen.OpenBoxState.Field.Message);
        Assert.Equal(NameBox.NameId, Screen.FocusId);
    }

    [Fact]
    public void A_failed_write_closes_the_box_and_is_said_on_the_line()
    {
        Open(CollectionLibrary.Sample());
        OpenNewBox("dogs");
        Screen.Key(Vk.Enter, 0);

        Screen.CollectionBoxAnswered(LibraryOutcome.NotWritten);

        Assert.False(Screen.BoxOpen);
        Assert.Contains(NotWrittenText, Painted());
    }

    [Fact]
    public void Esc_closes_a_rename_with_nothing_asked_and_the_focus_goes_back_to_Rename()
    {
        var library = Open(CollectionLibrary.Sample());
        ((Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!).PickValue(LibraryChoice.Of("c0").Value);

        ClickOn(CollectionActions.RenameId);
        Assert.Equal("pets", Screen.OpenBoxState!.Field.Draft);
        Screen.Key(Vk.Escape, 0);

        Assert.False(Screen.BoxOpen);
        Assert.Equal(CollectionActions.RenameId, Screen.FocusId);
        Assert.Empty(library.Asked);
    }

    [Fact]
    public void A_rename_sends_the_new_name_of_that_collection()
    {
        var library = Open(CollectionLibrary.Sample());
        ((Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!).PickValue(LibraryChoice.Of("c0").Value);
        ClickOn(CollectionActions.RenameId);

        Screen.EditChanged("dogs");
        ClickOn(NameBox.ConfirmId);

        Assert.Equal(["rename c0 dogs"], library.Asked);
    }

    [Fact]
    public void A_deletion_lands_on_Cancel_and_once_done_the_focus_goes_to_the_menu()
    {
        var library = Open(CollectionLibrary.Sample());
        ((Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!).PickValue(LibraryChoice.Of("c0").Value);

        ClickOn(CollectionActions.DeleteId);
        Assert.Equal(NameBox.CancelId, Screen.FocusId);
        Assert.Contains("Delete collection \"pets\"? Your clips stay.", Painted());
        Screen.Key(Vk.Enter, 0);
        Assert.False(Screen.BoxOpen);
        Assert.Empty(library.Asked);

        ClickOn(CollectionActions.DeleteId);
        Screen.Key(Vk.Tab, 0);
        Screen.Key(Vk.Enter, 0);
        Assert.Equal(["delete c0"], library.Asked);

        Screen.CollectionBoxAnswered(LibraryOutcome.Done);
        Assert.Equal(SettingsScreen.ClipFilterId, Screen.FocusId);
    }

    [Fact]
    public void Behind_the_box_the_window_is_dimmed_once_and_takes_no_click_wheel_or_key()
    {
        Open(CollectionLibrary.Sample());
        var canvas = new DimCountingCanvas();
        Screen.Paint(canvas);
        Assert.Equal(0, canvas.Dims);

        OpenNewBox();
        Screen.Paint(canvas);
        Assert.Equal(1, canvas.Dims);

        // A row of the list, the foot, the wheel and the page keys.
        Click(300, 118 + 20);
        ClickOn(SettingsScreen.ClipCopyId);
        Screen.Wheel(-3);
        Screen.Key(Vk.PageDown, 0);

        Assert.True(Screen.BoxOpen);
        Assert.Equal(NameBox.NameId, Screen.FocusId);
        Assert.Contains("clip0002.mp3", Painted());
    }

    [Fact]
    public void No_partial_paint_while_the_box_is_open()
    {
        Open(CollectionLibrary.Sample());
        OpenNewBox();
        var before = _rig.Host.Invalidated;

        Screen.LibraryLengthsKnown();

        Assert.Equal(before, _rig.Host.Invalidated);
    }

    [Fact]
    public void Leaving_the_page_closes_the_box_and_the_list_of_the_plus()
    {
        Open(CollectionLibrary.Sample());
        OpenNewBox();

        Screen.ShowSection(Section.Now);

        Assert.False(Screen.BoxOpen);
        Assert.False(Screen.PopupOpen);
    }

    // ---------------------------------------------------------------- a file in trouble

    [Fact]
    public void A_damaged_file_is_said_with_Start_over_and_its_box()
    {
        var library = Open(new CollectionLibrary { Lines = CollectionLibrary.Sample().Lines, Index = new LibraryData(), IndexStatus = LibraryStatus.Corrupt });

        Assert.Contains(CorruptText, string.Join(" ", Painted()));
        Assert.Null(Chips);
        Assert.Null(Screen.WidgetById(ChipLine.NewId));

        ClickOn(SettingsScreen.StartOverId);
        Assert.Equal(NameBoxKind.StartOver, Screen.OpenBoxState!.Kind);
        Assert.Equal(NameBox.CancelId, Screen.FocusId);
        Assert.Contains(NameBoxState.StartOverQuestion, string.Join(" ", Painted()));

        Screen.Key(Vk.Tab, 0);
        Screen.Key(Vk.Enter, 0);
        Assert.Equal(["start over"], library.Asked);

        library.IndexStatus = LibraryStatus.Loaded;
        Screen.LibraryReadingArrived();
        Screen.CollectionBoxAnswered(LibraryOutcome.Done);

        Assert.False(Screen.BoxOpen);
        Assert.Equal(SettingsScreen.ClipFilterId, Screen.FocusId);
    }

    [Theory]
    [InlineData(LibraryStatus.Unreadable, "Collections can't be read right now. Your clips are safe.")]
    [InlineData(LibraryStatus.TooNew, "A newer version wrote your collections. Update to change them.")]
    public void A_file_that_cannot_be_written_says_so_in_place_of_the_line(LibraryStatus status, string text)
    {
        var library = CollectionLibrary.Sample();
        library.IndexStatus = status;
        Open(library);

        Assert.Contains(text, Painted());
        Assert.Null(Chips);
        Assert.Null(Screen.WidgetById(ChipLine.NewId));
        Assert.Null(Screen.WidgetById(SettingsScreen.StartOverId));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Next_to_Start_over_the_sentence_of_a_damaged_file_is_never_cut(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var log = new DimCountingCanvas { Measurer = canvas };

        ChipLine.Notice(CorruptText, 200, 420).Paint(log, new UiState(0xD4F53C, null, null, null, -1, -1, 0, true, null));

        Assert.Equal(CorruptText, string.Join(" ", log.Texts));
        Assert.InRange(log.Texts.Count, 2, 2);
    }
}
