using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>The two adapters of the desktop whose answer is a choice of the code, not of the system: what each one says is pinned with made-up readings.</summary>
public class WindowsDesktopAdaptersTests
{
    [Theory]
    [InlineData(0, 1, UserNotificationState.NotPresent)]
    [InlineData(0, 2, UserNotificationState.Busy)]
    [InlineData(0, 3, UserNotificationState.RunningD3dFullScreen)]
    public void A_successful_call_gives_the_state_it_wrote(int result, int raw, UserNotificationState expected) =>
        Assert.Equal(expected, WindowsQuietState.FromResult(result, raw));

    [Theory]
    [InlineData(1)]
    [InlineData(unchecked((int)0x80004005))]
    public void A_failed_call_is_unknown_whatever_was_left_in_the_variable(int result) =>
        Assert.Equal(UserNotificationState.Unknown, WindowsQuietState.FromResult(result, (int)UserNotificationState.Busy));

    [Fact]
    public void The_active_screen_is_where_the_window_is_and_the_cursor_screen_where_the_cursor_is()
    {
        var window = new ScreenInfo(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 96);
        var cursor = new ScreenInfo(new ScreenRect(1920, 0, 3840, 1080), new ScreenRect(1920, 0, 3840, 1040), 144);
        var screens = new WindowsScreens(() => window, () => cursor);

        Assert.Equal(window, screens.ActiveScreen());
        Assert.Equal(cursor, screens.CursorScreen());
    }
}
