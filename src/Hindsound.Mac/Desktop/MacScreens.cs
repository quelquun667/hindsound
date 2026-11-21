using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Mac.Interop;
using Hindsound.Ui;

namespace Hindsound.Mac.Desktop;

/// <summary>
/// The screens and the active window of macOS. The screens come from NSScreen: <c>frame</c> for the whole screen,
/// <c>visibleFrame</c> for the part without the menu bar and the Dock. The rectangles are in points, the unit of Cocoa and
/// of SDL's windows, turned to a top-left origin on the main screen as the rest of the app counts; the DPI is 96 for that
/// unit (the Retina density is the window's own, as SDL gives it). The active window is that of the frontmost application
/// (NSWorkspace), its bounds read in the window list of CoreGraphics, which gives them without the permission to record
/// the screen (the titles are what that permission guards, and nothing here reads them). <c>{app}</c> is the name of the
/// application.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacScreens : IScreens
{
    /// <summary>What the main screen is taken for when NSScreen gives none (no window server): nothing is drawn on it.</summary>
    internal static readonly ScreenInfo Placeholder = new(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1080), ScreenGeometry.PointDpi);

    public ForegroundInfo Foreground()
    {
        var (name, bounds) = Front();
        return new(name, bounds is { } b && ScreenGeometry.At(Center(b), All()) is { } screen && ScreenGeometry.Covers(b, screen.Monitor));
    }

    public bool ForegroundCovers() => Foreground().CoversMonitor;

    public bool ForegroundCovers(ScreenInfo screen) => Front().Bounds is { } b && ScreenGeometry.Covers(b, screen.Monitor);

    public ScreenInfo? ActiveScreen() => Front().Bounds is { } b ? ScreenGeometry.At(Center(b), All()) : null;

    public ScreenInfo? CursorScreen()
    {
        var screens = All();
        if (screens.Count == 0)
        {
            return null;
        }

        var point = AppKit.SendPoint(AppKit.Class("NSEvent"), "mouseLocation");
        return ScreenGeometry.At(ScreenGeometry.FromCocoa(point, MainTop()), screens);
    }

    public ScreenInfo PrimaryScreen() => All() is [var main, ..] ? main : Placeholder;

    /// <summary>Every screen, the main one first (the one with the menu bar, whose bottom-left corner is the origin of Cocoa).</summary>
    internal static IReadOnlyList<ScreenInfo> All()
    {
        using var pool = new ObjC.Pool();
        var screens = ObjC.Send(AppKit.Class("NSScreen"), "screens");
        var count = screens == 0 ? 0 : (int)ObjC.SendUInt(screens, "count");
        if (count == 0)
        {
            return [];
        }

        var main = AppKit.SendRect(AppKit.SendAt(screens, "objectAtIndex:", 0), "frame");
        var top = main.Y + main.Height;
        var list = new List<ScreenInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var screen = AppKit.SendAt(screens, "objectAtIndex:", (nuint)i);
            list.Add(new ScreenInfo(
                ScreenGeometry.FromCocoa(AppKit.SendRect(screen, "frame"), top),
                ScreenGeometry.FromCocoa(AppKit.SendRect(screen, "visibleFrame"), top),
                ScreenGeometry.PointDpi));
        }

        return list;
    }

    private static double MainTop()
    {
        using var pool = new ObjC.Pool();
        var screens = ObjC.Send(AppKit.Class("NSScreen"), "screens");
        var main = AppKit.SendRect(AppKit.SendAt(screens, "objectAtIndex:", 0), "frame");
        return main.Y + main.Height;
    }

    /// <summary>The name of the frontmost application (null for this app itself) and the bounds of its frontmost window.</summary>
    internal static (string? Name, ScreenRect? Bounds) Front()
    {
        using var pool = new ObjC.Pool();
        var workspace = ObjC.Send(AppKit.Class("NSWorkspace"), "sharedWorkspace");
        var app = workspace == 0 ? 0 : ObjC.Send(workspace, "frontmostApplication");
        if (app == 0)
        {
            return (null, null);
        }

        var pid = AppKit.SendInt(app, "processIdentifier");
        var name = pid == Environment.ProcessId ? null : ObjC.String(ObjC.Send(app, "localizedName"));
        return (name, WindowOf(pid));
    }

    // The first window of the process in the list of CoreGraphics, which goes from front to back, at the level of ordinary
    // windows (0): the menu bar, the Dock and the panels of the system sit at other levels.
    private static ScreenRect? WindowOf(int pid)
    {
        var list = AppKit.CGWindowListCopyWindowInfo(AppKit.OnScreenWindows, 0);
        if (list == 0)
        {
            return null;
        }

        try
        {
            var count = ObjC.SendUInt(list, "count");
            for (nuint i = 0; i < count; i++)
            {
                var window = AppKit.SendAt(list, "objectAtIndex:", i);
                if (AppKit.SendInt(AppKit.Get(window, "kCGWindowOwnerPID"), "intValue") != pid
                    || AppKit.SendInt(AppKit.Get(window, "kCGWindowLayer"), "intValue") != 0)
                {
                    continue;
                }

                var bounds = AppKit.Get(window, "kCGWindowBounds");
                if (bounds == 0)
                {
                    continue;
                }

                return ScreenGeometry.Round(new CGRect(
                    AppKit.SendDouble(AppKit.Get(bounds, "X"), "doubleValue"),
                    AppKit.SendDouble(AppKit.Get(bounds, "Y"), "doubleValue"),
                    AppKit.SendDouble(AppKit.Get(bounds, "Width"), "doubleValue"),
                    AppKit.SendDouble(AppKit.Get(bounds, "Height"), "doubleValue")));
            }

            return null;
        }
        finally
        {
            AppKit.CFRelease(list);
        }
    }

    private static (int X, int Y) Center(ScreenRect rect) => (rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
}

