using System.Runtime.Versioning;
using Hindsound.Linux.App;
using Hindsound.Linux.Window.Input;
using Hindsound.Ui;

namespace Hindsound.Linux.Window;

/// <summary>
/// What the settings window asks of SDL, for one window. The window's logic talks only to this, so it is tested with a fake
/// on any system; <see cref="SdlVideo"/> is the real one. Every call is made on the loop thread, except <see cref="Push"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal unsafe interface ISdlVideo
{
    /// <summary>Starts SDL's video for this window: the hints first if video is not running yet, then a count of SDL's own.</summary>
    /// <param name="offscreen">The offscreen driver: the window is in memory only, never shown, never focused.</param>
    void Start(bool offscreen);

    /// <summary>The usable part of the screen under the mouse, and its content scale, before the window exists.</summary>
    (ScreenRect Area, float Scale) ScreenUnderMouse();

    /// <summary>Makes the window, hidden, its inside at (<paramref name="x"/>, <paramref name="y"/>), in window coordinates.</summary>
    void Create(string title, int width, int height, int x, int y);

    uint WindowId { get; }

    /// <summary>What a window coordinate is in pixels, times the scale of the desktop (SDL_GetWindowDisplayScale).</summary>
    float DisplayScale { get; }

    /// <summary>Pixels per window coordinate: 1 on X11, the scale of the output on Wayland.</summary>
    float PixelDensity { get; }

    /// <summary>The size of the surface, in pixels.</summary>
    (int W, int H) PixelSize { get; }

    WindowBorders Borders { get; }

    /// <summary>Where the inside of the window is, and its size, in window coordinates.</summary>
    (int X, int Y, int W, int H) Bounds { get; }

    /// <summary>The usable part of the screen the window is on.</summary>
    ScreenRect UsableArea();

    void Place(int x, int y, int width, int height);

    void Show();

    /// <summary>Back from minimized, and to the front.</summary>
    void BringBack();

    bool HasFocus { get; }

    void SetIcon(uint[] argb, int side);

    /// <summary>Copies a part of the bitmap to the window and shows it.</summary>
    void Present(uint* bits, int width, int height, (int X, int Y, int W, int H) area);

    void CaptureMouse(bool on);

    /// <summary>The SDL_Keymod held now.</summary>
    ushort ModState();

    /// <summary>The keycode the scancode types on the layout with the SDL_Keymod applied; 0 when it has none.</summary>
    uint KeyFromScancode(uint scancode, ushort mod);

    /// <summary>Text input on or off, its box and caret in window coordinates.</summary>
    void TextInput(bool wanted, int x, int y, int w, int h, int cursor);

    string? ClipboardText();

    void SetClipboardText(string text);

    /// <summary>The system's folder dialog; null if the user backed out. <paramref name="pumpOnce"/> runs while it is open.</summary>
    string? PickFolder(string startFolder, Action pumpOnce);

    /// <summary>The wait of the loop through this window's queue; every event but the wake goes to <paramref name="handler"/>.</summary>
    ILoopPump NewPump(SdlEventHandler handler);

    /// <summary>Puts an event in this process's own queue. Safe from any thread.</summary>
    bool Push(in SdlNative.Event e);

    /// <summary>Destroys the window and stops this window's count of SDL's video. Safe to call twice.</summary>
    void Close();
}

