using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>
/// The app reads what is true at the right click before it asks the tray to show the menu: on Windows, showing the menu
/// makes the app's own hidden window the active one, and the pointer goes on to the menu. Run on screen, so that the menu is
/// shown, with a tray that changes the foreground app and the pointer's screen as it shows it.
/// </summary>
public sealed class AppTrayMenuShownOrderTests : IDisposable
{
    private readonly ShellRunRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static string ScreenOf(ScreenInfo screen) => $"{screen.Monitor.Left},{screen.Monitor.Top},{screen.Monitor.Right},{screen.Monitor.Bottom}";

    [Fact]
    public void What_was_true_at_the_click_is_read_before_the_menu_shows()
    {
        var shown = 0;
        _rig.Shell.Tray.Shown = () =>
        {
            shown++;
            _rig.Screens.App = null;
            _rig.Screens.Pointer = ShellRunRig.Right;
        };

        _rig.Run(
            rig =>
            {
                rig.Screens.App = "game.exe";
                rig.Screens.Pointer = ShellRunRig.Left;
                rig.Shell.Tray.Gesture!(TrayGesture.Menu);
                rig.Shell.Tray.Chosen!(TrayCommand.Save30s);
            },
            offscreen: false);

        Assert.Equal(1, shown);
        Assert.Contains(_rig.Log(), line => line.Contains("tray menu: Save30s, app name known True, screen of the click " + ScreenOf(ShellRunRig.Left), StringComparison.Ordinal));
    }
}
