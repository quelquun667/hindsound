using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Play and Stop in the foot of the Library, the double click, the sentence about the next clip, and Can't play.</summary>
public sealed class ScreenLibraryPreviewTests : IDisposable
{
    private const float ListTop = 118, RowX = 300, StarX = 710;

    private readonly ScreenRig _rig = new();
    private readonly PlayingLibrary _library = new();

    public ScreenLibraryPreviewTests()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new NoNow());
        Screen.SetLibrary(_library);
        Screen.ShowSection(Section.Library);
    }

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

    private Button PlayButton => (Button)Screen.WidgetById(SettingsScreen.ClipPlayId)!;

    private ClipGroup Row(int i) => _library.Lines!.Lines[i];

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    private void DoubleClick(float x, float y)
    {
        Click(x, y);
        Screen.MouseDoubleClick(x, y);
        Screen.MouseUp(x, y);
    }

    private void ClickOn(string id)
    {
        var box = Screen.WidgetById(id)!.Bounds;
        Click(box.MidX, box.MidY);
    }

    private bool SaysSentence() => Painted().Any(text => text.StartsWith("Playback can end up", StringComparison.Ordinal));

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    private void ShowFirstCollection()
    {
        Assert.True(Screen.TabTo(SettingsScreen.ClipFilterId));
        _rig.Press(Vk.Down, Vk.Down);
        Assert.Equal("pets", LibraryFilter.NameOf(Screen.ClipChoice, _library.Index!));
    }

    [Fact]
    public void Play_sits_right_of_Show_in_folder_and_is_grey_with_nothing_chosen()
    {
        var show = Screen.WidgetById(SettingsScreen.ClipShowId)!.Bounds;
        var play = PlayButton.Bounds;
        Assert.False(PlayButton.Enabled);
        Assert.True(play.X > show.Right);
        Assert.Equal(show.Y, play.Y);
        Assert.True(play.Right <= SettingsScreen.WindowWidth - 24 + 0.01f);
        Assert.DoesNotContain(SettingsScreen.ClipPlayId, Screen.StopIds());
    }

    [Fact]
    public void Play_comes_last_in_the_order_of_Tab()
    {
        Click(RowX, RowY(1));
        var stops = Screen.StopIds();
        Assert.Equal([SettingsScreen.ClipCopyId, SettingsScreen.ClipShowId, SettingsScreen.ClipPlayId], stops.TakeLast(3));
        _rig.FocusOn(SettingsScreen.ClipPlayId);
        _rig.Press(Vk.Tab);
        Assert.Equal(stops[0], Screen.FocusId);
    }

    [Fact]
    public void Play_plays_the_chosen_clip_and_Stop_takes_its_place_and_its_width()
    {
        Click(RowX, RowY(1));
        var before = PlayButton.Bounds;
        Assert.True(PlayButton.Enabled);
        Assert.Equal(ButtonIcon.Play, PlayButton.Icon);
        ClickOn(SettingsScreen.ClipPlayId);
        Assert.Equal([Row(1)], _library.Played);

        Screen.PreviewChanged(new PreviewChange(PreviewState.Opening, Row(1)));
        Assert.Contains(SettingsScreen.StopLabel, Painted());
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(1)));
        Assert.Contains(SettingsScreen.StopLabel, Painted());
        Assert.Equal(before, PlayButton.Bounds);
        Assert.Equal(ButtonIcon.None, PlayButton.Icon);

        ClickOn(SettingsScreen.ClipPlayId);
        Assert.Equal(1, _library.Stops);
        Assert.Single(_library.Played);
    }

    [Fact]
    public void The_key_on_Play_then_on_Stop_plays_then_stops_and_keeps_the_focus()
    {
        Click(RowX, RowY(0));
        _rig.FocusOn(SettingsScreen.ClipPlayId);
        _rig.Press(Vk.Enter);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Assert.Equal(SettingsScreen.ClipPlayId, Screen.FocusId);
        _rig.Press(Vk.Space);
        Assert.Equal(1, _library.Stops);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Stopped, null));
        Assert.Equal(SettingsScreen.ClipPlayId, Screen.FocusId);
        Assert.Contains(SettingsScreen.PlayLabel, Painted());
    }

    [Fact]
    public void Another_clip_chosen_does_not_stop_the_sound_and_its_Play_replaces_it()
    {
        Click(RowX, RowY(0));
        ClickOn(SettingsScreen.ClipPlayId);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Click(RowX, RowY(2));
        Assert.Equal(0, _library.Stops);
        var painted = Painted();
        Assert.Contains(SettingsScreen.PlayLabel, painted);
        Assert.DoesNotContain(SettingsScreen.StopLabel, painted);
        ClickOn(SettingsScreen.ClipPlayId);
        Assert.Equal([Row(0), Row(2)], _library.Played);
        Assert.Equal(0, _library.Stops);
    }

    [Fact]
    public void With_nothing_chosen_any_more_the_clip_that_plays_can_still_be_stopped()
    {
        Click(RowX, RowY(0));
        ClickOn(SettingsScreen.ClipPlayId);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));

        // A reading without the clip that plays, as after a filter that hides it: nothing is chosen any more.
        _library.Lines = new ClipLines([.. _library.Lines!.Lines.Skip(1)], 0);
        Screen.LibraryReadingArrived();
        var painted = Painted();
        Assert.Contains(SettingsScreen.NothingSelected, painted);
        Assert.Contains(SettingsScreen.StopLabel, painted);
        Assert.True(PlayButton.Enabled);
        Assert.Contains(SettingsScreen.ClipPlayId, Screen.StopIds());

        ClickOn(SettingsScreen.ClipPlayId);
        Assert.Equal(1, _library.Stops);
        Assert.Single(_library.Played);

        Screen.PreviewChanged(new PreviewChange(PreviewState.Stopped, null));
        Assert.False(PlayButton.Enabled);
        Assert.Contains(SettingsScreen.PlayLabel, Painted());
    }

    [Fact]
    public void The_end_of_the_clip_gives_Play_back()
    {
        Click(RowX, RowY(0));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Ended, Row(0)));
        Assert.Contains(SettingsScreen.PlayLabel, Painted());
        Assert.False(Screen.PreviewPlaying);
    }

    [Fact]
    public void A_clip_that_fails_says_so_in_place_of_its_name_only_on_its_own_line_until_the_next_play()
    {
        Click(RowX, RowY(1));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Error, Row(1)));
        var painted = Painted();
        Assert.Contains(ClipPreview.CantPlayText, painted);
        Assert.DoesNotContain("clip0001.mp3", painted);
        Assert.True(PlayButton.Enabled);

        Click(RowX, RowY(2));
        Assert.DoesNotContain(ClipPreview.CantPlayText, Painted());
        Click(RowX, RowY(1));
        Assert.Contains(ClipPreview.CantPlayText, Painted());

        ClickOn(SettingsScreen.ClipPlayId);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Opening, Row(1)));
        Assert.DoesNotContain(ClipPreview.CantPlayText, Painted());
    }

    [Fact]
    public void A_double_click_on_a_row_chooses_it_and_plays_it()
    {
        DoubleClick(RowX, RowY(3));
        Assert.Equal(Row(3).Main.Path, Assert.Single(_library.Played).Main.Path);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void A_double_click_on_the_star_plays_nothing_and_only_turns_the_star()
    {
        // The row is chosen first: a press on the star leaves the choice as it is, so the chosen row is the one under the mouse.
        Click(RowX, RowY(0));
        DoubleClick(StarX, RowY(0));
        Assert.Empty(_library.Played);
        Assert.Equal(2, _library.StarsAsked);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void A_double_click_below_the_rows_or_on_another_page_plays_nothing()
    {
        _library.Lines = new ClipLines([.. _library.Lines!.Lines.Take(2)], 0);
        Screen.LibraryReadingArrived();
        DoubleClick(RowX, RowY(4));
        Screen.ShowSection(Section.Now);
        DoubleClick(RowX, RowY(0));
        Assert.Empty(_library.Played);
    }

    [Fact]
    public void A_double_click_behind_an_open_box_plays_nothing()
    {
        Click(RowX, RowY(1));
        ShowFirstCollection();
        ClickOn(CollectionActions.DeleteId);
        Assert.True(Screen.BoxOpen);
        Screen.MouseDoubleClick(RowX, RowY(0));
        Screen.MouseUp(RowX, RowY(0));
        Assert.Empty(_library.Played);
    }

    [Fact]
    public void The_sentence_shows_only_while_something_plays_where_the_capture_can_hear_it()
    {
        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false });
        Click(RowX, RowY(1));
        Assert.False(SaysSentence());

        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(1)));
        Assert.True(SaysSentence());

        // Another row chosen: the sound goes on, and so does the sentence.
        Click(RowX, RowY(2));
        Assert.True(SaysSentence());

        Screen.PreviewChanged(new PreviewChange(PreviewState.Stopped, null));
        Assert.False(SaysSentence());
    }

    [Fact]
    public void Where_the_capture_leaves_this_app_out_the_sentence_is_never_said()
    {
        Click(RowX, RowY(1));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(1)));
        Assert.False(SaysSentence());

        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false, SourceApp = "Discord.exe" });
        Assert.False(SaysSentence());

        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false });
        Assert.True(SaysSentence());
    }

    [Fact]
    public void While_the_sentence_shows_it_takes_the_place_of_Rename_and_Delete_which_come_back_at_the_end()
    {
        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false });
        ShowFirstCollection();
        Click(RowX, RowY(0));
        Assert.Contains(CollectionActions.RenameId, Screen.StopIds());
        Assert.Contains(CollectionActions.DeleteId, Screen.StopIds());

        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(1)));
        Assert.True(SaysSentence());
        Assert.Null(Screen.WidgetById(CollectionActions.RenameId));
        Assert.Null(Screen.WidgetById(CollectionActions.DeleteId));
        var stops = Screen.StopIds().ToList();
        Assert.Equal(SettingsScreen.ClipFilterId, stops[stops.IndexOf(SettingsScreen.ClipListId) - 1]);

        Screen.PreviewChanged(new PreviewChange(PreviewState.Ended, Row(1)));
        Assert.False(SaysSentence());
        Assert.NotNull(Screen.WidgetById(CollectionActions.RenameId));
        Assert.NotNull(Screen.WidgetById(CollectionActions.DeleteId));
    }

    [Fact]
    public void Leaving_the_page_does_not_stop_the_sound_and_coming_back_shows_Stop()
    {
        Click(RowX, RowY(0));
        ClickOn(SettingsScreen.ClipPlayId);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Screen.ShowSection(Section.Now);
        Assert.Null(Screen.WidgetById(SettingsScreen.ClipPlayId));
        Screen.ShowSection(Section.Library);
        Assert.Equal(0, _library.Stops);
        Assert.Contains(SettingsScreen.StopLabel, Painted());
    }

    [Fact]
    public void A_new_panel_forgets_what_the_last_one_played()
    {
        Click(RowX, RowY(0));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Screen.SetLibrary(_library);
        Assert.False(Screen.PreviewPlaying);
    }

    [Fact]
    public void A_clip_found_gone_greys_Play_like_Copy()
    {
        Click(RowX, RowY(0));
        Screen.ClipGestureEnded(Row(0).Main.Path, ShellOutcome.Gone);
        Assert.False(PlayButton.Enabled);
        DoubleClick(RowX, RowY(0));
        Assert.Empty(_library.Played);
    }

    [Fact]
    public void Stop_stays_live_when_the_playing_clip_is_found_gone()
    {
        Click(RowX, RowY(0));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Screen.ClipGestureEnded(Row(0).Main.Path, ShellOutcome.Gone);
        Assert.Contains(SettingsScreen.StopLabel, Painted());
        Assert.True(PlayButton.Enabled);
        Assert.False(((Button)Screen.WidgetById(SettingsScreen.ClipCopyId)!).Enabled);
        ClickOn(SettingsScreen.ClipPlayId);
        Assert.Equal(1, _library.Stops);
    }

    [Fact]
    public void A_double_click_behind_an_open_menu_plays_nothing()
    {
        Click(RowX, RowY(1));
        ClickOn(SettingsScreen.ClipFilterId);
        Assert.True(Screen.PopupOpen);
        Screen.MouseDoubleClick(RowX, RowY(1));
        Screen.MouseUp(RowX, RowY(1));
        Assert.Empty(_library.Played);
    }

    private sealed class NoNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    /// <summary>Six clips in one collection out of three, which says what it was asked to play.</summary>
    private sealed class PlayingLibrary : ILibraryPanel
    {
        public PlayingLibrary()
        {
            var rows = Enumerable.Range(0, 6).Select(i => FakeLibrary.Clip($"clip{i:0000}.mp3")).ToList();
            Lines = new ClipLines(rows, 0);
        }

        public ClipLines? Lines { get; set; }

        public List<ClipGroup> Played { get; } = [];

        public int Stops { get; private set; }

        public int StarsAsked { get; private set; }

        public LibraryData? Index { get; } = new()
        {
            Collections = [new() { Id = "c1", Name = "pets" }, new() { Id = "c2", Name = "Sam" }],
        };

        public LibraryStatus IndexStatus => LibraryStatus.Loaded;

        public ClipLabels LabelsOf(ClipGroup line) => new(false, ["c1"]);

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

        public void SetFavorite(ClipGroup line, bool favorite) => StarsAsked++;

        public void Play(ClipGroup line) => Played.Add(line);

        public void StopPlaying() => Stops++;
    }
}
