using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Where the user was in a long list of clips stays, whatever happens to the window in between.</summary>
public sealed class ScreenLibraryKeepsRowsTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return [.. _rig.Canvas.Drawn];
    }

    private void Scrolled()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(new FakeLibrary().With(2000));
        Screen.ShowSection(Section.Library);
        Assert.True(Screen.TabTo(SettingsScreen.ClipListId));
        for (var i = 0; i < 10; i++)
        {
            Screen.Key(Vk.Down, 0);
        }

        Screen.Wheel(-5);
    }

    [Fact]
    public void Another_page_a_minimized_window_a_new_reading_and_a_new_canvas_keep_the_rows_and_the_choice()
    {
        Scrolled();
        var before = Painted();

        // The chosen clip is named at the bottom. With nothing chosen the first arrow picks the first row, so ten of them pick clip0009.
        Assert.True(before.Contains("clip0009.mp3"), $"focus {Screen.FocusId}, drawn: {string.Join(" | ", before)}");
        Assert.DoesNotContain("clip0000", before);

        Screen.ShowSection(Section.Now);
        Screen.ShowSection(Section.Library);
        Assert.Equal(before, Painted());

        Screen.ShowSection(Section.General);
        Screen.ShowSection(Section.Library);
        Assert.Equal(before, Painted());

        Screen.WindowMinimized(true);
        Screen.LibraryReadingArrived();
        Screen.WindowMinimized(false);
        Assert.Equal(before, Painted());

        Screen.SetCanvas(_rig.Canvas);
        Assert.Equal(before, Painted());
    }
}
