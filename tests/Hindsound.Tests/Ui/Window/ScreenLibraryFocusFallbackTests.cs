using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Where the focus goes when the button it was on is switched off: back to the list, so the next arrow still picks a clip.</summary>
public sealed class ScreenLibraryFocusFallbackTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private FakeLibrary OpenWith(int count)
    {
        var library = new FakeLibrary().With(count);
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        return library;
    }

    [Theory]
    [InlineData(SettingsScreen.ClipCopyId)]
    [InlineData(SettingsScreen.ClipShowId)]
    public void A_clip_gone_while_a_foot_button_had_the_focus_leaves_the_focus_on_the_list(string button)
    {
        OpenWith(5);
        var path = FakeLibrary.PathOf(1);
        Screen.SelectClip(path);
        Assert.True(Screen.TabTo(button));

        Screen.ClipGestureEnded(path, ShellOutcome.Gone);

        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void The_next_arrow_after_it_still_picks_a_clip_and_stays_on_the_page()
    {
        OpenWith(5);
        var path = FakeLibrary.PathOf(1);
        Screen.SelectClip(path);
        Screen.TabTo(SettingsScreen.ClipCopyId);
        Screen.ClipGestureEnded(path, ShellOutcome.Gone);

        Screen.Key(Vk.Down, 0);

        Assert.Equal(Section.Library, Screen.Section);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
    }

    [Fact]
    public void With_no_clip_left_to_pick_the_focus_goes_to_the_side_bar()
    {
        var library = OpenWith(3);
        Screen.SelectClip(FakeLibrary.PathOf(1));
        Screen.TabTo(SettingsScreen.ClipCopyId);

        library.Lines = new ClipLines([], 0);
        Screen.LibraryReadingArrived();

        Assert.Equal("nav", Screen.FocusId);
    }
}
