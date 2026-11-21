using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Ui;
using Hindsound.Unix.Window;

namespace Hindsound.Unix.Notification;

/// <summary>What a system adds to the SDL window of the notification, through the window of the system under it.</summary>
internal interface INotificationWindowHook
{
    /// <summary>The SDL property that holds the window of the system (on macOS, <c>SDL.window.cocoa.window</c>, the NSWindow).</summary>
    string NativeWindowProperty { get; }

    /// <summary>The window was made: what SDL does not set (its level, its Spaces).</summary>
    void Created(nint nativeWindow);

    /// <summary>Clicks pass through the window, or not.</summary>
    void ClickThrough(nint nativeWindow, bool on);
}

/// <summary>
/// The window of the drawn notification on SDL 3: no border, above the other windows, transparent where the picture is, never
/// focusable, shown without being activated. It draws through an SDL renderer, the picture being a texture blended as
/// premultiplied alpha over a transparent clear, so its cut corner and the part not yet out of the edge show what is behind.
/// Its events never reach the app's queue: an event filter takes those of this window (the clicks are counted there) and
/// lets every other one through to the filter before it.
/// </summary>
/// <remarks>
/// On macOS, video must start on the main thread (SDL refuses otherwise), the thread of the app's loop. SDL_PumpEvents in
/// <see cref="TakeClicks"/> hands the events of the system to SDL even when no other window of the app is open.
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed unsafe class SdlNotificationSurface : INotificationSurface
{
    // SDL_WindowFlags not in SdlNative.
    private const ulong WindowAlwaysOnTop = 0x0000000000010000, WindowTransparent = 0x0000000040000000, WindowNotFocusable = 0x0000000080000000;
    private const int TextureAccessStreaming = 1;
    private const uint BlendPremultiplied = 0x00000010;

    /// <summary>"1": SDL neither makes the app a regular one nor activates it when video starts on macOS.</summary>
    internal const string HintMacBackgroundApp = "SDL_MAC_BACKGROUND_APP";

    /// <summary>"0": a window shown is not made the key window.</summary>
    internal const string HintActivateWhenShown = "SDL_WINDOW_ACTIVATE_WHEN_SHOWN";

    /// <summary>"1": a click on the window of an app in the background reaches it (macOS), instead of only bringing the app forward.</summary>
    internal const string HintMouseFocusClickThrough = "SDL_MOUSE_FOCUS_CLICKTHROUGH";

    // The window the filter looks for, and what it saw; the filter runs on whichever thread adds an event.
    private static uint s_windowId;
    private static int s_clicks;
    private static nint s_previousFilter, s_previousData;

    private readonly INotificationWindowHook? _hook;
    private readonly Api _sdl;
    private nint _window, _renderer, _texture, _native;
    private int _textureWidth, _textureHeight;
    private bool _started, _visible, _filterSet;

    public SdlNotificationSurface(INotificationWindowHook? hook)
    {
        _hook = hook;
        if (!SdlLibrary.TryLoad())
        {
            throw new InvalidOperationException("SDL 3 could not be loaded: " + SdlLibrary.LastError);
        }

        _sdl = new Api(SdlLibrary.Handle());
    }

    /// <summary>The SDL id of the window, 0 before the first <see cref="Open"/>.</summary>
    public uint WindowId => _window == 0 ? 0 : SdlNative.GetWindowID(_window);

    /// <summary>Whether SDL says the window has the keyboard focus.</summary>
    public bool HasFocus => _window != 0 && (SdlNative.GetWindowFlags(_window) & SdlNative.WindowInputFocus) != 0;

    /// <summary>Whether SDL says the window is hidden.</summary>
    public bool Hidden => _window == 0 || (SdlNative.GetWindowFlags(_window) & SdlNative.WindowHidden) != 0;

    public ScreenRect DefaultArea()
    {
        Start();
        SdlNative.Rect rect;
        return SdlNative.GetDisplayUsableBounds(SdlNative.GetPrimaryDisplay(), &rect)
            ? new ScreenRect(rect.X, rect.Y, rect.X + rect.W, rect.Y + rect.H)
            : new ScreenRect(0, 0, 1280, 800);
    }

    /// <summary>The whole main screen, in desktop units.</summary>
    public ScreenRect MainScreen()
    {
        Start();
        SdlNative.Rect rect;
        return _sdl.GetDisplayBounds(SdlNative.GetPrimaryDisplay(), &rect) != 0
            ? new ScreenRect(rect.X, rect.Y, rect.X + rect.W, rect.Y + rect.H)
            : default;
    }

    public double Open(ScreenRect bounds, bool clickThrough)
    {
        if (_window == 0)
        {
            Create(bounds);
        }

        SdlNative.SetWindowSize(_window, bounds.Width, bounds.Height);
        SdlNative.SetWindowPosition(_window, bounds.Left, bounds.Top);
        Volatile.Write(ref s_windowId, clickThrough ? 0 : SdlNative.GetWindowID(_window));
        if (_hook is not null && _native != 0)
        {
            _hook.ClickThrough(_native, clickThrough);
        }

        var density = (double)SdlNative.GetWindowPixelDensity(_window);
        if (!(density > 0))
        {
            density = 1;
        }

        int width = NotificationPicture.WidthAt(density), height = NotificationPicture.HeightAt(density);
        if (_texture == 0 || width != _textureWidth || height != _textureHeight)
        {
            if (_texture != 0)
            {
                _sdl.DestroyTexture(_texture);
            }

            _texture = _sdl.CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, TextureAccessStreaming, width, height);
            if (_texture == 0)
            {
                throw new InvalidOperationException("SDL made no texture for the notification: " + SdlNative.Error());
            }

            _sdl.SetTextureBlendMode(_texture, BlendPremultiplied);
            _textureWidth = width;
            _textureHeight = height;
        }

        return density;
    }

    public void Show(NotificationPicture picture, bool pictureChanged, (int SourceX, int DestinationX, int Width) slice)
    {
        if (_texture == 0)
        {
            return;
        }

        if (pictureChanged)
        {
            _sdl.UpdateTexture(_texture, null, picture.Pixels.Origin, picture.Width * sizeof(uint));
        }

        _sdl.SetRenderDrawColor(_renderer, 0, 0, 0, 0);
        _sdl.RenderClear(_renderer);
        var source = new FRect(slice.SourceX, 0, slice.Width, picture.Height);
        var destination = new FRect(slice.DestinationX, 0, slice.Width, picture.Height);
        _sdl.RenderTexture(_renderer, _texture, &source, &destination);
        _sdl.RenderPresent(_renderer);
        if (!_visible)
        {
            _visible = true;
            SdlNative.SetHint(HintActivateWhenShown, "0");
            SdlNative.ShowWindow(_window);
            _sdl.ResetHint(HintActivateWhenShown);
        }
    }

    public int TakeClicks()
    {
        if (_window == 0)
        {
            return 0;
        }

        _sdl.PumpEvents();
        return Interlocked.Exchange(ref s_clicks, 0);
    }

    public void Close()
    {
        if (_texture != 0)
        {
            _sdl.DestroyTexture(_texture);
            _texture = 0;
        }

        if (_renderer != 0)
        {
            _sdl.DestroyRenderer(_renderer);
            _renderer = 0;
        }

        if (_window != 0)
        {
            SdlNative.DestroyWindow(_window);
            _window = 0;
            _native = 0;
            _visible = false;
            Volatile.Write(ref s_windowId, 0);
            Interlocked.Exchange(ref s_clicks, 0);
        }

        if (_filterSet)
        {
            _filterSet = false;
            _sdl.SetEventFilter(s_previousFilter, s_previousData);
            s_previousFilter = 0;
            s_previousData = 0;
            _sdl.ResetHint(HintMouseFocusClickThrough);
        }

        // SDL counts the starts of each subsystem: video stops with the last of the app's windows.
        if (_started)
        {
            _started = false;
            SdlNative.QuitSubSystem(SdlNative.InitVideo);
        }
    }

    public void Dispose() => Close();

    private void Start()
    {
        if (_started)
        {
            return;
        }

        if (SdlNative.WasInit(SdlNative.InitVideo) == 0)
        {
            SdlNative.SetHint(SdlNative.HintAppId, SdlNative.AppId);
            SdlNative.SetHint(SdlNative.HintNoSignalHandlers, "1");
            SdlNative.SetHint(SdlNative.HintAllowScreensaver, "1");
            SdlNative.SetHint(SdlNative.HintVideoDriver, null);

            // Video started by the notification must not bring the app forward: the window the user is in keeps the focus.
            SdlNative.SetHint(HintMacBackgroundApp, "1");
        }

        if (!SdlNative.InitSubSystem(SdlNative.InitVideo))
        {
            throw new InvalidOperationException("SDL video did not start: " + SdlNative.Error());
        }

        _started = true;
    }

    private void Create(ScreenRect bounds)
    {
        Start();
        var flags = SdlNative.WindowHidden | SdlNative.WindowBorderless | WindowAlwaysOnTop | SdlNative.WindowHighPixelDensity
            | WindowTransparent | WindowNotFocusable;
        _window = SdlNative.CreateWindow("Hindsound notification", bounds.Width, bounds.Height, flags);
        if (_window == 0)
        {
            throw new InvalidOperationException("SDL made no window for the notification: " + SdlNative.Error());
        }

        _renderer = _sdl.CreateRenderer(_window, null);
        if (_renderer == 0)
        {
            var error = SdlNative.Error();
            Close();
            throw new InvalidOperationException("SDL made no renderer for the notification: " + error);
        }

        // The first click on the window of an app in the background is the click itself, not only a call to come forward.
        SdlNative.SetHint(HintMouseFocusClickThrough, "1");

        nint previousFilter, previousData;
        if (_sdl.GetEventFilter(&previousFilter, &previousData) == 0)
        {
            previousFilter = 0;
            previousData = 0;
        }

        s_previousFilter = previousFilter;
        s_previousData = previousData;
        _sdl.SetEventFilter((nint)(delegate* unmanaged<nint, SdlNative.Event*, byte>)&Filter, 0);
        _filterSet = true;

        if (_hook is not null)
        {
            _native = _sdl.GetPointerProperty(_sdl.GetWindowProperties(_window), _hook.NativeWindowProperty, 0);
            if (_native != 0)
            {
                _hook.Created(_native);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static byte Filter(nint data, SdlNative.Event* e)
    {
        try
        {
            // From the first window event to the last mouse event, every event carries its window at the same place.
            var window = e->Type is >= NotificationEvents.FirstWindowEvent and <= NotificationEvents.LastMouseEvent ? e->Window.WindowId : 0;
            if (!NotificationEvents.Keeps(e->Type, window, e->Button.Button, Volatile.Read(ref s_windowId), ref s_clicks))
            {
                return 0;
            }

            var previous = s_previousFilter;
            return previous == 0 ? (byte)1 : ((delegate* unmanaged<nint, SdlNative.Event*, byte>)previous)(s_previousData, e);
        }
        catch
        {
            // Nothing may cross back into SDL: the event goes on as if there were no filter.
            return 1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FRect(float x, float y, float w, float h)
    {
        public readonly float X = x, Y = y, W = w, H = h;
    }

    /// <summary>The calls of SDL 3.2 that SdlNative does not declare, taken from the library SdlLibrary loaded.</summary>
    private sealed class Api(nint library)
    {
        private readonly delegate* unmanaged<nint, byte*, nint> _createRenderer = (delegate* unmanaged<nint, byte*, nint>)Export(library, "SDL_CreateRenderer");
        private readonly delegate* unmanaged<nint, void> _destroyRenderer = (delegate* unmanaged<nint, void>)Export(library, "SDL_DestroyRenderer");
        private readonly delegate* unmanaged<nint, uint, int, int, int, nint> _createTexture = (delegate* unmanaged<nint, uint, int, int, int, nint>)Export(library, "SDL_CreateTexture");
        private readonly delegate* unmanaged<nint, void> _destroyTexture = (delegate* unmanaged<nint, void>)Export(library, "SDL_DestroyTexture");
        private readonly delegate* unmanaged<nint, SdlNative.Rect*, void*, int, byte> _updateTexture = (delegate* unmanaged<nint, SdlNative.Rect*, void*, int, byte>)Export(library, "SDL_UpdateTexture");
        private readonly delegate* unmanaged<nint, uint, byte> _setTextureBlendMode = (delegate* unmanaged<nint, uint, byte>)Export(library, "SDL_SetTextureBlendMode");
        private readonly delegate* unmanaged<nint, byte, byte, byte, byte, byte> _setRenderDrawColor = (delegate* unmanaged<nint, byte, byte, byte, byte, byte>)Export(library, "SDL_SetRenderDrawColor");
        private readonly delegate* unmanaged<nint, byte> _renderClear = (delegate* unmanaged<nint, byte>)Export(library, "SDL_RenderClear");
        private readonly delegate* unmanaged<nint, nint, FRect*, FRect*, byte> _renderTexture = (delegate* unmanaged<nint, nint, FRect*, FRect*, byte>)Export(library, "SDL_RenderTexture");
        private readonly delegate* unmanaged<nint, byte> _renderPresent = (delegate* unmanaged<nint, byte>)Export(library, "SDL_RenderPresent");
        private readonly delegate* unmanaged<nint, nint, void> _setEventFilter = (delegate* unmanaged<nint, nint, void>)Export(library, "SDL_SetEventFilter");
        private readonly delegate* unmanaged<nint*, nint*, byte> _getEventFilter = (delegate* unmanaged<nint*, nint*, byte>)Export(library, "SDL_GetEventFilter");
        private readonly delegate* unmanaged<void> _pumpEvents = (delegate* unmanaged<void>)Export(library, "SDL_PumpEvents");
        private readonly delegate* unmanaged<byte*, byte> _resetHint = (delegate* unmanaged<byte*, byte>)Export(library, "SDL_ResetHint");
        private readonly delegate* unmanaged<nint, uint> _getWindowProperties = (delegate* unmanaged<nint, uint>)Export(library, "SDL_GetWindowProperties");
        private readonly delegate* unmanaged<uint, byte*, nint, nint> _getPointerProperty = (delegate* unmanaged<uint, byte*, nint, nint>)Export(library, "SDL_GetPointerProperty");
        private readonly delegate* unmanaged<uint, SdlNative.Rect*, byte> _getDisplayBounds = (delegate* unmanaged<uint, SdlNative.Rect*, byte>)Export(library, "SDL_GetDisplayBounds");

        public nint CreateRenderer(nint window, string? name)
        {
            if (name is null)
            {
                return _createRenderer(window, null);
            }

            fixed (byte* utf8 = Utf8(name))
            {
                return _createRenderer(window, utf8);
            }
        }

        public void DestroyRenderer(nint renderer) => _destroyRenderer(renderer);

        public nint CreateTexture(nint renderer, uint format, int access, int w, int h) => _createTexture(renderer, format, access, w, h);

        public void DestroyTexture(nint texture) => _destroyTexture(texture);

        public bool UpdateTexture(nint texture, SdlNative.Rect* rect, void* pixels, int pitch) => _updateTexture(texture, rect, pixels, pitch) != 0;

        public bool SetTextureBlendMode(nint texture, uint mode) => _setTextureBlendMode(texture, mode) != 0;

        public bool SetRenderDrawColor(nint renderer, byte r, byte g, byte b, byte a) => _setRenderDrawColor(renderer, r, g, b, a) != 0;

        public bool RenderClear(nint renderer) => _renderClear(renderer) != 0;

        public bool RenderTexture(nint renderer, nint texture, FRect* source, FRect* destination) => _renderTexture(renderer, texture, source, destination) != 0;

        public bool RenderPresent(nint renderer) => _renderPresent(renderer) != 0;

        public void SetEventFilter(nint filter, nint data) => _setEventFilter(filter, data);

        public byte GetEventFilter(nint* filter, nint* data) => _getEventFilter(filter, data);

        public void PumpEvents() => _pumpEvents();

        public void ResetHint(string name)
        {
            fixed (byte* utf8 = Utf8(name))
            {
                _resetHint(utf8);
            }
        }

        public uint GetWindowProperties(nint window) => _getWindowProperties(window);

        public nint GetPointerProperty(uint properties, string name, nint fallback)
        {
            fixed (byte* utf8 = Utf8(name))
            {
                return _getPointerProperty(properties, utf8, fallback);
            }
        }

        public byte GetDisplayBounds(uint display, SdlNative.Rect* rect) => _getDisplayBounds(display, rect);

        private static nint Export(nint library, string name) => NativeLibrary.GetExport(library, name);

        private static byte[] Utf8(string value) => System.Text.Encoding.UTF8.GetBytes(value + "\0");
    }
}
