using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Unix.Notification;

/// <summary>The window a <see cref="NotificationPresenter"/> draws in: one at a time, on the loop thread.</summary>
internal interface INotificationSurface : IDisposable
{
    /// <summary>The usable area of the main screen, in desktop units: where a notification goes when the app gave no area.</summary>
    ScreenRect DefaultArea();

    /// <summary>Makes the window, hidden until the first <see cref="Show"/>, or moves it. Returns physical pixels per desktop unit.</summary>
    /// <param name="bounds">Where it ends up, in desktop units, as wide and as high as the notification.</param>
    /// <param name="clickThrough">Clicks pass through it to whatever is below.</param>
    double Open(ScreenRect bounds, bool clickThrough);

    /// <summary>
    /// Shows the columns <paramref name="slice"/> names of the picture, the rest of the window transparent. The pixels are sent
    /// again only when <paramref name="pictureChanged"/>.
    /// </summary>
    void Show(NotificationPicture picture, bool pictureChanged, (int SourceX, int DestinationX, int Width) slice);

    /// <summary>The clicks on the window since the last call, its pending events handled first.</summary>
    int TakeClicks();

    /// <summary>Hides the window and frees it; <see cref="Open"/> makes a new one.</summary>
    void Close();
}

/// <summary>
/// The notification drawn by the app on a system without the Windows layered window (macOS today, Linux later if it wants
/// one): the picture of <see cref="NotificationPicture"/> in a window of an <see cref="INotificationSurface"/>, with the
/// motion and the times of <see cref="NotificationMotion"/>, the timers of the app's loop, one at a time, replaced in place.
/// </summary>
internal sealed class NotificationPresenter : INotificationView
{
    /// <summary>The gap between the top of the area (below the menu bar on macOS) and the notification, in desktop units.</summary>
    public const int TopGap = 10;

    private readonly IAppLoop _loop;
    private readonly Func<INotificationSurface>? _newSurface;
    private readonly Func<ICanvasText> _newText;
    private readonly Func<long> _nowMs;
    private readonly Action<string> _log;
    private INotificationSurface? _surface;
    private ICanvasText? _text;
    private NotificationPicture? _picture;
    private bool _pictureChanged, _leftEdge, _clickThrough;
    private (int, int, int) _lastSlice;
    private long _start;
    private int _generation;

    /// <param name="newSurface">Makes the window, once; null draws where nothing shows it (<c>--offscreen</c>) and keeps the times.</param>
    /// <param name="newText">Makes the text, once, at the first notification; the presenter disposes it.</param>
    /// <param name="nowMs">The clock, in milliseconds; null is the monotonic clock of the system.</param>
    public NotificationPresenter(IAppLoop loop, Func<INotificationSurface>? newSurface, Func<ICanvasText> newText, Action<string> log, Func<long>? nowMs = null)
    {
        _loop = loop;
        _newSurface = newSurface;
        _newText = newText;
        _log = log;
        _nowMs = nowMs ?? (static () => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency);
    }

    public NotificationContent? Showing { get; private set; }

    public Action? Clicked { get; set; }

    public Action? Finished { get; set; }

    /// <summary>Holds it on screen past its time, until set back to false: the proof in the CI looks at it meanwhile.</summary>
    internal bool HoldOpen { get; set; }

    /// <summary>Where the window ends up, in desktop units.</summary>
    internal ScreenRect Bounds { get; private set; }

    /// <summary>Physical pixels per desktop unit of the picture shown.</summary>
    internal double Scale { get; private set; }

    /// <summary>The picture shown, or null.</summary>
    internal NotificationPicture? Picture => _picture;

    /// <summary>The frames sent to the window so far.</summary>
    internal int Frames { get; private set; }

    /// <summary>Where a notification goes in <paramref name="area"/>: against its right or left edge, <see cref="TopGap"/> below its top.</summary>
    public static ScreenRect Place(ScreenRect area, bool leftEdge)
    {
        var left = leftEdge ? area.Left : area.Right - NotificationPicture.DesignWidth;
        var top = area.Top + TopGap;
        return new ScreenRect(left, top, left + NotificationPicture.DesignWidth, top + NotificationPicture.DesignHeight);
    }

    public void Present(NotificationContent content, NotificationSpot spot, uint accent)
    {
        ICanvasText text;
        try
        {
            text = _text ??= _newText();
        }
        catch (TextUnavailableException ex)
        {
            // Nothing shows rather than a notification without its words; the app goes on as when it has left the screen.
            _log("notification: no text: " + ex.Message);
            _loop.Post(() => Finished?.Invoke());
            return;
        }

        var replaced = Showing is not null;
        var scale = spot.Dpi > 0 ? spot.Dpi / 96.0 : 1;
        if (_newSurface is not null)
        {
            _surface ??= _newSurface();
            var area = spot.Area.Width > 0 && spot.Area.Height > 0 ? spot.Area : _surface.DefaultArea();
            Bounds = Place(area, spot.LeftEdge);
            scale = _surface.Open(Bounds, spot.ClickThrough);
        }

        var picture = new NotificationPicture(content, scale, accent, spot.LeftEdge, text);
        _picture?.Dispose();
        _picture = picture;
        _pictureChanged = true;
        _leftEdge = spot.LeftEdge;
        _clickThrough = spot.ClickThrough;
        _lastSlice = default;
        Scale = scale;
        Showing = content;

        // Replaced in place when one is already up: no second entry, the hold simply starts over, and the frames go on.
        _start = _nowMs() - (replaced ? NotificationMotion.EnterMs : 0);
        if (replaced)
        {
            Draw(_nowMs() - _start);
            return;
        }

        var generation = ++_generation;
        Tick(generation);
    }

    private void Tick(int generation)
    {
        if (generation != _generation || _picture is null)
        {
            return;
        }

        var clicks = _surface?.TakeClicks() ?? 0;
        if (clicks > 0 && !_clickThrough)
        {
            Clicked?.Invoke();
            if (generation != _generation || _picture is null)
            {
                return;
            }
        }

        var elapsed = _nowMs() - _start;
        if (HoldOpen && elapsed > NotificationMotion.HoldEndMs - NotificationMotion.HoldTickMs)
        {
            elapsed = NotificationMotion.HoldEndMs - NotificationMotion.HoldTickMs;
            _start = _nowMs() - elapsed;
        }

        if (elapsed >= NotificationMotion.TotalMs)
        {
            Hide();
            Finished?.Invoke();
            return;
        }

        Draw(elapsed);
        _loop.After(NotificationMotion.NextTick(elapsed), () => Tick(generation));
    }

    private void Draw(double elapsed)
    {
        var picture = _picture!;
        var barsChanged = picture.SetBarsScale(NotificationMotion.BarsScale(elapsed));
        var slice = NotificationMotion.Slice(picture.Width, NotificationMotion.Visible(elapsed), _leftEdge);
        if (!barsChanged && !_pictureChanged && slice == _lastSlice)
        {
            return;
        }

        _surface?.Show(picture, _pictureChanged || barsChanged, slice);
        _pictureChanged = false;
        _lastSlice = slice;
        Frames++;
    }

    private void Hide()
    {
        _generation++;
        _surface?.Close();
        _picture?.Dispose();
        _picture = null;
        Showing = null;
    }

    public void Dispose()
    {
        Hide();
        _surface?.Dispose();
        _surface = null;
        _text?.Dispose();
        _text = null;
    }
}
