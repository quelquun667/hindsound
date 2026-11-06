using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The mouse on a page that scrolls, driven as the window would, with no window.</summary>
public sealed class SettingsMouseTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    // Scrolls so that this widget has only its top showing, above the foot, and returns a point on that part.
    private (float X, float Y) HalfUnderTheFoot(string id)
    {
        const float shown = 6;
        var box = WidgetBox(id);
        Screen.ScrollTo(box.Y + shown - SettingsScreen.ViewBottom);
        var y = box.Y - Screen.Scroll + shown / 2;
        Assert.InRange(y, SettingsScreen.BandHeight, SettingsScreen.ViewBottom - 1);
        return (box.MidX, y);
    }

    private Box WidgetBox(string id)
    {
        var previous = Screen.FocusId!;
        _rig.FocusOn(id);
        var box = Screen.Focus!.Bounds;
        _rig.FocusOn(previous);
        return box;
    }

    [Fact]
    public void The_first_click_on_a_button_half_under_the_foot_counts()
    {
        Screen.ShowSection(Section.General);
        var (x, y) = HalfUnderTheFoot("general.test");
        var before = Screen.Scroll;

        Screen.MouseDown(x, y);
        Assert.NotEqual(before, Screen.Scroll);
        Screen.MouseUp(x, y);

        Assert.Equal(1, _rig.Backend.Tests);
    }

    [Fact]
    public void A_click_on_a_half_hidden_swatch_picks_the_one_under_the_mouse()
    {
        Screen.ShowSection(Section.General);
        var swatches = WidgetBox("general.accent");
        Screen.ScrollTo(swatches.Y + 8 - SettingsScreen.BandHeight);
        Assert.True(Screen.Scroll > 0);
        var y = SettingsScreen.BandHeight + 3;

        // The second swatch, in the part that still shows under the band.
        Screen.MouseDown(swatches.X + 44 + 15, y);
        Screen.MouseUp(swatches.X + 44 + 15, y);

        Assert.Equal(SettingsEditor.AccentOrder[1], AccentColors.PresetOf(_rig.Settings.General.AccentColor));
    }

    [Fact]
    public void Letting_go_outside_the_widget_is_still_no_click()
    {
        Screen.ShowSection(Section.General);
        var (x, y) = HalfUnderTheFoot("general.test");

        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y - 100);

        Assert.Equal(0, _rig.Backend.Tests);
    }

    // ---------------------------------------------------------------- the scroll bar

    private (float X, float Y) DragTheThumb()
    {
        Screen.ShowSection(Section.General);
        Assert.True(Screen.MaxScroll > 0);
        var x = SettingsScreen.WindowWidth - 9;
        Screen.MouseDown(x, SettingsScreen.BandHeight + 12);
        Screen.MouseMove(x, SettingsScreen.BandHeight + 40);
        return (x, SettingsScreen.BandHeight + 40);
    }

    private static uint ThumbColor(SettingsScreen screen)
    {
        var canvas = new FakeCanvas();
        screen.Paint(canvas);
        return canvas.LastRoundFill;
    }

    [Fact]
    public void The_thumb_goes_dark_again_when_the_drag_ends()
    {
        var (x, y) = DragTheThumb();
        Assert.Equal(Theme.Muted, ThumbColor(Screen));
        var before = _rig.Host.Invalidated;

        Screen.MouseUp(x, y);

        Assert.True(_rig.Host.Invalidated > before);
        Assert.Equal(Theme.Control, ThumbColor(Screen));
    }

    [Fact]
    public void The_thumb_goes_dark_again_when_the_mouse_is_taken_away()
    {
        DragTheThumb();
        var before = _rig.Host.Invalidated;

        Screen.MouseCaptureLost();

        Assert.True(_rig.Host.Invalidated > before);
        Assert.Equal(Theme.Control, ThumbColor(Screen));
    }
}
