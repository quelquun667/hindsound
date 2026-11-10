namespace Hindsound.Ui;

/// <summary>
/// Only reads the window manager: GetForegroundWindow, GetWindowText and the window's rectangle. No handle to the game's
/// process, no hook, nothing injected.
/// </summary>
internal static unsafe class ForegroundWindow
{
    public static ForegroundInfo Read()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0)
        {
            return new ForegroundInfo(null, false);
        }

        uint processId;
        Native.GetWindowThreadProcessId(hwnd, &processId);
        var title = TitleOf(hwnd);
        var usable = UsableForApp(title, ClassOf(hwnd), processId == (uint)Environment.ProcessId);
        return new ForegroundInfo(usable ? title : null, Covers(hwnd));
    }

    /// <summary>
    /// Whether a window's title can name a clip. The taskbar, the desktop and this app's own windows say nothing about what
    /// the user was doing: a right click on the icon makes the taskbar the active window, and showing the menu makes it ours.
    /// </summary>
    public static bool UsableForApp(string title, string className, bool ownWindow) =>
        !ownWindow
        && !string.IsNullOrWhiteSpace(title)
        && !IsDesktopClass(className)
        && className is not ("NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "TaskListThumbnailWnd")
        && !className.StartsWith("Hindsound", StringComparison.Ordinal);

    /// <summary>
    /// Whether the active window covers its screen, without reading its title. Safe from another thread: GetWindowText on a
    /// window of this process sends a message to its loop and waits for the answer.
    /// </summary>
    public static bool ForegroundCovers()
    {
        var hwnd = Native.GetForegroundWindow();
        return hwnd != 0 && Covers(hwnd);
    }

    /// <summary>The screen under the active window.</summary>
    public static ScreenInfo ActiveScreen() =>
        ScreenOf(Native.MonitorFromWindow(Native.GetForegroundWindow(), Native.MONITOR_DEFAULTTOPRIMARY));

    /// <summary>The screen under the mouse.</summary>
    public static ScreenInfo CursorScreen()
    {
        Native.POINT cursor = default;
        Native.GetCursorPos(&cursor);
        return ScreenOf(Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST));
    }

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
    public static bool RectCovers(ScreenRect window, ScreenRect monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    private static string TitleOf(nint hwnd)
    {
        const int capacity = 256;
        var text = stackalloc char[capacity];
        var length = Native.GetWindowTextW(hwnd, text, capacity);
        return length > 0 ? new string(text, 0, length) : string.Empty;
    }

    private static string ClassOf(nint hwnd)
    {
        const int capacity = 64;
        var text = stackalloc char[capacity];
        var length = Native.GetClassNameW(hwnd, text, capacity);
        return length > 0 ? new string(text, 0, length) : string.Empty;
    }

    private static bool Covers(nint hwnd)
    {
        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTOPRIMARY);
        Native.MONITORINFO info = default;
        info.Size = sizeof(Native.MONITORINFO);
        return Native.GetMonitorInfoW(monitor, &info) != 0 && Covers(hwnd, info.Monitor);
    }

    private static bool Covers(nint hwnd, ScreenRect monitor)
    {
        if (IsDesktopClass(ClassOf(hwnd)))
        {
            return false;
        }

        Native.RECT window;
        return Native.GetWindowRect(hwnd, &window) != 0 && RectCovers(window, monitor);
    }
}
