using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>What Play and Stop do when the clip that plays, or the button that has the focus, is found gone.</summary>
public sealed class ScreenLibraryPreviewGapTests : IDisposable
{
    private const float ListTop = 118, RowX = 300;

    private sealed class NoNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();
    private readonly PlayingLibraryFake _library = new();

    public ScreenLibraryPreviewGapTests()
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

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    [Fact]
    public void A_playing_clip_found_gone_keeps_Stop_active_and_Stop_stops_it()
    {
        Click(RowX, RowY(0));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Screen.ClipGestureEnded(Row(0).Main.Path, ShellOutcome.Gone);

        Assert.True(PlayButton.Enabled);
        Assert.Contains(SettingsScreen.StopLabel, Painted());
        var box = PlayButton.Bounds;
        Click(box.MidX, box.MidY);
        Assert.Equal(1, _library.Stops);
    }

    [Fact]
    public void A_clip_gone_while_Play_had_the_focus_leaves_the_focus_on_the_list()
    {
        Click(RowX, RowY(0));
        Assert.True(Screen.TabTo(SettingsScreen.ClipPlayId));

        Screen.ClipGestureEnded(Row(0).Main.Path, ShellOutcome.Gone);

        Assert.False(PlayButton.Enabled);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void The_button_of_the_collection_keeps_the_focus_when_the_sentence_comes()
    {
        Assert.True(Screen.TabTo(SettingsScreen.ClipFilterId));
        _rig.Press(Vk.Down, Vk.Down);
        Click(RowX, RowY(0));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, Row(0)));
        Assert.True(Screen.TabTo(CollectionActions.MoreId));

        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false });

        Assert.NotNull(Screen.WidgetById(CollectionActions.MoreId));
        Assert.Equal(CollectionActions.MoreId, Screen.FocusId);
    }
}
