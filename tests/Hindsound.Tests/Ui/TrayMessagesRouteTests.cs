using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>How a message of the Windows notification area becomes a gesture of the app, and, off screen, a command.</summary>
public class TrayMessagesRouteTests
{
    private readonly List<TrayGesture> _gestures = [];
    private readonly List<TrayCommand> _chosen = [];
    private int _opens;

    private void Route(uint code, nuint wParam, bool opensMenu) =>
        TrayMessages.Route(code, wParam, gesture =>
        {
            _gestures.Add(gesture);
            if (opensMenu)
            {
                _opens++;
            }
        }, () => _opens, _chosen.Add);

    [Fact]
    public void A_right_click_asks_for_the_menu_and_the_menu_tells_the_command_itself()
    {
        Route(Native.WM_CONTEXTMENU, 0x00500030, opensMenu: true);

        Assert.Equal([TrayGesture.Menu], _gestures);
        Assert.Empty(_chosen);
    }

    [Fact]
    public void Off_screen_the_menu_does_not_open_and_the_message_names_the_command()
    {
        Route(Native.WM_CONTEXTMENU, (nuint)TrayCommand.Library, opensMenu: false);

        Assert.Equal([TrayGesture.Menu], _gestures);
        Assert.Equal([TrayCommand.Library], _chosen);
    }

    [Fact]
    public void A_left_click_opens_the_app_and_chooses_nothing()
    {
        Route(Native.NIN_SELECT, (nuint)TrayCommand.Quit, opensMenu: false);

        Assert.Equal([TrayGesture.OpenSettings], _gestures);
        Assert.Empty(_chosen);
    }

    [Fact]
    public void Any_other_message_of_the_icon_reaches_nothing()
    {
        Route(Native.WM_LBUTTONUP, (nuint)TrayCommand.Quit, opensMenu: false);

        Assert.Empty(_gestures);
        Assert.Empty(_chosen);
    }

    [Theory]
    [InlineData(0x00500030u, 48, 80)]
    [InlineData(0xFFF0FFE0u, -32, -16)]
    public void The_point_of_the_menu_is_read_as_signed_screen_coordinates(uint wParam, int x, int y)
    {
        Assert.Equal((x, y), TrayMessages.PointOf(wParam));
    }
}
