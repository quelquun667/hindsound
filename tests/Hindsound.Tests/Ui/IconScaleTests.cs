using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui;

public class IconScaleTests
{
    private const uint Accent = 0xD4F53C;

    [Theory]
    [InlineData(Native.WM_DPICHANGED, 0u)]
    [InlineData(Native.WM_DISPLAYCHANGE, 32u)]
    [InlineData(Native.WM_SETTINGCHANGE, Native.SPI_SETLOGICALDPIOVERRIDE)]
    [InlineData(Native.WM_SETTINGCHANGE, Native.SPI_SETNONCLIENTMETRICS)]
    [InlineData(Native.WM_SETTINGCHANGE, Native.SPI_SETICONMETRICS)]
    public void These_messages_can_mean_a_new_scale(uint message, uint wParam)
    {
        Assert.True(UiLoop.MeansScaleChange(message, wParam));
    }

    [Theory]
    [InlineData(Native.WM_SETTINGCHANGE, 0u)] // the environment, or a theme
    [InlineData(Native.WM_SETTINGCHANGE, 0x0014u)] // the wallpaper
    [InlineData(Native.WM_TIMER, 0u)]
    public void These_do_not(uint message, uint wParam)
    {
        Assert.False(UiLoop.MeansScaleChange(message, wParam));
    }

    [Theory]
    [InlineData(16, 96, false)]
    [InlineData(16, 144, true)]
    [InlineData(24, 144, false)]
    [InlineData(24, 192, true)]
    public void An_icon_is_stale_at_a_scale_it_was_not_drawn_for(int iconSize, uint dpi, bool stale)
    {
        Assert.Equal(stale, TrayIcon.IsStale(iconSize, dpi));
    }

    [Theory]
    [InlineData(96, 16, 32)]
    [InlineData(144, 24, 48)]
    [InlineData(192, 32, 64)]
    public void The_sizes_follow_the_dpi_asked_for_and_not_the_one_the_process_started_with(uint dpi, int small, int big)
    {
        Assert.Equal(small, TrayIcon.SmallSizeAt(dpi));
        Assert.Equal(big, TrayIcon.BigSizeAt(dpi));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(96)]
    public void An_icon_is_drawn_at_the_size_asked_for(int size)
    {
        var icon = TrayIcon.CreateIcon(TrayState.Active, Accent, size);
        try
        {
            Assert.Equal(size, TrayIcon.WidthOf(icon));
        }
        finally
        {
            Native.DestroyIcon(icon);
        }
    }

    [Fact]
    public void The_title_bar_has_two_icons_each_at_its_own_size_and_they_follow_the_window()
    {
        // The window is created and never shown, so nothing appears and nothing takes the focus.
        using var rig = new ScreenRig();
        var screen = new ScreenInfo(default, default, 96);
        using var window = new SettingsWindow(rig.Settings, rig.Backend, () => 0, firstRun: false, offscreen: true, screen, () => { });

        window.ProbeDpiChange(96);
        window.SetIcon(Accent);
        Assert.Equal((16, 32), window.IconWidths());

        window.ProbeDpiChange(144);
        Assert.Equal((24, 48), window.IconWidths());
    }
}
