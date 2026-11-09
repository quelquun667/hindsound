using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>What the box of a collection and the list of +N hold back or give up while they are open over the Library page.</summary>
public sealed class ScreenOverlayGuardsTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

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

    private void ChooseCollection(string id) => ((Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!).PickValue(LibraryChoice.Of(id).Value);

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return [.. _rig.Canvas.Drawn];
    }

    [Fact]
    public void While_the_box_waits_for_its_answer_typing_changes_nothing_and_the_name_sent_stays_shown()
    {
        Open(CollectionLibrary.Sample());
        ClickOn(ChipLine.NewId);
        Screen.EditChanged("dogs");
        Screen.Key(Vk.Enter, 0);
        Assert.True(Screen.OpenBoxState!.Waiting);
        var resets = _rig.Host.EditResets;

        Screen.EditChanged("cats");

        // The name that left is the one the box shows, and the native box is told to take it back.
        Assert.Equal("dogs", Screen.OpenBoxState.Field.Draft);
        Assert.True(_rig.Host.EditResets > resets);
        Assert.Equal("dogs", _rig.Host.EditText);
    }

    [Fact]
    public void With_the_list_of_the_plus_open_the_keys_it_does_not_use_move_nothing_behind_it()
    {
        Open(CollectionLibrary.Sample(8));
        var chips = (CollectionChips)Screen.WidgetById(ChipLine.ChipsId)!;
        var plus = chips.Fit.Plus!.Value;
        Click(plus.X + plus.W / 2, plus.MidY);
        Assert.NotNull(Screen.MoreList);
        var cursor = chips.CursorIndex;

        Assert.False(Screen.Key(Vk.Left, 0));
        Assert.False(Screen.Key(Vk.Right, 0));

        Assert.NotNull(Screen.MoreList);
        Assert.Equal(cursor, chips.CursorIndex);
        Assert.Equal(ChipLine.ChipsId, Screen.FocusId);
    }

    /// <summary>Ten clips all in the collection pets, so that the list scrolls under a Delete box, whose focus is on a button.</summary>
    private CollectionLibrary OpenOnAFullCollection()
    {
        var library = CollectionLibrary.Sample();
        for (var row = 0; row < 10; row++)
        {
            library.Labels[CollectionLibrary.PathOf(row)] = new ClipLabels(false, ["c0"]);
        }

        Open(library, chosen: null);
        ChooseCollection("c0");
        return library;
    }

    [Fact]
    public void Behind_a_Delete_box_the_page_keys_move_no_row()
    {
        OpenOnAFullCollection();
        var before = Painted();
        Assert.Contains("clip0000", before);
        Assert.DoesNotContain("clip0009", before);

        ClickOn(CollectionActions.DeleteId);
        Assert.Equal(NameBox.CancelId, Screen.FocusId);
        var withTheBox = Painted();
        Screen.Key(Vk.PageDown, 0);
        Screen.Key(Vk.PageUp, 0);
        Screen.Key(Vk.PageDown, 0);

        Assert.Equal(withTheBox, Painted());
        Screen.Key(Vk.Escape, 0);
        Assert.False(Screen.BoxOpen);

        // With the box gone, the same key moves the rows: the test above holds back something real.
        Screen.Key(Vk.PageDown, 0);
        Assert.DoesNotContain("clip0000", Painted());
    }

    [Fact]
    public void Behind_a_Delete_box_the_wheel_moves_no_row()
    {
        OpenOnAFullCollection();
        ClickOn(CollectionActions.DeleteId);
        Screen.Paint(_rig.Canvas);
        var before = Painted();
        Assert.Contains("clip0000", before);

        Screen.Wheel(-3);

        Assert.Contains("clip0000", Painted());
        Screen.Key(Vk.Escape, 0);
        Screen.Wheel(-3);
        Assert.DoesNotContain("clip0000", Painted());
    }

    [Fact]
    public void Losing_the_keyboard_closes_the_list_of_the_plus()
    {
        Open(CollectionLibrary.Sample(8));
        var plus = ((CollectionChips)Screen.WidgetById(ChipLine.ChipsId)!).Fit.Plus!.Value;
        Click(plus.X + plus.W / 2, plus.MidY);
        Assert.True(Screen.PopupOpen);

        Screen.Deactivated();

        Assert.Null(Screen.MoreList);
        Assert.False(Screen.PopupOpen);
    }

    [Fact]
    public void When_the_control_that_opened_a_box_is_gone_Esc_gives_the_focus_to_the_menu()
    {
        var library = Open(CollectionLibrary.Sample(), chosen: null);
        ChooseCollection("c1");
        ClickOn(CollectionActions.RenameId);
        Assert.True(Screen.BoxOpen);

        // A clip outside Sam is chosen from elsewhere (a notification): the menu goes back to every clip, and Rename with it.
        Screen.SelectClip(CollectionLibrary.PathOf(3));
        Assert.Null(Screen.WidgetById(CollectionActions.RenameId));
        Screen.Key(Vk.Escape, 0);

        Assert.False(Screen.BoxOpen);
        Assert.Equal(SettingsScreen.ClipFilterId, Screen.FocusId);
        Assert.Empty(library.Asked);
    }

    private const string NotWrittenText ="Couldn't save that. Nothing changed.";

    [Fact]
    public void An_answer_that_comes_after_the_page_was_left_is_still_there_when_the_page_comes_back_with_its_reading()
    {
        var library = Open(CollectionLibrary.Sample());
        library.OnReadAgain = Screen.LibraryReadingArrived;
        ClickOn(ChipLine.NewId);
        Screen.EditChanged("dogs");
        Screen.Key(Vk.Enter, 0);

        Screen.ShowSection(Section.Now);
        Screen.CollectionBoxAnswered(LibraryOutcome.NotWritten);
        Screen.LibraryReadingArrived();
        Screen.ShowSection(Section.Library);

        // The reading that the return asked for is not news: the sentence stays until the one after it.
        Assert.Contains(NotWrittenText, Painted());

        Screen.LibraryReadingArrived();
        Assert.DoesNotContain(NotWrittenText, Painted());
    }

    private const uint StarOutline = 0x8E95A3;

    private int EmptyStars()
    {
        _rig.Canvas.Strokes.Clear();
        Painted();
        return _rig.Canvas.Strokes.Count(s => s.Rgb == StarOutline) / CanvasShapes.StarCorners;
    }

    [Theory]
    [InlineData(LibraryStatus.Unreadable)]
    [InlineData(LibraryStatus.Corrupt)]
    public void A_file_that_cannot_be_read_draws_no_star_rather_than_stars_that_say_not_a_favorite(LibraryStatus status)
    {
        var library = CollectionLibrary.Sample();
        library.Labels[CollectionLibrary.PathOf(4)] = new ClipLabels(true, []);
        Open(library, chosen: null);
        Assert.True(EmptyStars() > 0);

        library.Index = new LibraryData();
        library.IndexStatus = status;
        Screen.LibraryReadingArrived();

        Assert.Equal(0, EmptyStars());
    }

    [Theory]
    [InlineData(LibraryStatus.Unreadable)]
    [InlineData(LibraryStatus.Corrupt)]
    public void A_file_that_cannot_be_read_keeps_the_choice_of_the_menu_and_does_not_say_there_is_nothing_in_it(LibraryStatus status)
    {
        var library = CollectionLibrary.Sample();
        Open(library, chosen: null);
        ((Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!).PickValue(LibraryChoice.Favorites.Value);
        Assert.Contains("No favorite clips", Painted());

        library.Index = new LibraryData();
        library.IndexStatus = status;
        Screen.LibraryReadingArrived();

        Assert.Equal(LibraryChoice.Favorites, Screen.ClipChoice);
        Assert.DoesNotContain("No favorite clips", Painted());

        // Once the file reads again, the same choice is still there to show what it holds.
        library.Index = CollectionLibrary.Sample().Index;
        library.IndexStatus = LibraryStatus.Loaded;
        Screen.LibraryReadingArrived();
        Assert.Equal(LibraryChoice.Favorites, Screen.ClipChoice);
        Assert.Contains("No favorite clips", Painted());
    }

    [Fact]
    public void A_collection_chosen_stays_chosen_and_named_in_the_menu_while_the_file_cannot_be_read()
    {
        var library = CollectionLibrary.Sample();
        Open(library, chosen: null);
        ChooseCollection("c1");

        library.Index = new LibraryData();
        library.IndexStatus = LibraryStatus.Unreadable;
        Screen.LibraryReadingArrived();

        Assert.Equal(LibraryChoice.Of("c1"), Screen.ClipChoice);
        var menu = (Dropdown)Screen.WidgetById(SettingsScreen.ClipFilterId)!;
        Assert.Equal("Sam", menu.Items[menu.SelectedIndex].Label);
        Assert.DoesNotContain("No clips in this collection", Painted());
    }
}
