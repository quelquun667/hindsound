using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// No screen: the Linux app draws nothing, and the notification server places its notifications itself. The active window
/// is unknown (Wayland keeps it to itself), so <c>{app}</c> in a file name falls back as it does when Windows says nothing.
/// The contract asks for a main screen all the same; this one is a placeholder that nothing is drawn on.
/// </summary>
internal sealed class LinuxScreens : IScreens
{
    private static readonly ScreenInfo Placeholder = new(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080), 96);

    public ForegroundInfo Foreground() => new(null, false);

    public bool ForegroundCovers() => false;

    public bool ForegroundCovers(ScreenInfo screen) => false;

    public ScreenInfo? ActiveScreen() => null;

    public ScreenInfo? CursorScreen() => null;

    public ScreenInfo PrimaryScreen() => Placeholder;
}
