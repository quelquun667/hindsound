using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>
/// A command of the icon's menu acts on what was true at the right click: showing the menu makes the app's own hidden window
/// the active one, and the pointer goes on to the menu, so what is true when the command comes no longer says it.
/// </summary>
public sealed class AppTrayClickOrderTests : IDisposable
{
    private readonly ShellRunRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static string ScreenOf(ScreenInfo screen) => $"{screen.Monitor.Left},{screen.Monitor.Top},{screen.Monitor.Right},{screen.Monitor.Bottom}";

    [Fact]
    public void A_command_of_the_menu_keeps_the_app_and_the_screen_of_the_right_click()
    {
        _rig.Run(rig =>
        {
            rig.Screens.App = "game.exe";
            rig.Screens.Pointer = ShellRunRig.Left;
            rig.Shell.Tray.Gesture!(TrayGesture.Menu);

            rig.Screens.App = null;
            rig.Screens.Pointer = ShellRunRig.Right;
            rig.Shell.Tray.Chosen!(TrayCommand.Save30s);
        });

        Assert.Contains(_rig.Log(), line => line.Contains("tray menu: Save30s, app name known True, screen of the click " + ScreenOf(ShellRunRig.Left), StringComparison.Ordinal));
    }

    [Fact]
    public void A_command_without_a_right_click_before_it_reads_what_is_true_now()
    {
        _rig.Run(rig =>
        {
            rig.Screens.App = "game.exe";
            rig.Screens.Pointer = ShellRunRig.Left;
            rig.Shell.Tray.Gesture!(TrayGesture.Menu);
            rig.Shell.Tray.Chosen!(TrayCommand.Save30s);

            rig.Screens.App = null;
            rig.Screens.Pointer = ShellRunRig.Right;
            rig.Shell.Tray.Chosen!(TrayCommand.Save15s);
        });

        Assert.Contains(_rig.Log(), line => line.Contains("tray menu: Save15s, app name known False, screen of the click " + ScreenOf(ShellRunRig.Right), StringComparison.Ordinal));
    }
}
