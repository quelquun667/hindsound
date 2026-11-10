using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A preview that is still opening already counts as a playback: Stop is there and the sentence about the next clip is said.</summary>
public sealed class ScreenLibraryPreviewOpeningTests : IDisposable
{
    private const float ListTop = 118, RowX = 300;

    private sealed class NoNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();
    private readonly PlayingLibraryFake _library = new();

    public ScreenLibraryPreviewOpeningTests()
    {
        Screen.UpdateStatus(ScreenRig.Capturing() with { OwnSoundsExcluded = false });
        Screen.SetNow(new NoNow());
        Screen.SetLibrary(_library);
        Screen.ShowSection(Section.Library);
    }

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

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
    public void While_a_preview_is_opening_the_sentence_is_said_and_takes_the_place_of_Rename_and_Delete()
    {
        Assert.True(Screen.TabTo(SettingsScreen.ClipFilterId));
        _rig.Press(Vk.Down, Vk.Down);
        Click(RowX, RowY(0));
        Assert.NotNull(Screen.WidgetById(CollectionActions.RenameId));

        Screen.PreviewChanged(new PreviewChange(PreviewState.Opening, Row(1)));

        Assert.True(Screen.SaysPreviewCanEndUpInClip);
        Assert.Contains(Painted(), text => text.StartsWith("Playback can end up", StringComparison.Ordinal));
        Assert.Null(Screen.WidgetById(CollectionActions.RenameId));
        Assert.Null(Screen.WidgetById(CollectionActions.DeleteId));
    }

    [Fact]
    public void While_a_preview_is_opening_the_button_says_Stop()
    {
        Click(RowX, RowY(1));
        Screen.PreviewChanged(new PreviewChange(PreviewState.Opening, Row(1)));

        Assert.Contains(SettingsScreen.StopLabel, Painted());
    }
}
