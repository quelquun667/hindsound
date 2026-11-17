using System.Runtime.InteropServices;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The size of the window follows its zoom: the real window, never shown, in the area it makes up off screen (1920 x 1080 at 96 dpi).</summary>
public sealed class WindowZoomTests : IDisposable
{
    private readonly ScreenRig _rig = new();
    private SettingsWindow? _window;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    public void Dispose()
    {
        _window?.Dispose();
        _rig.Dispose();
    }

    private SettingsWindow Open(int zoom)
    {
        _rig.Settings.General.WindowZoom = zoom;
        _window = new SettingsWindow(_rig.Settings, _rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });
        return _window;
    }

    [Theory]
    [InlineData(100, "760x488, bitmap 760x488")]
    [InlineData(120, "912x586, bitmap 912x586")]
    [InlineData(140, "1064x683, bitmap 1064x683")]
    public void The_client_and_the_bitmap_are_the_design_size_at_the_zoom_of_the_settings(int zoom, string expected)
    {
        Assert.Equal(expected, Open(zoom).ClientSizeText());
    }

    [Fact]
    public void A_new_zoom_resizes_the_window_around_its_centre_and_the_bitmap_with_it()
    {
        var window = Open(120);
        GetWindowRect(window.Hwnd, out var before);

        window.ZoomChanged(100);

        Assert.Equal("760x488, bitmap 760x488", window.ClientSizeText());
        GetWindowRect(window.Hwnd, out var after);
        Assert.InRange((after.Left + after.Right) - (before.Left + before.Right), -2, 2);
        Assert.InRange((after.Top + after.Bottom) - (before.Top + before.Bottom), -2, 2);

        window.ZoomChanged(140);

        Assert.Equal("1064x683, bitmap 1064x683", window.ClientSizeText());
    }

    [Fact]
    public void The_same_zoom_again_changes_nothing()
    {
        var window = Open(120);
        GetWindowRect(window.Hwnd, out var before);

        window.ZoomChanged(120);

        GetWindowRect(window.Hwnd, out var after);
        Assert.Equal((before.Left, before.Top, before.Right, before.Bottom), (after.Left, after.Top, after.Right, after.Bottom));
    }

    [Fact]
    public void A_screen_of_another_dpi_is_chosen_again_for_the_zoom_of_the_settings()
    {
        var window = Open(120);

        window.ProbeDpiChange(144);

        // 150% of the screen, then 120% of the window: 1368 x 878.
        // The probe sends the message without moving the window to a real screen, so the frame keeps its own DPI: the bitmap tells.
        Assert.EndsWith("bitmap 1368x878", window.ClientSizeText());
    }

    [Fact]
    public void A_zoom_the_screen_cannot_hold_falls_to_the_largest_that_fits()
    {
        var window = Open(140);

        // At 200% of the screen, the design size is 1520 x 976 and 120% would be 1824 x 1171: too high for 1080.
        window.ProbeDpiChange(192);

        Assert.EndsWith("bitmap 1520x976", window.ClientSizeText());
    }

    [Fact]
    public void A_work_area_too_small_for_even_100_percent_puts_the_window_at_its_top_left()
    {
        // 600 x 400 cannot hold the 760 x 488 window: centred, its title bar would be above the top of the area.
        _rig.Settings.General.WindowZoom = 100;
        var work = new ScreenRect(100, 80, 700, 480);
        var screen = new ScreenInfo(work, work, GetDpiForSystem());
        _window = new SettingsWindow(_rig.Settings, _rig.Backend, () => 0f, firstRun: false, offscreen: false, screen, () => { });

        GetWindowRect(_window.Hwnd, out var rect);

        Assert.Equal((100, 80), (rect.Left, rect.Top));
    }

    [Fact]
    public void A_click_lands_where_the_window_is_drawn_at_the_zoom()
    {
        var window = Open(120);
        var screen = window.Screen;
        screen.ShowSection(Section.General);
        for (var i = 0; i < 40 && screen.FocusId != "general.size"; i++)
        {
            screen.Key(Vk.Tab, 0);
        }

        Assert.Equal("general.size", screen.FocusId);
        var box = screen.Focus!.Bounds;

        // The right third of the control is 140%. The pixel is the design point times the scale of the window, 1.2.
        var x = (int)((box.X + box.W * 5 / 6) * 1.2f);
        var y = (int)((box.MidY - screen.Scroll) * 1.2f);
        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_LBUTTONDOWN, 1, (y << 16) | x);
        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_LBUTTONUP, 0, (y << 16) | x);

        Assert.Equal(140, _rig.Settings.General.WindowZoom);
    }
}
