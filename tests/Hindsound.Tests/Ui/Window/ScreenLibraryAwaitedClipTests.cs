using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A clip waited for, because the reading that lists it has not come, gives way to any press of the user on a row.</summary>
public sealed class ScreenLibraryAwaitedClipTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const float ListTop = 86, RowX = 300;

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

    private FakeLibrary OpenWith(int count)
    {
        var library = new FakeLibrary().With(count);
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

    /// <summary>The path Copy acts on: the clip chosen.</summary>
    private string ChosenPath(FakeLibrary library)
    {
        var copy = Screen.WidgetById(SettingsScreen.ClipCopyId)!.Bounds;
        Click(copy.MidX, copy.MidY);
        return library.Copied[^1].Main.Path;
    }

    [Fact]
    public void A_press_on_the_row_already_chosen_keeps_it_when_the_awaited_clip_arrives()
    {
        var library = OpenWith(3);
        Click(RowX, RowY(1));
        Screen.SelectClip(FakeLibrary.PathOf(7));

        Click(RowX, RowY(1));
        library.With(10);
        Screen.LibraryReadingArrived();

        Assert.Equal(FakeLibrary.PathOf(1), ChosenPath(library));
    }

    [Fact]
    public void Without_a_press_the_awaited_clip_is_chosen_when_it_arrives()
    {
        var library = OpenWith(3);
        Click(RowX, RowY(1));
        Screen.SelectClip(FakeLibrary.PathOf(7));

        library.With(10);
        Screen.LibraryReadingArrived();

        Assert.Equal(FakeLibrary.PathOf(7), ChosenPath(library));
    }

    [Fact]
    public void A_press_below_the_last_row_does_not_cancel_the_wait()
    {
        var library = OpenWith(3);
        Screen.SelectClip(FakeLibrary.PathOf(7));

        Click(RowX, RowY(5));
        library.With(10);
        Screen.LibraryReadingArrived();

        Assert.Equal(FakeLibrary.PathOf(7), ChosenPath(library));
    }
}
