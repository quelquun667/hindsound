using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>
/// An off-screen run (--offscreen, --settings-probe) must never draw its notification on a real screen, and never take
/// a click. No window is shown here: only the placement and the flag handed to the notification are read.
/// </summary>
public class NotificationOffscreenTests
{
    private static readonly NotificationSpot Spot = new(new ScreenRect(100, 50, 2020, 1130), 144, false, true);

    [Fact]
    public void Offscreen_the_area_is_past_the_corner_of_the_virtual_screen_at_96_dpi_and_clicks_pass_through()
    {
        var placement = NotificationPlacement.For(Spot, offscreen: true);

        var right = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN) - 4000;
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) - 4000;
        Assert.Equal(right, placement.Area.Right);
        Assert.Equal(right - 1920, placement.Area.Left);
        Assert.Equal(top, placement.Area.Top);
        Assert.Equal(top + 1080, placement.Area.Bottom);
        Assert.Equal(96u, placement.Dpi);
        Assert.True(placement.ClickThrough);
        Assert.True(placement.LeftEdge);
    }

    [Fact]
    public void Offscreen_the_spot_of_the_app_does_not_matter_but_its_side()
    {
        var placement = NotificationPlacement.For(Spot with { LeftEdge = false, ClickThrough = false, Dpi = 192 }, offscreen: true);

        Assert.Equal(96u, placement.Dpi);
        Assert.True(placement.ClickThrough);
        Assert.False(placement.LeftEdge);
    }

    [Fact]
    public void On_a_screen_the_spot_of_the_app_is_kept_as_it_is()
    {
        var placement = NotificationPlacement.For(Spot, offscreen: false);

        Assert.Equal(new ScreenRect(100, 50, 2020, 1130), (ScreenRect)placement.Area);
        Assert.Equal(144u, placement.Dpi);
        Assert.False(placement.ClickThrough);
        Assert.True(placement.LeftEdge);

        var covering = NotificationPlacement.For(Spot with { ClickThrough = true, LeftEdge = false }, offscreen: false);
        Assert.True(covering.ClickThrough);
        Assert.False(covering.LeftEdge);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Windows_shell_hands_the_off_screen_choice_to_the_notification(bool offscreen)
    {
        var notification = Assert.IsType<Notification>(new WindowsShell().NewNotification(new NoLoop(), offscreen));

        Assert.Equal(offscreen, notification.Offscreen);
        Assert.Null(notification.Showing);
    }

    // A real UiLoop is the target of every message of the process while it is the last one made: the notification needs none.
    private sealed class NoLoop : IAppLoop
    {
        public Action? CloseAsked { get; set; }

        public Action? ScaleMayHaveChanged { get; set; }

        public void Post(Action action) => throw new NotSupportedException();

        public void After(int milliseconds, Action action) => throw new NotSupportedException();

        public void Run() => throw new NotSupportedException();

        public void Quit() => throw new NotSupportedException();

        public bool InFront() => false;

        public void Dispose()
        {
        }
    }
}
