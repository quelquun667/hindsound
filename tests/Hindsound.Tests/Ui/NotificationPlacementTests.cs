using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public class NotificationPlacementTests
{
    private static Native.RECT Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static NotificationPlacement On(Native.RECT area, bool leftEdge = false) => new(area, 96, false, leftEdge);

    private static readonly Native.RECT Monitor = Rect(0, 0, 1920, 1080);

    [Fact]
    public void A_taskbar_on_the_right_leaves_the_tab_beside_it()
    {
        var screen = new ScreenInfo(Monitor, Rect(0, 0, 1860, 1080), 96);

        var placement = On(NotificationPlacement.AreaOn(screen, foregroundCoversScreen: false));

        Assert.Equal(1860 - 360, placement.LeftWhenShowing(360));
    }

    [Fact]
    public void A_taskbar_on_the_left_leaves_the_tab_beside_it()
    {
        var screen = new ScreenInfo(Monitor, Rect(60, 0, 1920, 1080), 96);

        var placement = On(NotificationPlacement.AreaOn(screen, foregroundCoversScreen: false), leftEdge: true);

        Assert.Equal(60, placement.LeftWhenShowing(360));
    }

    [Fact]
    public void A_taskbar_on_top_moves_the_tab_down_with_the_area()
    {
        var screen = new ScreenInfo(Monitor, Rect(0, 100, 1920, 1080), 96);

        var placement = On(NotificationPlacement.AreaOn(screen, foregroundCoversScreen: false));

        Assert.Equal(100 + 294, placement.Top);
    }

    [Fact]
    public void Over_a_full_screen_window_the_tab_sticks_to_the_screen_itself()
    {
        var screen = new ScreenInfo(Monitor, Rect(0, 0, 1860, 1040), 96);

        Assert.Equal(Monitor, NotificationPlacement.AreaOn(screen, foregroundCoversScreen: true));
    }

    [Fact]
    public void A_screen_left_of_the_main_one_keeps_its_negative_coordinates()
    {
        var work = Rect(-2560, -200, -60, 1240);
        var screen = new ScreenInfo(Rect(-2560, -200, 0, 1240), work, 144);

        var placement = On(NotificationPlacement.AreaOn(screen, foregroundCoversScreen: false));

        Assert.Equal(-60 - 540, placement.LeftWhenShowing(540));
        Assert.Equal(-200 + 432, placement.Top);
        Assert.Equal(-2560, On(work, leftEdge: true).LeftWhenShowing(540));
    }

    [Fact]
    public void The_tab_comes_out_of_its_edge()
    {
        var right = On(Monitor);
        var left = On(Monitor, leftEdge: true);

        Assert.Equal(1920 - 1, right.LeftWhenShowing(1));
        Assert.Equal(1920 - 360, right.LeftWhenShowing(360));
        Assert.Equal(0, left.LeftWhenShowing(1));
        Assert.Equal(0, left.LeftWhenShowing(360));
    }
}
