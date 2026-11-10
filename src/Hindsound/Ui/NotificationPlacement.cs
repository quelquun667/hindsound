using Hindsound.App;

namespace Hindsound.Ui;

/// <summary>Where a notification goes.</summary>
/// <param name="Area">The rectangle it sticks to, in physical pixels: see <see cref="AreaOn"/>.</param>
/// <param name="Dpi">The DPI of that monitor.</param>
/// <param name="ClickThrough">Clicks pass through to whatever is below: the active window covers the screen.</param>
/// <param name="LeftEdge">Stuck to the left edge of the screen instead of the right one.</param>
internal readonly record struct NotificationPlacement(Native.RECT Area, uint Dpi, bool ClickThrough, bool LeftEdge)
{
    /// <summary>The top of the tab: 30 % down the area.</summary>
    public int Top => Area.Top + (int)Math.Round(Area.Height * 0.30);

    /// <summary>
    /// The work area, or a taskbar on that side would cover the tab. Over a full-screen window there is no taskbar to
    /// avoid, and the tab sticks to the edge of the screen itself.
    /// </summary>
    public static Native.RECT AreaOn(ScreenInfo screen, bool foregroundCoversScreen) =>
        Native.RECT.From(NotificationSpot.AreaOn(screen, foregroundCoversScreen));

    /// <summary>
    /// Where the app's choice puts it. Off screen, past the corner of the virtual screen at 96 dpi, where nothing shows it
    /// and clicks pass through.
    /// </summary>
    public static NotificationPlacement For(NotificationSpot spot, bool offscreen)
    {
        if (!offscreen)
        {
            return new NotificationPlacement(Native.RECT.From(spot.Area), spot.Dpi, spot.ClickThrough, spot.LeftEdge);
        }

        var right = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN) - 4000;
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) - 4000;
        return new NotificationPlacement(new Native.RECT { Left = right - 1920, Top = top, Right = right, Bottom = top + 1080 }, 96, true, spot.LeftEdge);
    }

    /// <summary>The left of the part of the tab that shows, from the edge it comes out of.</summary>
    public int LeftWhenShowing(int visible) => LeftEdge ? Area.Left : Area.Right - visible;
}
