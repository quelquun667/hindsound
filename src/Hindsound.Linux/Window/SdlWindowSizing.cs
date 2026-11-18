using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Window;

/// <summary>The frame the desktop draws around a window, in window coordinates. Zero when the desktop does not say.</summary>
internal readonly record struct WindowBorders(int Top, int Left, int Bottom, int Right);

/// <summary>
/// The size and the place of the Linux window, as plain arithmetic. SDL measures a window in window coordinates: pixels on
/// X11, logical units on Wayland. The pixels of its surface are those times its pixel density. A design pixel is the display
/// scale times the zoom, in surface pixels, as it is the DPI times the zoom on Windows.
/// </summary>
internal static class SdlWindowSizing
{
    /// <summary>What a design pixel is in surface pixels.</summary>
    public static double Scale(float displayScale, int zoom) => displayScale * zoom / 100.0;

    /// <summary>The size of the bitmap, the same as on Windows at the same scale.</summary>
    public static (int W, int H) PixelSize(double scale) =>
        ((int)Math.Round(SettingsScreen.WindowWidth * scale), (int)Math.Round(SettingsScreen.WindowHeight * scale));

    /// <summary>The size to ask SDL for, in window coordinates.</summary>
    public static (int W, int H) WindowSize(double scale, float density)
    {
        var (width, height) = PixelSize(scale);
        var d = density > 0 ? density : 1;
        return ((int)Math.Round(width / d), (int)Math.Round(height / d));
    }

    /// <summary>The whole window, frame included, in window coordinates.</summary>
    public static (int W, int H) OuterSize(double scale, float density, WindowBorders borders)
    {
        var (width, height) = WindowSize(scale, density);
        return (width + borders.Left + borders.Right, height + borders.Top + borders.Bottom);
    }

    /// <summary>The largest zoom of the settings, up to <paramref name="wanted"/>, whose whole window fits <paramref name="area"/>.</summary>
    public static int ChooseZoom(int wanted, float displayScale, float density, WindowBorders borders, ScreenRect area) =>
        WindowZoomFit.Choose(wanted, zoom => OuterSize(Scale(displayScale, zoom), density, borders), area.Width, area.Height);

    /// <summary>Where the inside of the window goes, centred in the area, and never above or left of it.</summary>
    /// <remarks>SDL places the inside of a window; the frame is around it.</remarks>
    public static (int X, int Y) Centered(ScreenRect area, (int W, int H) outer, WindowBorders borders) =>
        Fit(area.Left + (area.Width - outer.W) / 2, area.Top + (area.Height - outer.H) / 2, outer, area, borders);

    /// <summary>
    /// Where the inside of the window goes for a frame wanted at (<paramref name="outerLeft"/>, <paramref name="outerTop"/>):
    /// inside the area as far as it holds the window, and when even the smallest window is too big, its title bar stays in reach.
    /// </summary>
    public static (int X, int Y) Fit(int outerLeft, int outerTop, (int W, int H) outer, ScreenRect area, WindowBorders borders) =>
        (Math.Max(area.Left, Math.Min(outerLeft, area.Right - outer.W)) + borders.Left,
            Math.Max(area.Top, Math.Min(outerTop, area.Bottom - outer.H)) + borders.Top);

    /// <summary>A point of an SDL event, in window coordinates, in design pixels.</summary>
    public static float ToDesign(float windowCoordinate, float density, double scale) =>
        (float)(windowCoordinate * (density > 0 ? density : 1) / scale);

    /// <summary>The pixels of the bitmap that hold <paramref name="area"/>, cut to the bitmap. Empty when nothing is left.</summary>
    public static (int X, int Y, int W, int H) PixelArea(Box area, double scale, int width, int height)
    {
        var left = Math.Clamp((int)Math.Floor(area.X * scale), 0, width);
        var top = Math.Clamp((int)Math.Floor(area.Y * scale), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(area.Right * scale), left, width);
        var bottom = Math.Clamp((int)Math.Ceiling(area.Bottom * scale), top, height);
        return (left, top, right - left, bottom - top);
    }

    /// <summary>The smallest rectangle that holds both; an empty one counts as nothing.</summary>
    public static (int X, int Y, int W, int H) Union((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b)
    {
        if (a.W <= 0 || a.H <= 0)
        {
            return b;
        }

        if (b.W <= 0 || b.H <= 0)
        {
            return a;
        }

        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return (left, top, Math.Max(a.X + a.W, b.X + b.W) - left, Math.Max(a.Y + a.H, b.Y + b.H) - top);
    }

    /// <summary>
    /// The character a key types, from its SDL keycode: what goes to a text field the key itself brought back into view,
    /// since SDL had no text input on when the key went down. Null for a key that types nothing, or with Ctrl, Alt or Super.
    /// </summary>
    /// <param name="keycode">The SDL keycode: the character of the key on the layout, unshifted, for a key that types one.</param>
    /// <param name="modifiers">The <c>HotkeyGesture</c> flags.</param>
    public static string? TypedText(uint keycode, uint modifiers)
    {
        const uint scancodeMask = 1u << 30;
        if ((modifiers & ~Hindsound.Input.HotkeyGesture.ModShift) != 0 || (keycode & scancodeMask) != 0 || keycode < 0x20 || keycode == 0x7F
            || keycode > 0x10FFFF || keycode is >= 0xD800 and <= 0xDFFF)
        {
            return null;
        }

        var text = char.ConvertFromUtf32((int)keycode);
        return (modifiers & Hindsound.Input.HotkeyGesture.ModShift) != 0 ? text.ToUpperInvariant() : text;
    }
}
