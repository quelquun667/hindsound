namespace Hindsound.Ui;

/// <summary>A screen, in physical pixels.</summary>
/// <param name="Monitor">All of it.</param>
/// <param name="Work">What the taskbar leaves.</param>
/// <param name="Dpi">Its own DPI.</param>
internal readonly record struct ScreenInfo(Native.RECT Monitor, Native.RECT Work, uint Dpi);

/// <summary>What the active window looks like from outside.</summary>
/// <param name="Title">Its title, for <c>{app}</c>. Empty if it has none.</param>
/// <param name="CoversMonitor">It covers its whole screen, and is not the desktop itself.</param>
internal readonly record struct ForegroundInfo(string Title, bool CoversMonitor);

/// <summary>
/// Only reads the window manager: GetForegroundWindow, GetWindowText and the window's rectangle. No handle to the game's
/// process, no hook, nothing injected (PLAN section 4.8).
/// </summary>
internal static unsafe class ForegroundWindow
{
    public static ForegroundInfo Read()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0)
        {
            return new ForegroundInfo(string.Empty, false);
        }

        return new ForegroundInfo(TitleOf(hwnd), Covers(hwnd));
    }

    /// <summary>The screen under the active window.</summary>
    public static ScreenInfo ActiveScreen() =>
        ScreenOf(Native.MonitorFromWindow(Native.GetForegroundWindow(), Native.MONITOR_DEFAULTTOPRIMARY));

    /// <summary>Whether the active window covers that screen, which need not be the one it is on.</summary>
    public static bool CoversScreen(ScreenInfo screen)
    {
        var hwnd = Native.GetForegroundWindow();
        return hwnd != 0 && Covers(hwnd, screen.Monitor);
    }

    private static ScreenInfo ScreenOf(nint monitor)
    {
        Native.MONITORINFO info = default;
        info.Size = sizeof(Native.MONITORINFO);
        Native.GetMonitorInfoW(monitor, &info);
        uint dpi = 96, dpiY;
        Native.GetDpiForMonitor(monitor, 0, &dpi, &dpiY);
        return new ScreenInfo(info.Monitor, info.Work, dpi == 0 ? 96 : dpi);
    }

    /// <summary>The shell's own windows cover the screen too, and clicks on them must stay clicks.</summary>
    public static bool IsDesktopClass(string className) =>
        className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    /// <summary>At least the monitor's rectangle. A maximized window stops at the taskbar, so it does not count.</summary>
    public static bool RectCovers(Native.RECT window, Native.RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    private static string TitleOf(nint hwnd)
    {
        const int capacity = 256;
        var text = stackalloc char[capacity];
        var length = Native.GetWindowTextW(hwnd, text, capacity);
        return length > 0 ? new string(text, 0, length) : string.Empty;
    }

    private static bool Covers(nint hwnd)
    {
        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTOPRIMARY);
        Native.MONITORINFO info = default;
        info.Size = sizeof(Native.MONITORINFO);
        return Native.GetMonitorInfoW(monitor, &info) != 0 && Covers(hwnd, info.Monitor);
    }

    private static bool Covers(nint hwnd, Native.RECT monitor)
    {
        const int capacity = 64;
        var text = stackalloc char[capacity];
        var length = Native.GetClassNameW(hwnd, text, capacity);
        if (length > 0 && IsDesktopClass(new string(text, 0, length)))
        {
            return false;
        }

        Native.RECT window;
        return Native.GetWindowRect(hwnd, &window) != 0 && RectCovers(window, monitor);
    }
}
