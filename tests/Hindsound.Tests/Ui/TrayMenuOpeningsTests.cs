using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>
/// The icon's own memory between a right click and its menu: the menu opens where the click was, and a menu that opened
/// tells its command alone, so a wParam that holds the point is never read as a command too.
/// </summary>
public class TrayMenuOpeningsTests
{
    private readonly TrayMenuOpenings _openings = new();
    private readonly List<TrayCommand> _chosen = [];
    private readonly List<(int X, int Y)> _menus = [];

    // As the app does with the real icon: the menu gesture opens the menu, which tells the command the user picked.
    private void ShowMenuOnGesture(TrayGesture gesture)
    {
        if (gesture == TrayGesture.Menu)
        {
            _menus.Add(_openings.Open());
            _chosen.Add(TrayCommand.OpenFolder);
        }
    }

    [Fact]
    public void A_right_click_opens_one_menu_where_the_click_was_and_raises_only_its_command()
    {
        // Left of the main screen, at x = 9: the point read as a command would be Quit.
        _openings.Receive(Native.WM_CONTEXTMENU, 0x02000009, ShowMenuOnGesture, _chosen.Add);

        Assert.Equal([(9, 512)], _menus);
        Assert.Equal([TrayCommand.OpenFolder], _chosen);
    }

    [Fact]
    public void Each_right_click_opens_its_menu_where_it_was()
    {
        _openings.Receive(Native.WM_CONTEXTMENU, 0x00500030, ShowMenuOnGesture, _chosen.Add);
        _openings.Receive(Native.WM_CONTEXTMENU, 0x0400FFF0, ShowMenuOnGesture, _chosen.Add);

        Assert.Equal([(48, 80), (-16, 1024)], _menus);
        Assert.Equal([TrayCommand.OpenFolder, TrayCommand.OpenFolder], _chosen);
    }

    [Fact]
    public void A_menu_message_that_opens_no_menu_names_its_command()
    {
        _openings.Receive(Native.WM_CONTEXTMENU, (nuint)TrayCommand.Save30s, _ => { }, _chosen.Add);

        Assert.Empty(_menus);
        Assert.Equal([TrayCommand.Save30s], _chosen);
    }
}