/// <summary>The arithmetic of the screens of macOS, apart from the calls, so that it runs anywhere.</summary>
internal static class ScreenGeometry
{
    /// <summary>The DPI of a rectangle counted in points.</summary>
    public const uint PointDpi = 96;

    /// <summary>A rectangle of Cocoa (origin bottom-left of the main screen, y up) with its top-left origin, y down.</summary>
    /// <param name="mainTop">The top of the main screen in Cocoa: its origin y plus its height.</param>
    public static ScreenRect FromCocoa(CGRect rect, double mainTop) =>
        Round(new CGRect(rect.X, mainTop - (rect.Y + rect.Height), rect.Width, rect.Height));

    /// <summary>A point of Cocoa with its top-left origin.</summary>
    public static (int X, int Y) FromCocoa(CGPoint point, double mainTop) =>
        ((int)Math.Floor(point.X), (int)Math.Floor(mainTop - point.Y));

    /// <summary>A rectangle of the window list of CoreGraphics, already top-left, in whole points.</summary>
    public static ScreenRect Round(CGRect rect)
    {
        var left = (int)Math.Round(rect.X);
        var top = (int)Math.Round(rect.Y);
        return new ScreenRect(left, top, left + (int)Math.Round(rect.Width), top + (int)Math.Round(rect.Height));
    }

    /// <summary>Whether a window covers the whole of a screen: full screen, or a borderless window as big as it.</summary>
    public static bool Covers(ScreenRect window, ScreenRect monitor) =>
        monitor.Width > 0 && monitor.Height > 0
        && window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    /// <summary>The screen that holds a point, the main one taking the edges they share; null when none does.</summary>
    public static ScreenInfo? At((int X, int Y) point, IReadOnlyList<ScreenInfo> screens)
    {
        foreach (var screen in screens)
        {
            var m = screen.Monitor;
            if (point.X >= m.Left && point.X < m.Right && point.Y >= m.Top && point.Y < m.Bottom)
            {
                return screen;
            }
        }

        return null;
    }
}
