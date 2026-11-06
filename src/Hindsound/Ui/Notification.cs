using System.Diagnostics;
using System.Runtime.InteropServices;

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
        foregroundCoversScreen ? screen.Monitor : screen.Work;

    /// <summary>The left of the part of the tab that shows, from the edge it comes out of.</summary>
    public int LeftWhenShowing(int visible) => LeftEdge ? Area.Left : Area.Right - visible;
}

/// <summary>
/// The notification: one layered window, created on first use, replaced in place, never activated. It grows out of the
/// screen edge and shows part of the bitmap, so nothing crosses onto a neighbouring screen.
/// </summary>
internal sealed unsafe class Notification : IDisposable
{
    private const string ClassName = "HindsoundNotification";
    private const int EnterMs = 140, BarsMs = 200, HoldEndMs = EnterMs + 2600, TotalMs = HoldEndMs + 180;
    private const nuint TimerId = 1;

    private static Notification? _current;
    private static bool _classRegistered;

    private nint _hwnd;
    private NotificationBitmap? _bitmap;
    private NotificationPlacement _placement;
    private long _start;
    private int _lastVisible = -1;
    private bool _shown, _holding;

    /// <summary>The notification has left the screen.</summary>
    public Action? Finished { get; set; }

    /// <summary>A click landed on it. Never raised while clicks pass through.</summary>
    public Action? Clicked { get; set; }

    /// <summary>What is shown now, or null.</summary>
    public NotificationContent? Showing { get; private set; }

    public void Present(NotificationContent content, NotificationPlacement placement, uint accent)
    {
        EnsureWindow();
        var replaced = _shown;
        var bitmap = new NotificationBitmap(content, placement.Dpi / 96.0, accent, placement.LeftEdge);
        _bitmap?.Dispose();
        _bitmap = bitmap;
        Showing = content;

        var style = Native.GetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE);
        var wanted = placement.ClickThrough ? style | (nint)Native.WS_EX_TRANSPARENT : style & ~(nint)Native.WS_EX_TRANSPARENT;
        if (wanted != style)
        {
            Native.SetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE, wanted);
        }

        _placement = placement;
        _lastVisible = -1;
        _holding = false;

        // Replaced in place when one is already up: no second entry, the hold simply starts over.
        _start = Stopwatch.GetTimestamp() - (replaced ? Stopwatch.Frequency * EnterMs / 1000 : 0);
        Native.SetTimer(_hwnd, TimerId, 10, 0);
        Frame();
    }

    private void Frame()
    {
        if (_bitmap == null)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        if (elapsed >= TotalMs)
        {
            Hide();
            Finished?.Invoke();
            return;
        }

        if (elapsed >= BarsMs && elapsed < HoldEndMs)
        {
            Draw(1, 1);
            _holding = true;

            // Nothing moves until the exit: one timer tick, no frames.
            Native.SetTimer(_hwnd, TimerId, (uint)Math.Max(1, HoldEndMs - elapsed), 0);
            return;
        }

        if (_holding)
        {
            _holding = false;
            Native.SetTimer(_hwnd, TimerId, 10, 0);
        }

        double visible;
        if (elapsed < EnterMs)
        {
            var t = 1 - elapsed / EnterMs;
            visible = 1 - t * t * t;
        }
        else if (elapsed < HoldEndMs)
        {
            visible = 1;
        }
        else
        {
            var t = (elapsed - HoldEndMs) / (TotalMs - HoldEndMs);
            visible = 1 - t * t * t;
        }

        Draw(visible, elapsed < BarsMs ? 0.05f + 0.95f * (float)(elapsed / BarsMs) : 1);
    }

    private void Hide()
    {
        Native.KillTimer(_hwnd, TimerId);
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        _shown = false;
        Showing = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void Draw(double visibleFraction, float barsScale)
    {
        var bitmap = _bitmap!;
        var visible = Math.Max(1, (int)Math.Round(bitmap.Width * visibleFraction));
        var barsChanged = bitmap.SetBarsScale(barsScale);
        var moved = visible != _lastVisible;
        if (!moved && !barsChanged)
        {
            return;
        }

        _lastVisible = visible;
        Native.POINT destination, source = default;
        Native.SIZE size;
        size.Cy = bitmap.Height;
        size.Cx = visible;
        destination.Y = _placement.Top;
        destination.X = _placement.LeftWhenShowing(visible);
        if (_placement.LeftEdge)
        {
            // The mirror of the right edge: the inner part of the tab comes out first, the stripe last.
            source.X = bitmap.Width - visible;
        }

        Native.BLENDFUNCTION blend = default;
        blend.SourceConstantAlpha = 255;
        blend.AlphaFormat = 1;

        var dirty = bitmap.BarsRect;
        Native.UPDATELAYEREDWINDOWINFO info = default;
        info.Size = (uint)sizeof(Native.UPDATELAYEREDWINDOWINFO);
        info.PptDst = &destination;
        info.Psize = &size;
        info.HdcSrc = bitmap.Dc;
        info.PptSrc = &source;
        info.Pblend = &blend;
        info.Flags = Native.ULW_ALPHA;
        if (!moved && visible == bitmap.Width)
        {
            info.PrcDirty = &dirty;
        }

        Native.UpdateLayeredWindowIndirect(_hwnd, &info);
        if (!_shown)
        {
            _shown = true;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
    }

    private void EnsureWindow()
    {
        if (_hwnd != 0)
        {
            return;
        }

        _current = this;
        var instance = Native.GetModuleHandleW(null);
        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                Native.WNDCLASSEXW cls = default;
                cls.Size = (uint)sizeof(Native.WNDCLASSEXW);
                cls.WndProc = &WndProc;
                cls.Instance = instance;
                cls.ClassName = name;
                Native.RegisterClassExW(&cls);
            }

            _classRegistered = true;
        }

        _hwnd = Native.CreateWindowExW(
            Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW,
            ClassName, "Hindsound notification", Native.WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed");
        }
    }

    public void Dispose()
    {
        if (_hwnd != 0)
        {
            Hide();
            Native.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        // An exception crossing this boundary would end the process, and the buffer with it.
        try
        {
            switch (message)
            {
                case Native.WM_MOUSEACTIVATE:
                    return Native.MA_NOACTIVATE;
                case Native.WM_TIMER:
                    _current?.Frame();
                    return 0;
                case Native.WM_LBUTTONUP:
                    _current?.Clicked?.Invoke();
                    return 0;
                default:
                    return Native.DefWindowProcW(hwnd, message, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
            return 0;
        }
    }
}

/// <summary>Where exceptions caught at the window procedures go.</summary>
internal static class UiErrors
{
    public static Action<Exception>? Handler { get; set; }

    public static void Report(Exception ex)
    {
        try
        {
            Handler?.Invoke(ex);
        }
        catch (Exception)
        {
            // Nothing left to tell.
        }
    }
}
