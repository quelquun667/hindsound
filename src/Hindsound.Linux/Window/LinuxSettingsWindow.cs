using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Linux.App;
using Hindsound.Linux.Window.Input;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.App;

namespace Hindsound.Linux.Window;

/// <summary>
/// The window of the settings and the Library on Linux: one SDL window around one bitmap the window owns, drawn by the same
/// portable pages as on Windows, with the text field drawn over it. It lives only while it is open: closing destroys the
/// window, frees the bitmap and stops its timers, and when it was the last window SDL's video stops too.
/// </summary>
/// <remarks>
/// Everything runs on the loop thread. While the window is open, the loop waits in the window's event queue, so events and
/// the loop's actions take turns. Paints are put off to the loop, as WM_PAINT is on Windows: many asks make one paint.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxSettingsWindow : IWindowHost, ISettingsWindow
{
    public const int MeterIntervalMs = 100;
    public const int WaveIntervalMs = 100;

    /// <summary>The side of the window's icon, in pixels at a display scale of 1.</summary>
    public const int IconSide = 64;

    /// <summary>The screen an off-screen window stands for, as on Windows: 1920 by 1080, at a scale of 1.</summary>
    public static readonly ScreenRect OffscreenArea = new(0, 0, 1920, 1080);

    private static int s_windows;
    private static int s_bitmaps;

    private readonly UnixLoop _loop;
    private readonly ISdlVideo _video;
    private readonly Func<ICanvasText> _newText;
    private readonly Func<float> _readMicPeak;
    private readonly bool _offscreen;
    private readonly LinuxTextField _field;
    private readonly ILoopPump _pump;

    private PixelCanvas _canvas;
    private uint* _bits;
    private int _width, _height;
    private float _displayScale = 1;
    private float _density = 1;
    private float? _probeScale;
    private WindowBorders _borders;
    private int _wantedZoom;
    private int _zoom = WindowZoomFit.Floor;
    private uint? _iconAccent;
    private int _iconSide;

    private bool _dirty = true;
    private bool _fieldDirty;
    private (int X, int Y, int W, int H) _pending;
    private bool _paintPosted;
    private bool _meterOn, _waveOn, _blinkOn;
    private int _meterRun, _waveRun, _blinkRun;
    private int _wheelRest;
    private bool _capturing;
    private bool _picking;
    private bool _closeAfterPick;
    private bool _hiddenByDesktop;
    private bool _frameRead;
    private bool _closed;

    /// <summary>Opens SDL's video and makes the window, hidden until <see cref="Show"/>.</summary>
    /// <param name="newText">Makes the text of a bitmap: each bitmap gets its own, and frees it with itself.</param>
    public LinuxSettingsWindow(SettingsWindowRequest request, UnixLoop loop, Func<ICanvasText> newText, ISdlVideo video)
    {
        Closed = request.Closed;
        _loop = loop;
        _video = video;
        _newText = newText;
        _readMicPeak = request.ReadMicPeak;
        _offscreen = request.Offscreen;
        _wantedZoom = request.Settings.General.WindowZoom;

        _video.Start(_offscreen);
        try
        {
            var (area, scale) = _offscreen ? (OffscreenArea, 1f) : _video.ScreenUnderMouse();
            _displayScale = scale;
            _zoom = ChooseZoom(area);
            var size = SdlWindowSizing.WindowSize(Scale, _density);
            var at = SdlWindowSizing.Centered(area, SdlWindowSizing.OuterSize(Scale, _density, _borders), _borders);
            _video.Create("Hindsound", size.W, size.H, at.X, at.Y);

            // The window may have landed on a screen of another scale than the one it was sized for, and only now does it
            // know its pixel density and its frame.
            if (!_offscreen)
            {
                _displayScale = _video.DisplayScale;
                _density = _video.PixelDensity;
                _borders = _video.Borders;
                area = _video.UsableArea();
                _zoom = ChooseZoom(area);
                var real = SdlWindowSizing.WindowSize(Scale, _density);
                if (real != size)
                {
                    at = SdlWindowSizing.Centered(area, SdlWindowSizing.OuterSize(Scale, _density, _borders), _borders);
                    _video.Place(at.X, at.Y, real.W, real.H);
                }
            }

            _canvas = NewCanvas(out _bits, out _width, out _height);
            _field = new LinuxTextField(_video.ClipboardText, _video.SetClipboardText) { Repaint = FieldRepaint };
            _pump = _video.NewPump(Handle);
            _loop.Pump = _pump;
            Screen = new SettingsScreen(request.Settings, request.Backend, this, _canvas, request.FirstRun);
            _field.Changed = Screen.EditChanged;
        }
        catch
        {
            // A window that failed to open leaves nothing: not the loop's wait, the window, SDL's video nor the bitmap.
            if (_pump is not null && ReferenceEquals(_loop.Pump, _pump))
            {
                _loop.Pump = null;
            }

            _video.Close();
            _canvas?.Dispose();
            FreeBitmap(_bits);
            throw;
        }

        Interlocked.Increment(ref s_windows);
    }

    /// <summary>Opens a window on SDL itself.</summary>
    public static LinuxSettingsWindow Open(SettingsWindowRequest request, UnixLoop loop, Func<ICanvasText> newText) =>
        new(request, loop, newText, new SdlVideo());

    /// <summary>The windows open in this process, for <c>--settings-probe</c>.</summary>
    public static int OpenWindows => Volatile.Read(ref s_windows);

    /// <summary>The bitmaps of windows not freed yet, for <c>--settings-probe</c>.</summary>
    public static int LiveBitmaps => Volatile.Read(ref s_bitmaps);

    public SettingsScreen Screen { get; }

    public Action Closed { get; }

    public Action? Activated { get; set; }

    /// <summary>The window size this screen holds, in percent.</summary>
    public int AppliedZoom => _zoom;

    /// <summary>What a design pixel is in pixels now: the display scale, then the zoom.</summary>
    internal float Scale => (float)SdlWindowSizing.Scale(_displayScale, _zoom);

    internal LinuxTextField Field => _field;

    internal bool IsClosed => _closed;

    /// <summary>The bitmap the window draws into, null once the window is closed.</summary>
    internal nint Bitmap => (nint)_bits;

    internal (int W, int H) BitmapSize => (_width, _height);

    internal int IconSideNow => _iconSide;

    /// <summary>The id SDL gives the window, which its events carry.</summary>
    internal uint WindowId => _video.WindowId;

    public void Show()
    {
        // Off screen the window is in memory only: never shown, so it never takes the focus.
        if (!_offscreen)
        {
            _video.Show();
        }

        Invalidate();
    }

    public void BringBack()
    {
        if (!_offscreen && !_closed)
        {
            _video.BringBack();
        }
    }

    /// <summary>As the close button: on the loop, after what it is doing now.</summary>
    public void Close() => _loop.Post(CloseNow);

    public void CloseWindow() => Close();

    public void SetIcon(uint accent)
    {
        if (_closed)
        {
            return;
        }

        _iconAccent = accent;
        _iconSide = (int)Math.Round(IconSide * _displayScale);
        _video.SetIcon(TrayIconShape.Compose(TrayState.Active, _iconSide, accent), _iconSide);
    }

    public void ProbePicture(string path)
    {
        if (_dirty || _fieldDirty)
        {
            PaintNow();
        }

        PngFile.Write(path, _bits, _width, _height);
    }

    public string ClientSizeText()
    {
        var (w, h) = _video.PixelSize;
        return $"{w}x{h}, bitmap {_width}x{_height}";
    }

    /// <summary>What the desktop would say on a move to a screen of <paramref name="dpi"/>: <c>--settings-probe</c> goes through the same code.</summary>
    public void ProbeScaleChange(uint dpi)
    {
        _probeScale = dpi / 96f;
        Rescale();
    }

    // ---------------------------------------------------------------- what the screen asks

    public void Invalidate()
    {
        if (_closed)
        {
            return;
        }

        _dirty = true;
        SchedulePaint();
    }

    public void InvalidateArea(Box area)
    {
        if (_closed)
        {
            return;
        }

        _pending = SdlWindowSizing.Union(_pending, SdlWindowSizing.PixelArea(area, Scale, _width, _height));
        SchedulePaint();
    }

    public void ShowEdit(Box box, string text, Face face, float size, bool resetText) => _field.Show(box, text, face, size, resetText);

    /// <summary>The command of the shortcuts, put on the clipboard as text by the Copy button of the page.</summary>
    public void CopyText(string text) => _video.SetClipboardText(text);

    public void HideEdit()
    {
        _field.Hide();
        UpdateTextInput();
    }

    public bool IsForeground => !_offscreen && !_closed && _video.HasFocus;

    public void FocusEdit()
    {
        if (_field.Visible)
        {
            _field.Focus();
            AfterInput();
        }
    }

    public void CaptureMouse()
    {
        _capturing = true;
        _video.CaptureMouse(true);
    }

    public void ReleaseMouse()
    {
        _capturing = false;
        _video.CaptureMouse(false);
    }

    public void MeterWanted(bool wanted)
    {
        if (wanted == _meterOn || _closed)
        {
            return;
        }

        _meterOn = wanted;
        var run = ++_meterRun;
        if (wanted)
        {
            Every(MeterIntervalMs, () => _meterOn && run == _meterRun, () => Screen.MeterTick(_readMicPeak()));
        }
    }

    public void WaveWanted(bool wanted)
    {
        if (wanted == _waveOn || _closed)
        {
            return;
        }

        _waveOn = wanted;
        var run = ++_waveRun;
        if (wanted)
        {
            Every(WaveIntervalMs, () => _waveOn && run == _waveRun, Screen.WaveTick);
        }
    }

    public string? PickFolder(string startFolder)
    {
        // The dialog is the desktop's: while it is open, the window's own input waits, as behind a modal dialog on Windows.
        // The loop goes on all the same (a save asked by the hotkey, the paints, the timers), as the dialog's own loop
        // dispatches the messages of the loop on Windows.
        _picking = true;
        try
        {
            return _video.PickFolder(startFolder, () => _loop.RunOnce(50));
        }
        finally
        {
            _picking = false;
            if (_closeAfterPick)
            {
                _closeAfterPick = false;
                _loop.Post(CloseNow);
            }
        }
    }

    // ---------------------------------------------------------------- events

    /// <summary>One event of SDL's queue, on the loop thread.</summary>
    internal void Handle(in SdlNative.Event e)
    {
        if (_closed)
        {
            return;
        }

        var type = e.Type;
        if (type is >= SdlNative.EventDisplayOrientation and <= SdlNative.EventDisplayContentScaleChanged)
        {
            Rescale();
            return;
        }

        // Window, keyboard and mouse events all carry the window's id at the same place.
        if (type is < SdlNative.EventWindowShown or > 0x4FF || e.Window.WindowId != _video.WindowId)
        {
            return;
        }

        if (_picking && type >= SdlNative.EventKeyDown)
        {
            return;
        }

        switch (type)
        {
            case SdlNative.EventWindowShown:
            case SdlNative.EventWindowExposed:
                if (type == SdlNative.EventWindowShown)
                {
                    FrameKnown();
                }

                // Back from what the desktop hid it for: the paints and the timers go on.
                if (_hiddenByDesktop)
                {
                    _hiddenByDesktop = false;
                    Screen.WindowMinimized(false);
                }

                PresentAll();
                break;

            // Wayland has no minimized state: a minimized window is only told it is suspended, which SDL reports as occluded.
            // Nobody sees the window then, as when it is minimized: no paint, no wave, no meter.
            case SdlNative.EventWindowOccluded:
            case SdlNative.EventWindowHidden:
                _hiddenByDesktop = true;
                Screen.WindowMinimized(true);
                break;

            case SdlNative.EventWindowMinimized:
                Screen.WindowMinimized(true);
                break;

            case SdlNative.EventWindowRestored:
            case SdlNative.EventWindowMaximized:
                Screen.WindowMinimized(false);

                // A size asked for while the window was minimized waits for its way back.
                ZoomChanged(_wantedZoom);
                PresentAll();
                break;

            case SdlNative.EventWindowMouseLeave:
                Screen.MouseLeft();
                break;

            case SdlNative.EventWindowFocusGained:
                Activated?.Invoke();
                if (_field.Visible)
                {
                    _field.Focus();
                }

                break;

            case SdlNative.EventWindowFocusLost:
                _field.Blur();
                if (_capturing)
                {
                    _capturing = false;
                    Screen.MouseCaptureLost();
                }

                Screen.Deactivated();
                break;

            case SdlNative.EventWindowCloseRequested:
                // Not inside the wait of the queue: the window goes once the wait is over.
                Close();
                break;

            case SdlNative.EventWindowDisplayChanged:
            case SdlNative.EventWindowDisplayScaleChanged:
            case SdlNative.EventWindowPixelSizeChanged:
                Rescale();
                break;

            case SdlNative.EventKeyDown:
                KeyDown(e.Key);
                break;

            case SdlNative.EventKeyUp:
                Screen.KeyUp(SdlKeys.VirtualKey((uint)e.Key.Scancode, e.Key.Key, e.Key.Mod), SdlKeys.Modifiers(e.Key.Mod));
                break;

            case SdlNative.EventTextInput:
                _field.TextInput(e.Text.TextString);
                break;

            case SdlNative.EventTextEditing:
                _field.Composition(e.Edit.TextString, e.Edit.Start);
                break;

            case SdlNative.EventMouseMotion:
                var mx = Design(e.Motion.X);
                if (!_field.MouseMove(mx))
                {
                    Screen.MouseMove(mx, Design(e.Motion.Y));
                }

                break;

            case SdlNative.EventMouseButtonDown:
                MouseDown(e.Button);
                break;

            case SdlNative.EventMouseButtonUp:
                if (e.Button.Button == SdlNative.ButtonLeft)
                {
                    _field.MouseUp();
                    Screen.MouseUp(Design(e.Button.X), Design(e.Button.Y));
                }

                break;

            case SdlNative.EventMouseWheel:
                Wheel(e.Wheel);
                break;
        }

        AfterInput();
    }

    private void KeyDown(in SdlNative.KeyboardEvent key)
    {
        var vk = SdlKeys.VirtualKey((uint)key.Scancode, key.Key, key.Mod);
        var modifiers = SdlKeys.Modifiers(key.Mod);
        if (vk == 0 || _field.KeyDown(vk, modifiers))
        {
            return;
        }

        var fieldWasHidden = !_field.Visible;
        var used = Screen.Key(vk, modifiers, key.Repeat);

        // This key brought a scrolled-away field back. Text input was off when it went down, so SDL sent no text for it:
        // the field gets the character of the key, as the box gets its WM_CHAR on Windows.
        // With Shift or Caps Lock on, the layout says what the key types ('!' for Shift and 1): not the key's own character.
        var layoutKey = (key.Mod & (SdlKeys.ModLShift | SdlKeys.ModRShift | SdlKeys.ModCaps)) != 0
            ? _video.KeyFromScancode((uint)key.Scancode, key.Mod)
            : 0u;
        if (!used && fieldWasHidden && _field.Visible && SdlWindowSizing.TypedText(key.Key, modifiers, layoutKey) is { } text)
        {
            var hadFocus = _field.Focused;
            _field.Focus();
            _field.TextInput(text);
            if (!hadFocus && !IsForeground)
            {
                _field.Blur();
            }
        }
    }

    private void MouseDown(in SdlNative.MouseButtonEvent button)
    {
        var x = Design(button.X);
        var y = Design(button.Y);
        if (button.Button == SdlNative.ButtonRight)
        {
            Screen.RightClick(x, y);
            return;
        }

        if (button.Button != SdlNative.ButtonLeft)
        {
            return;
        }

        var shift = !_offscreen && (SdlKeys.Modifiers(_video.ModState()) & HotkeyGesture.ModShift) != 0;
        if (_field.MouseDown(x, y, shift, button.Clicks))
        {
            return;
        }

        // Windows makes every second press of a quick run a double click, and the press after it a plain one again.
        if (button.Clicks >= 2 && button.Clicks % 2 == 0)
        {
            Screen.MouseDoubleClick(x, y);
        }
        else
        {
            Screen.MouseDown(x, y);
        }
    }

    private void Wheel(in SdlNative.MouseWheelEvent wheel)
    {
        var y = wheel.Direction == SdlNative.MouseWheelFlipped ? -wheel.Y : wheel.Y;
        (var notches, _wheelRest) = WheelNotches.Add(_wheelRest, (int)MathF.Round(y * WheelNotches.PerNotch));
        if (notches != 0)
        {
            Screen.Wheel(notches, Design(wheel.MouseX), Design(wheel.MouseY));
        }
    }

    private float Design(float windowCoordinate) => SdlWindowSizing.ToDesign(windowCoordinate, _density, Scale);

    // After each event: the input method follows the field, and the caret blinks while the field has the keyboard.
    private void AfterInput()
    {
        if (_closed)
        {
            return;
        }

        UpdateTextInput();
        if (_field.WantsTextInput && !_blinkOn)
        {
            _blinkOn = true;
            var run = ++_blinkRun;
            Every(LinuxTextField.BlinkMilliseconds, () => run == _blinkRun && (_blinkOn = _field.WantsTextInput), _field.Blink);
        }
    }

    private void UpdateTextInput()
    {
        if (_closed)
        {
            return;
        }

        // The field gives pixels; SDL wants window coordinates.
        var (x, y, w, h, cursor) = _field.InputArea();
        var d = _density > 0 ? _density : 1;
        _video.TextInput(_field.WantsTextInput, (int)(x / d), (int)(y / d), (int)Math.Ceiling(w / d), (int)Math.Ceiling(h / d), (int)(cursor / d));
    }

    // A timer of the loop that runs while it is still wanted; a stopped one dies at its next turn.
    private void Every(int milliseconds, Func<bool> wanted, Action tick)
    {
        void Turn()
        {
            if (_closed || !wanted())
            {
                return;
            }

            tick();
            _loop.After(milliseconds, Turn);
        }

        _loop.After(milliseconds, Turn);
    }

    // ---------------------------------------------------------------- paint

    private void FieldRepaint()
    {
        if (_closed)
        {
            return;
        }

        // A field that went away leaves what was under it: only the page can draw that again.
        if (_field.Visible)
        {
            _fieldDirty = true;
        }
        else
        {
            _dirty = true;
        }

        SchedulePaint();
    }

    private void PresentAll()
    {
        _pending = (0, 0, _width, _height);
        SchedulePaint();
    }

    private void SchedulePaint()
    {
        if (_paintPosted || _closed)
        {
            return;
        }

        _paintPosted = true;
        _loop.Post(PaintNow);
    }

    /// <summary>What WM_PAINT does on Windows: draws the page if it changed, then puts what changed on the window.</summary>
    internal void PaintNow()
    {
        _paintPosted = false;

        // Drawing the page for nobody costs a full paint at each news: the page stays dirty, and the way back asks for it.
        if (_closed || Screen.Minimized)
        {
            return;
        }

        var area = _pending;
        _pending = default;
        if (_dirty)
        {
            _dirty = false;
            _fieldDirty = false;
            Screen.Paint(_canvas);
            _field.Paint(_canvas);
            area = (0, 0, _width, _height);
        }
        else if (_fieldDirty)
        {
            _fieldDirty = false;
            _field.Paint(_canvas);
            var (x, y, w, h, _) = _field.InputArea();
            area = SdlWindowSizing.Union(area, (Math.Max(0, x - 1), Math.Max(0, y - 1), Math.Min(_width, x + w + 1) - Math.Max(0, x - 1), Math.Min(_height, y + h + 1) - Math.Max(0, y - 1)));
        }

        _video.Present(_bits, _width, _height, area);
        UpdateTextInput();
    }

    // ---------------------------------------------------------------- size and scale

    /// <summary>The size of the window changed in the settings: the largest zoom its screen can hold, up to that one, around the same centre.</summary>
    public void ZoomChanged(int percent)
    {
        if (_closed)
        {
            return;
        }

        _wantedZoom = percent;
        var zoom = ChooseZoom(AreaNow());
        if (zoom == _zoom || Screen.Minimized)
        {
            return;
        }

        _zoom = zoom;
        Resized();
    }

    // The desktop says the scale may have changed: the window takes the new one, and the zoom is chosen again for that screen.
    private void Rescale()
    {
        if (_closed)
        {
            return;
        }

        var scale = _probeScale ?? (_offscreen ? 1f : _video.DisplayScale);
        var density = _offscreen ? 1f : _video.PixelDensity;
        var zoom = ChooseZoom(AreaNow(), scale, density);
        if (scale == _displayScale && density == _density && zoom == _zoom)
        {
            return;
        }

        _displayScale = scale;
        _density = density;
        _zoom = zoom;
        Resized();
    }

    // X11 learns the size of the title bar when the window is mapped, after the size and the zoom were chosen without it: the
    // first time the window is shown, they are chosen again with it.
    private void FrameKnown()
    {
        if (_frameRead || _offscreen)
        {
            return;
        }

        _frameRead = true;
        var borders = _video.Borders;
        if (borders == _borders)
        {
            return;
        }

        _borders = borders;
        _zoom = ChooseZoom(AreaNow());
        Resized();
    }

    private ScreenRect AreaNow() => _offscreen ? OffscreenArea : _video.UsableArea();

    private int ChooseZoom(ScreenRect area) => ChooseZoom(area, _displayScale, _density);

    private int ChooseZoom(ScreenRect area, float scale, float density) =>
        SdlWindowSizing.ChooseZoom(_wantedZoom, scale, density, _borders, area);

    // The window takes its new size around the same centre: a new bitmap, the field, the icon follow, and the page is drawn again.
    private void Resized()
    {
        var oldCanvas = _canvas;
        var oldBits = _bits;
        _canvas = NewCanvas(out _bits, out _width, out _height);
        Screen.SetCanvas(_canvas);
        oldCanvas.Dispose();
        FreeBitmap(oldBits);

        var now = _video.Bounds;
        var size = SdlWindowSizing.WindowSize(Scale, _density);
        var outer = SdlWindowSizing.OuterSize(Scale, _density, _borders);
        var at = SdlWindowSizing.Fit(
            now.X - _borders.Left + (now.W + _borders.Left + _borders.Right) / 2 - outer.W / 2,
            now.Y - _borders.Top + (now.H + _borders.Top + _borders.Bottom) / 2 - outer.H / 2,
            outer, AreaNow(), _borders);
        _video.Place(at.X, at.Y, size.W, size.H);
        if (_iconAccent is { } accent)
        {
            SetIcon(accent);
        }

        Invalidate();
    }

    private PixelCanvas NewCanvas(out uint* bits, out int width, out int height)
    {
        var scale = SdlWindowSizing.Scale(_displayScale, _zoom);
        (width, height) = SdlWindowSizing.PixelSize(scale);
        bits = (uint*)NativeMemory.AllocZeroed((nuint)width * (nuint)height * sizeof(uint));
        Interlocked.Increment(ref s_bitmaps);
        try
        {
            return new PixelCanvas(bits, width, height, scale, _newText());
        }
        catch
        {
            FreeBitmap(bits);
            throw;
        }
    }

    private static void FreeBitmap(uint* bits)
    {
        if (bits is not null)
        {
            NativeMemory.Free(bits);
            Interlocked.Decrement(ref s_bitmaps);
        }
    }

    // ---------------------------------------------------------------- close

    private void CloseNow()
    {
        if (_closed)
        {
            return;
        }

        // Not under the dialog of the desktop: the window goes once it is answered.
        if (_picking)
        {
            _closeAfterPick = true;
            return;
        }

        Screen.Closing();
        Release();
    }

    // Nothing of the window outlives it: timers, the loop's wait, the window, the bitmap and its text.
    private void Release()
    {
        _closed = true;
        _meterOn = _waveOn = _blinkOn = false;
        _meterRun++;
        _waveRun++;
        _blinkRun++;
        if (ReferenceEquals(_loop.Pump, _pump))
        {
            _loop.Pump = null;
        }

        _video.Close();
        _canvas.Dispose();
        FreeBitmap(_bits);
        _bits = null;
        Interlocked.Decrement(ref s_windows);
        Closed();
    }

    public void Dispose() => CloseNow();

    // ---------------------------------------------------------------- probe

    /// <summary>A key of the probe, pushed to this window's own queue as SDL would: never through the real keyboard.</summary>
    internal bool PushKey(uint scancode, uint keycode)
    {
        var e = default(SdlNative.Event);
        e.Key.Type = SdlNative.EventKeyDown;
        e.Key.WindowId = _video.WindowId;
        e.Key.Scancode = (int)scancode;
        e.Key.Key = keycode;
        e.Key.DownByte = 1;
        return _video.Push(e);
    }

    /// <summary>A window event of the probe, pushed to this window's own queue.</summary>
    internal bool PushWindowEvent(uint type)
    {
        var e = default(SdlNative.Event);
        e.Window.Type = type;
        e.Window.WindowId = _video.WindowId;
        return _video.Push(e);
    }

    /// <summary>A key the probe sends to the text field, as a key posted to the box on Windows: whether or not it has the keyboard.</summary>
    internal void KeyInField(uint vk) => _loop.Post(() =>
    {
        if (_closed)
        {
            return;
        }

        WithField(() =>
        {
            if (!_field.KeyDown(vk, 0))
            {
                Screen.Key(vk, 0);
            }
        });
        AfterInput();
    });

    /// <summary>The probe replaces the text of the field, as typing all of it would.</summary>
    internal void TypeInField(string text)
    {
        if (_closed || !_field.Visible)
        {
            return;
        }

        WithField(() =>
        {
            _field.Model.SelectAll();
            if (text.Length == 0)
            {
                _field.KeyDown(Vk.Back, 0);
            }
            else
            {
                _field.TextInput(text);
            }
        });
    }

    // The field takes a key or a text it was sent, then gives the keyboard back if it did not have it.
    private void WithField(Action action)
    {
        var hadFocus = _field.Focused;
        if (_field.Visible && !hadFocus)
        {
            _field.Focus();
        }

        action();
        if (!hadFocus && _field.Visible)
        {
            _field.Blur();
        }
    }
}
