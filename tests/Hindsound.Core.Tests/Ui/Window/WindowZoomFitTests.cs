using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class WindowZoomFitTests
{
    // The window as the system sizes it: its design size at the scale of the screen and the zoom, and a frame around it.
    private static Func<int, (int W, int H)> Outer(int dpi) =>
        zoom =>
        {
            var scale = dpi / 96.0 * zoom / 100.0;
            return ((int)Math.Round(760 * scale) + 16, (int)Math.Round(488 * scale) + 39);
        };

    [Theory]
    [InlineData(100, 100)]
    [InlineData(120, 120)]
    [InlineData(140, 140)]
    public void A_roomy_screen_gives_what_was_asked_and_never_more(int wanted, int expected)
    {
        Assert.Equal(expected, WindowZoomFit.Choose(wanted, Outer(96), 1920, 1040));
    }

    [Fact]
    public void A_zoom_that_fits_on_a_full_hd_screen_at_96_dpi_is_kept()
    {
        Assert.Equal(120, WindowZoomFit.Choose(120, Outer(96), 1920, 1080));
    }

    [Fact]
    public void A_small_screen_at_150_percent_gets_the_design_size()
    {
        Assert.Equal(100, WindowZoomFit.Choose(120, Outer(144), 1366, 728));
        Assert.Equal(100, WindowZoomFit.Choose(140, Outer(144), 1366, 728));
    }

    [Fact]
    public void When_only_the_middle_zoom_fits_the_largest_one_that_fits_is_taken()
    {
        // 1366 x 768 at 96 dpi: 120% is 928 x 625, 140% is 1080 x 722 plus the frame, which is too high for 700.
        Assert.Equal(120, WindowZoomFit.Choose(140, Outer(96), 1366, 700));
    }

    [Fact]
    public void A_wider_zoom_than_wanted_is_not_taken_though_it_fits()
    {
        Assert.Equal(100, WindowZoomFit.Choose(100, Outer(96), 3840, 2160));
    }

    [Fact]
    public void A_screen_smaller_than_the_window_at_100_percent_still_gets_100()
    {
        Assert.Equal(100, WindowZoomFit.Choose(140, Outer(96), 640, 400));
        Assert.Equal(100, WindowZoomFit.Choose(120, Outer(96), 0, 0));
    }

    [Fact]
    public void The_choice_is_always_one_the_settings_offer()
    {
        for (var width = 600; width <= 2200; width += 97)
        {
            Assert.Contains(WindowZoomFit.Choose(140, Outer(96), width, width * 9 / 16), GeneralSettings.WindowZooms);
        }
    }
}
