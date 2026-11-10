namespace Hindsound.Ui;

/// <summary>
/// A rectangle in physical pixels, edges included on the left and top, as the window manager gives it. The same four fields
/// as the Win32 RECT, which converts to it, so this code does not depend on Windows.
/// </summary>
internal readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>A screen, in physical pixels.</summary>
/// <param name="Monitor">All of it.</param>
/// <param name="Work">What the taskbar leaves.</param>
/// <param name="Dpi">Its own DPI.</param>
internal readonly record struct ScreenInfo(ScreenRect Monitor, ScreenRect Work, uint Dpi);

/// <summary>What the active window looks like from outside.</summary>
/// <param name="AppName">Its title, for <c>{app}</c>. Null if it has none, or if it says nothing about what the user was doing.</param>
/// <param name="CoversMonitor">It covers its whole screen, and is not the desktop itself.</param>
internal readonly record struct ForegroundInfo(string? AppName, bool CoversMonitor);