/// <summary>
/// <see cref="ISdlVideo"/> on SDL 3.2. The hints are set only while SDL's video is off: they choose the driver for every
/// window of the process. The window asks for a surface of real pixels (high pixel density), so a Wayland output at 150 %
/// gets a sharp picture, not one scaled up.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class SdlVideo : ISdlVideo
{
    private nint _window;
    private bool _started;
    private bool _offscreen;
    private SdlPump? _pump;
    private SdlTextInput? _textInput;

    public void Start(bool offscreen)
    {
        if (!SdlLibrary.TryLoad())
        {
            throw new InvalidOperationException("SDL 3 could not be loaded: " + SdlLibrary.LastError);
        }

        _offscreen = offscreen;
        if (SdlNative.WasInit(SdlNative.InitVideo) == 0)
        {
            SdlNative.SetHint(SdlNative.HintAppId, SdlNative.AppId);
            SdlNative.SetHint(SdlNative.HintNoSignalHandlers, "1");
            SdlNative.SetHint(SdlNative.HintAllowScreensaver, "1");
            SdlNative.SetHint(SdlNative.HintVideoDriver, offscreen ? SdlNative.OffscreenDriver : null);
        }

        if (!SdlNative.InitSubSystem(SdlNative.InitVideo))
        {
            throw new InvalidOperationException("SDL video did not start: " + SdlNative.Error());
        }

        _started = true;
    }

    public (ScreenRect Area, float Scale) ScreenUnderMouse()
    {
        float x, y;
        SdlNative.GetGlobalMouseState(&x, &y);
        var point = new SdlNative.Point { X = (int)x, Y = (int)y };
        var display = SdlNative.GetDisplayForPoint(&point);
        if (display == 0)
        {
            display = SdlNative.GetPrimaryDisplay();
        }

        var scale = SdlNative.GetDisplayContentScale(display);
        return (AreaOf(display), scale > 0 ? scale : 1);
    }

    public void Create(string title, int width, int height, int x, int y)
    {
        var flags = SdlNative.WindowHidden | SdlNative.WindowHighPixelDensity;
        _window = SdlNative.CreateWindow(title, width, height, flags);
        if (_window == 0)
        {
            throw new InvalidOperationException("SDL made no window: " + SdlNative.Error());
        }

        if (!_offscreen)
        {
            SdlNative.SetWindowPosition(_window, x, y);
        }

        _textInput = new SdlTextInput(_window);
    }

    public uint WindowId => SdlNative.GetWindowID(_window);

    public float DisplayScale => Positive(SdlNative.GetWindowDisplayScale(_window));

    public float PixelDensity => Positive(SdlNative.GetWindowPixelDensity(_window));

    public (int W, int H) PixelSize
    {
        get
        {
            int w = 0, h = 0;
            SdlNative.GetWindowSizeInPixels(_window, &w, &h);
            return (w, h);
        }
    }

    public WindowBorders Borders
    {
        get
        {
            int top = 0, left = 0, bottom = 0, right = 0;
            return SdlNative.GetWindowBordersSize(_window, &top, &left, &bottom, &right) ? new(top, left, bottom, right) : default;
        }
    }

    public (int X, int Y, int W, int H) Bounds
    {
        get
        {
            int x = 0, y = 0, w = 0, h = 0;
            SdlNative.GetWindowPosition(_window, &x, &y);
            SdlNative.GetWindowSize(_window, &w, &h);
            return (x, y, w, h);
        }
    }

    public ScreenRect UsableArea()
    {
        var display = SdlNative.GetDisplayForWindow(_window);
        return AreaOf(display != 0 ? display : SdlNative.GetPrimaryDisplay());
    }

    public void Place(int x, int y, int width, int height)
    {
        SdlNative.SetWindowSize(_window, width, height);
        if (!_offscreen)
        {
            // Wayland lets no program place its window: SDL ignores this there, and the desktop decides.
            SdlNative.SetWindowPosition(_window, x, y);
        }
    }

    public void Show()
    {
        SdlNative.ShowWindow(_window);
        SdlNative.RaiseWindow(_window);
    }

    public void BringBack()
    {
        SdlNative.RestoreWindow(_window);
        SdlNative.ShowWindow(_window);
        SdlNative.RaiseWindow(_window);
    }

    public bool HasFocus => _window != 0 && (SdlNative.GetWindowFlags(_window) & SdlNative.WindowInputFocus) != 0;

    public void SetIcon(uint[] argb, int side)
    {
        fixed (uint* pixels = argb)
        {
            var surface = SdlNative.CreateSurfaceFrom(side, side, SdlNative.PixelFormatArgb8888, pixels, side * 4);
            if (surface is null)
            {
                return;
            }

            SdlNative.SetWindowIcon(_window, surface);
            SdlNative.DestroySurface(surface);
        }
    }

    public void Present(uint* bits, int width, int height, (int X, int Y, int W, int H) area)
    {
        // The window's surface may be a new one after a resize: SDL hands out the current one each time.
        var target = SdlNative.GetWindowSurface(_window);
        if (target is null || area.W <= 0 || area.H <= 0)
        {
            return;
        }

        // Our bitmap is B, G, R, X in memory: XRGB8888 on a little-endian system. Wrapping it copies nothing.
        var source = SdlNative.CreateSurfaceFrom(width, height, SdlNative.PixelFormatXrgb8888, bits, width * 4);
        if (source is null)
        {
            return;
        }

        var rect = new SdlNative.Rect(area.X, area.Y, area.W, area.H);
        SdlNative.BlitSurface(source, &rect, target, &rect);
        SdlNative.DestroySurface(source);
        SdlNative.UpdateWindowSurfaceRects(_window, &rect, 1);
    }

    public void CaptureMouse(bool on)
    {
        if (!_offscreen)
        {
            SdlNative.CaptureMouse(on);
        }
    }

    public ushort ModState() => SdlNative.GetModState();

    public uint KeyFromScancode(uint scancode, ushort mod) => SdlNative.GetKeyFromScancode(scancode, mod, false);

    public void TextInput(bool wanted, int x, int y, int w, int h, int cursor) => _textInput?.Update(wanted, x, y, w, h, cursor);

    public string? ClipboardText() => SdlClipboardText.Get();

    public void SetClipboardText(string text) => SdlClipboardText.Set(text);

    public string? PickFolder(string startFolder, Action pumpOnce) => SdlFolderPicker.Pick(_window, startFolder, pumpOnce);

    public ILoopPump NewPump(SdlEventHandler handler)
    {
        _pump = new SdlPump { Handler = handler };
        return _pump;
    }

    public bool Push(in SdlNative.Event e)
    {
        var copy = e;
        return SdlNative.PushEvent(&copy);
    }

    public void Close()
    {
        if (_pump is not null)
        {
            _pump.Handler = null;
        }

        if (_textInput is { On: true })
        {
            _textInput.Update(false, 0, 0, 0, 0, 0);
        }

        if (_window != 0)
        {
            SdlNative.DestroyWindow(_window);
            _window = 0;
        }

        // SDL counts the starts of each subsystem: video stops with the last window, and the app goes back to its memory.
        if (_started)
        {
            _started = false;
            SdlNative.QuitSubSystem(SdlNative.InitVideo);
        }
    }

    private static ScreenRect AreaOf(uint display)
    {
        SdlNative.Rect rect;
        return SdlNative.GetDisplayUsableBounds(display, &rect)
            ? new ScreenRect(rect.X, rect.Y, rect.X + rect.W, rect.Y + rect.H)
            : new ScreenRect(0, 0, 1920, 1080);
    }

    private static float Positive(float value) => value > 0 ? value : 1;
}
