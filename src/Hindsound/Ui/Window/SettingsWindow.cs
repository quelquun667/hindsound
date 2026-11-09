using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Ui.Window;

internal static unsafe partial class WindowNative
{
    public const uint WM_SETICON = 0x0080, WM_GETICON = 0x007F, WM_DESTROY = 0x0002, WM_SIZE = 0x0005, SIZE_MINIMIZED = 1, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_ACTIVATE = 0x0006,
        WM_SETFONT = 0x0030, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_SYSCHAR = 0x0106,
        WM_COMMAND = 0x0111, WM_CTLCOLOREDIT = 0x0133, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEWHEEL = 0x020A,
        WM_CAPTURECHANGED = 0x0215, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0, EM_SETSEL = 0x00B1, EM_SETLIMITTEXT = 0x00C5, EN_CHANGE = 0x0300, TME_LEAVE = 0x0002;

    public const uint WS_OVERLAPPED = 0, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_CLIPCHILDREN = 0x02000000,
        WS_CHILD = 0x40000000, ES_AUTOHSCROLL = 0x0080, SRCCOPY = 0x00CC0020, SWP_NOZORDER = 0x0004;

    public const int SW_SHOW = 5, SW_SHOWNOACTIVATE = 4, SW_RESTORE = 9, GWLP_WNDPROC = -4, IDC_ARROW = 32512, WA_INACTIVE = 0;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_CAPTION_COLOR = 35;
    public const uint CLSCTX_INPROC_SERVER = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint Hdc;
        public int Erase;
        public Native.RECT Paint;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint Size, Flags;
        public nint Target;
        public uint Hover;
    }

    private const string U = "user32.dll", G = "gdi32.dll";

    [LibraryImport(U)] public static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* paint);
    [LibraryImport(U)] public static partial int EndPaint(nint hwnd, PAINTSTRUCT* paint);
    [LibraryImport(U)] public static partial int InvalidateRect(nint hwnd, Native.RECT* rect, int erase);
    [LibraryImport(U)] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport(U)] public static partial int AdjustWindowRectExForDpi(Native.RECT* rect, uint style, int menu, uint exStyle, uint dpi);
    [LibraryImport(U)] public static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport(U)] public static partial nint SetFocus(nint hwnd);
    [LibraryImport(U)] public static partial short GetKeyState(int key);
    [LibraryImport(U)] public static partial nint SetCapture(nint hwnd);
    [LibraryImport(U)] public static partial int ReleaseCapture();
    [LibraryImport(U)] public static partial int TrackMouseEvent(TRACKMOUSEEVENT* track);
    [LibraryImport(U)] public static partial nint SendMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [LibraryImport(U, StringMarshalling = StringMarshalling.Utf16)] public static partial int SetWindowTextW(nint hwnd, string text);
    [LibraryImport(U)] public static partial int GetWindowTextLengthW(nint hwnd);
    [LibraryImport(U)] public static partial nint CallWindowProcW(nint proc, nint hwnd, uint msg, nuint wParam, nint lParam);
    [LibraryImport(U)] public static partial int GetClientRect(nint hwnd, Native.RECT* rect);
    [LibraryImport(U)] public static partial uint GetGuiResources(nint process, uint flags);
    [LibraryImport(U)] public static partial int PrintWindow(nint hwnd, nint dc, uint flags);
    [LibraryImport(G)] public static partial int BitBlt(nint dest, int x, int y, int w, int h, nint source, int sx, int sy, uint op);
    [LibraryImport(G)] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport(G)] public static partial uint SetBkColor(nint dc, uint color);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, void* value, int size);
    [LibraryImport("ole32.dll")] public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
}

/// <summary>
/// The window itself: a native frame in dark mode around one bitmap, and one child EDIT that moves to whichever text
/// field has the focus. It lives only while it is open: closing destroys it, with its bitmap, its fonts and its timer.
/// </summary>
internal sealed unsafe class SettingsWindow : IWindowHost, IDisposable
{
    private const string ClassName = "HindsoundSettings";
    private const uint Style = WindowNative.WS_OVERLAPPED | WindowNative.WS_CAPTION | WindowNative.WS_SYSMENU | WindowNative.WS_MINIMIZEBOX | WindowNative.WS_CLIPCHILDREN;
    private const nuint MeterTimer = 1;
    private const uint MeterIntervalMs = 100;
    private const nuint WaveTimer = 2;
    private const uint WaveIntervalMs = 100;
    private const nint EditId = 100;

    // A window gets its messages on the thread that made it: one on another thread must not take them from it.
    [ThreadStatic]
    private static SettingsWindow? _current;
    private static bool _classRegistered;

    private readonly bool _offscreen;
    private readonly Func<float> _readMicPeak;
    private readonly nint _editBrush;

    private nint _hwnd;
    private nint _edit;
    private static nint _editOriginal;
    private nint _editFont;
    private nint _smallIcon, _bigIcon;
    private uint? _iconAccent;
    private GdiCanvas _canvas;
    private uint _dpi;
    private bool _dirty = true;
    private bool _settingEditText;
    private bool _trackingLeave;
    private bool _meterOn;
    private bool _waveOn;
    private bool _editVisible;
    private readonly CaptureKeys _captureKeys = new();
    private bool _charForEdit;
    private int _wheelRest;
    private (Face Face, float Size, uint Dpi) _editFontKey;
    private Box _editBox;
    private float _editSize;
    private bool _closed;

    /// <summary>On the screen under the mouse, or off screen, where no screen counts.</summary>
    public SettingsWindow(AppSettings settings, ISettingsBackend backend, Func<float> readMicPeak, bool firstRun, bool offscreen, Action closed)
        : this(settings, backend, readMicPeak, firstRun, offscreen, offscreen ? default : ForegroundWindow.CursorScreen(), closed)
    {
    }

    public SettingsWindow(AppSettings settings, ISettingsBackend backend, Func<float> readMicPeak, bool firstRun, bool offscreen, ScreenInfo screen, Action closed)
    {
        Closed = closed;
        _offscreen = offscreen;
        _readMicPeak = readMicPeak;
        _current = this;
        var instance = Native.GetModuleHandleW(null);
        RegisterClass(instance);

        var (area, dpi) = offscreen ? OffscreenArea() : (screen.Work, screen.Dpi);
        _dpi = dpi;
        var size = OuterSize(dpi);
        var exStyle = offscreen ? Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW : 0;
        _hwnd = Native.CreateWindowExW(exStyle, ClassName, "Hindsound", Style, area.Left + (area.Width - size.Cx) / 2, area.Top + (area.Height - size.Cy) / 2, size.Cx, size.Cy, 0, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed");
        }

        // The window may have landed on a monitor of another DPI than the one it was sized for.
        var real = WindowNative.GetDpiForWindow(_hwnd);
        if (real != 0 && real != _dpi)
        {
            _dpi = real;
            size = OuterSize(real);
            Native.SetWindowPos(_hwnd, 0, 0, 0, size.Cx, size.Cy, Native.SWP_NOMOVE | WindowNative.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        DarkFrame();
        _editBrush = WindowNative.CreateSolidBrush(ColorRef(Theme.Raised));
        _canvas = NewCanvas();
        CreateEdit(instance);
        Screen = new SettingsScreen(settings, backend, this, _canvas, firstRun);
    }

    public SettingsScreen Screen { get; }

    public nint Hwnd => _hwnd;

    public Action Closed { get; }

    /// <summary>The window came back to the foreground.</summary>
    public Action? Activated { get; set; }

    /// <summary>Shows the window. Off screen for a test: never activated, no taskbar button, never takes the focus.</summary>
    public void Show()
    {
        if (_offscreen)
        {
            Native.ShowWindow(_hwnd, WindowNative.SW_SHOWNOACTIVATE);
            return;
        }

        Native.ShowWindow(_hwnd, WindowNative.SW_SHOW);
        Native.SetForegroundWindow(_hwnd);
    }

    /// <summary>The menu asked for it again while it is open.</summary>
    public void BringBack()
    {
        if (!_offscreen)
        {
            Native.ShowWindow(_hwnd, WindowNative.SW_RESTORE);
            Native.SetForegroundWindow(_hwnd);
        }
    }

    public void Close() => Native.PostMessageW(_hwnd, Native.WM_CLOSE, 0, 0);

    public nint EditHwnd => _edit;

    /// <summary>What Windows sends when the window moves to a screen of another DPI: <c>--settings-probe</c> goes through the same code.</summary>
    public void ProbeDpiChange(uint dpi)
    {
        var size = OuterSize(dpi);
        Native.RECT now;
        Native.GetWindowRect(_hwnd, &now);
        Native.RECT rect = new() { Left = now.Left, Top = now.Top, Right = now.Left + size.Cx, Bottom = now.Top + size.Cy };
        WindowNative.SendMessageW(_hwnd, WindowNative.WM_DPICHANGED, dpi | ((nuint)dpi << 16), (nint)(&rect));
    }

    /// <summary>What Windows would show, children included, from the window itself: for <c>--settings-probe</c>, which never shows it.</summary>
    public void ProbePicture(string path)
    {
        const uint clientOnlyFullContent = 3;
        using var picture = new GdiCanvas(_canvas.Width, _canvas.Height, Scale);
        WindowNative.PrintWindow(_hwnd, picture.Dc, clientOnlyFullContent);
        PngFile.Write(path, picture.Bits, picture.Width, picture.Height);
    }

    public string ClientSizeText()
    {
        Native.RECT client;
        WindowNative.GetClientRect(_hwnd, &client);
        return $"{client.Width}x{client.Height}, bitmap {_canvas.Width}x{_canvas.Height}";
    }

    /// <summary>The icons of the title bar: the same mark as the tray, in the accent colour.</summary>
    public void SetIcon(uint accent)
    {
        const nuint small = 0, big = 1;
        _iconAccent = accent;
        var smallIcon = TrayIcon.CreateIcon(TrayState.Active, accent, TrayIcon.SmallSizeAt(_dpi));
        var bigIcon = TrayIcon.CreateIcon(TrayState.Active, accent, TrayIcon.BigSizeAt(_dpi));
        WindowNative.SendMessageW(_hwnd, WindowNative.WM_SETICON, small, smallIcon);
        WindowNative.SendMessageW(_hwnd, WindowNative.WM_SETICON, big, bigIcon);
        DestroyIcons();
        _smallIcon = smallIcon;
        _bigIcon = bigIcon;
    }

    /// <summary>The widths of the two icons, as the window itself holds them.</summary>
    public (int Small, int Big) IconWidths()
    {
        const nuint small = 0, big = 1;
        return (TrayIcon.WidthOf(WindowNative.SendMessageW(_hwnd, WindowNative.WM_GETICON, small, 0)),
            TrayIcon.WidthOf(WindowNative.SendMessageW(_hwnd, WindowNative.WM_GETICON, big, 0)));
    }

    /// <summary>The pixels of the two icons, as the window itself holds them.</summary>
    public (uint[] Small, uint[] Big) IconPixels()
    {
        const nuint small = 0, big = 1;
        return (TrayIcon.PixelsOf(WindowNative.SendMessageW(_hwnd, WindowNative.WM_GETICON, small, 0)),
            TrayIcon.PixelsOf(WindowNative.SendMessageW(_hwnd, WindowNative.WM_GETICON, big, 0)));
    }

    private void DestroyIcons()
    {
        if (_smallIcon != 0)
        {
            Native.DestroyIcon(_smallIcon);
            _smallIcon = 0;
        }

        if (_bigIcon != 0)
        {
            Native.DestroyIcon(_bigIcon);
            _bigIcon = 0;
        }
    }

    private static (Native.RECT Area, uint Dpi) OffscreenArea()
    {
        var right = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN) - 4000;
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) - 4000;
        return (new Native.RECT { Left = right - 1920, Top = top, Right = right, Bottom = top + 1080 }, 96);
    }

    private static Native.SIZE OuterSize(uint dpi)
    {
        var scale = dpi / 96.0;
        Native.RECT rect = default;
        rect.Right = (int)Math.Round(SettingsScreen.WindowWidth * scale);
        rect.Bottom = (int)Math.Round(SettingsScreen.WindowHeight * scale);
        WindowNative.AdjustWindowRectExForDpi(&rect, Style, 0, 0, dpi);
        return new Native.SIZE { Cx = rect.Width, Cy = rect.Height };
    }

    private GdiCanvas NewCanvas()
    {
        var scale = _dpi / 96.0;
        return new GdiCanvas((int)Math.Round(SettingsScreen.WindowWidth * scale), (int)Math.Round(SettingsScreen.WindowHeight * scale), scale);
    }

    private static void RegisterClass(nint instance)
    {
        if (_classRegistered)
        {
            return;
        }

        fixed (char* name = ClassName)
        {
            Native.WNDCLASSEXW cls = default;
            cls.Size = (uint)sizeof(Native.WNDCLASSEXW);
            cls.WndProc = &WndProc;
            cls.Instance = instance;
            cls.ClassName = name;
            cls.Cursor = WindowNative.LoadCursorW(0, WindowNative.IDC_ARROW);
            Native.RegisterClassExW(&cls);
        }

        _classRegistered = true;
    }

    private void DarkFrame()
    {
        var on = 1;
        WindowNative.DwmSetWindowAttribute(_hwnd, WindowNative.DWMWA_USE_IMMERSIVE_DARK_MODE, &on, sizeof(int));

        // Windows 11 only; on Windows 10 this does nothing and dark mode alone remains.
        var caption = ColorRef(Theme.Base);
        WindowNative.DwmSetWindowAttribute(_hwnd, WindowNative.DWMWA_CAPTION_COLOR, &caption, sizeof(uint));
    }

    private void CreateEdit(nint instance)
    {
        _edit = Native.CreateWindowExW(0, "EDIT", null, WindowNative.WS_CHILD | WindowNative.ES_AUTOHSCROLL, 0, 0, 0, 0, _hwnd, EditId, instance, 0);
        if (_edit == 0)
        {
            return;
        }

        WindowNative.SendMessageW(_edit, WindowNative.EM_SETLIMITTEXT, 256, 0);
        _editOriginal = Native.GetWindowLongPtrW(_edit, WindowNative.GWLP_WNDPROC);
        Native.SetWindowLongPtrW(_edit, WindowNative.GWLP_WNDPROC, (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&EditWndProc);
    }

    private static uint ColorRef(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private float Scale => _dpi / 96f;

    private static uint Modifiers()
    {
        uint modifiers = 0;
        if (WindowNative.GetKeyState((int)Vk.Ctrl) < 0) modifiers |= HotkeyGesture.ModControl;
        if (WindowNative.GetKeyState((int)Vk.Alt) < 0) modifiers |= HotkeyGesture.ModAlt;
        if (WindowNative.GetKeyState((int)Vk.Shift) < 0) modifiers |= HotkeyGesture.ModShift;
        if (WindowNative.GetKeyState((int)Vk.LWin) < 0 || WindowNative.GetKeyState((int)Vk.RWin) < 0) modifiers |= HotkeyGesture.ModWin;
        return modifiers;
    }

    // ---------------------------------------------------------------- what the screen asks

    public void Invalidate()
    {
        _dirty = true;
        if (_hwnd != 0)
        {
            WindowNative.InvalidateRect(_hwnd, null, 0);
        }
    }

    // The picture is in the bitmap already, so this is no full paint: WM_PAINT only copies this part of it to the screen.
    public void InvalidateArea(Box area)
    {
        if (_hwnd == 0)
        {
            return;
        }

        var scale = Scale;
        Native.RECT rect = new()
        {
            Left = (int)MathF.Floor(area.X * scale),
            Top = (int)MathF.Floor(area.Y * scale),
            Right = (int)MathF.Ceiling(area.Right * scale),
            Bottom = (int)MathF.Ceiling(area.Bottom * scale),
        };
        WindowNative.InvalidateRect(_hwnd, &rect, 0);
    }

    public void ShowEdit(Box box, string text, Face face, float size, bool resetText)
    {
        if (_edit == 0)
        {
            return;
        }

        var key = (face, size, _dpi);
        if (_editFont == 0 || _editFontKey != key)
        {
            var old = _editFont;
            // The text box is a native control, so GDI draws it: the same family as the drawn text (for the mono face, the first
            // of Cascadia Mono, Consolas and Courier New that is installed), in ClearType like the rest of the window.
            _editFont = GdiFonts.Create(face == Face.Mono ? Face.Mono : Face.Body, (int)MathF.Round(size * Scale), Native.CLEARTYPE_QUALITY, out _);
            _editFontKey = key;
            WindowNative.SendMessageW(_edit, WindowNative.WM_SETFONT, (nuint)_editFont, 1);
            if (old != 0)
            {
                Native.DeleteObject(old);
            }
        }

        _editBox = box;
        _editSize = size;
        PlaceEdit();
        if (resetText)
        {
            _settingEditText = true;
            WindowNative.SetWindowTextW(_edit, text);
            _settingEditText = false;
            WindowNative.SendMessageW(_edit, WindowNative.EM_SETSEL, 0, -1);
        }

        if (!_editVisible)
        {
            _editVisible = true;
            Native.ShowWindow(_edit, _offscreen ? WindowNative.SW_SHOWNOACTIVATE : WindowNative.SW_SHOW);
        }
    }

    // Off screen for a test, the window never counts as the foreground one, so it never asks for the keyboard.
    public bool IsForeground => !_offscreen && _hwnd != 0 && Native.GetForegroundWindow() == _hwnd;

    public void FocusEdit()
    {
        if (_edit != 0)
        {
            WindowNative.SetFocus(_edit);
        }
    }

    // The text box is one line tall and sits in the middle of the field, so the text lines up with the drawn state.
    private void PlaceEdit()
    {
        var scale = Scale;
        var lineHeight = (int)MathF.Ceiling(_editSize * scale * 1.5f);
        var left = (int)MathF.Round(_editBox.X * scale);
        var width = (int)MathF.Round(_editBox.W * scale);
        var top = (int)MathF.Round(_editBox.MidY * scale - lineHeight / 2f);
        Native.SetWindowPos(_edit, 0, left, top, width, lineHeight, WindowNative.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public void HideEdit()
    {
        if (_editVisible)
        {
            _editVisible = false;
            Native.ShowWindow(_edit, Native.SW_HIDE);

            // In the background the keyboard is not ours to move. Coming back, WM_ACTIVATE gives it to the window.
            if (IsForeground)
            {
                WindowNative.SetFocus(_hwnd);
            }
        }
    }

    public void CaptureMouse() => WindowNative.SetCapture(_hwnd);

    public void ReleaseMouse() => WindowNative.ReleaseCapture();

    public void MeterWanted(bool wanted)
    {
        if (wanted == _meterOn || _hwnd == 0)
        {
            return;
        }

        _meterOn = wanted;
        if (wanted)
        {
            Native.SetTimer(_hwnd, MeterTimer, MeterIntervalMs, 0);
        }
        else
        {
            Native.KillTimer(_hwnd, MeterTimer);
        }
    }

    public void WaveWanted(bool wanted)
    {
        if (wanted == _waveOn || _hwnd == 0)
        {
            return;
        }

        _waveOn = wanted;
        if (wanted)
        {
            Native.SetTimer(_hwnd, WaveTimer, WaveIntervalMs, 0);
        }
        else
        {
            Native.KillTimer(_hwnd, WaveTimer);
        }
    }

    public string? PickFolder(string startFolder) => FolderPicker.Pick(_hwnd, "Choose the clips folder", startFolder);

    public void CloseWindow() => Close();

    // ---------------------------------------------------------------- messages

    private nint Handle(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowNative.WM_ERASEBKGND:
                return 1;

            case WindowNative.WM_PAINT:
                Paint(hwnd);
                return 0;

            case WindowNative.WM_KEYDOWN:
            case WindowNative.WM_SYSKEYDOWN:
                _captureKeys.KeyDown(Screen.Listening, KeyRepeat.FromLParam(lParam));
                var editWasHidden = !_editVisible;
                var used = Screen.Key((uint)wParam, Modifiers(), KeyRepeat.FromLParam(lParam));

                // This key brought a scrolled-away field back: it is the box's, and so is its character, already on its way here.
                _charForEdit = !used && editWasHidden && _editVisible && message == WindowNative.WM_KEYDOWN;
                if (_charForEdit)
                {
                    return WindowNative.SendMessageW(_edit, message, wParam, lParam);
                }

                return used ? 0 : Native.DefWindowProcW(hwnd, message, wParam, lParam);

            case WindowNative.WM_KEYUP:
            case WindowNative.WM_SYSKEYUP:
                Screen.KeyUp((uint)wParam, Modifiers());

                // An Alt typed for a hotkey must not open the window menu when it comes up.
                return message == WindowNative.WM_KEYUP || _captureKeys.Swallow ? 0 : Native.DefWindowProcW(hwnd, message, wParam, lParam);

            case WindowNative.WM_SYSCHAR:
                return Screen.Listening || _captureKeys.Swallow ? 0 : Native.DefWindowProcW(hwnd, message, wParam, lParam);

            case WindowNative.WM_CHAR:
                if (_charForEdit)
                {
                    _charForEdit = false;
                    return WindowNative.SendMessageW(_edit, message, wParam, lParam);
                }

                return 0;

            case WindowNative.WM_MOUSEMOVE:
                TrackLeave();
                Screen.MouseMove(DesignX(lParam), DesignY(lParam));
                return 0;

            case WindowNative.WM_MOUSELEAVE:
                _trackingLeave = false;
                Screen.MouseLeft();
                return 0;

            case WindowNative.WM_LBUTTONDOWN:
                if (!_editVisible && !_offscreen)
                {
                    WindowNative.SetFocus(hwnd);
                }

                Screen.MouseDown(DesignX(lParam), DesignY(lParam));
                return 0;

            case WindowNative.WM_LBUTTONUP:
                Screen.MouseUp(DesignX(lParam), DesignY(lParam));
                return 0;

            case WindowNative.WM_CAPTURECHANGED:
                if (lParam != hwnd)
                {
                    Screen.MouseCaptureLost();
                }

                return 0;

            case WindowNative.WM_MOUSEWHEEL:
                (var notches, _wheelRest) = WheelNotches.Add(_wheelRest, (short)((wParam >> 16) & 0xFFFF));
                if (notches != 0)
                {
                    Screen.Wheel(notches);
                }

                return 0;

            case Native.WM_TIMER:
                if (wParam == MeterTimer)
                {
                    Screen.MeterTick(_readMicPeak());
                }
                else if (wParam == WaveTimer)
                {
                    Screen.WaveTick();
                }

                return 0;

            case WindowNative.WM_SIZE:
                Screen.WindowMinimized(wParam == WindowNative.SIZE_MINIMIZED);
                return Native.DefWindowProcW(hwnd, message, wParam, lParam);

            case WindowNative.WM_ACTIVATE:
                if ((int)(wParam & 0xFFFF) == WindowNative.WA_INACTIVE)
                {
                    Screen.Deactivated();
                }
                else
                {
                    Activated?.Invoke();
                }

                return Native.DefWindowProcW(hwnd, message, wParam, lParam);

            case WindowNative.WM_SETFOCUS:
                if (_editVisible)
                {
                    WindowNative.SetFocus(_edit);
                }

                return 0;

            case WindowNative.WM_COMMAND:
                if ((wParam >> 16) == WindowNative.EN_CHANGE && !_settingEditText && lParam == _edit)
                {
                    Screen.EditChanged(EditText());
                }

                return 0;

            case WindowNative.WM_CTLCOLOREDIT:
                WindowNative.SetBkColor((nint)wParam, ColorRef(Theme.Raised));
                Native.SetTextColor((nint)wParam, ColorRef(Theme.Text));
                return _editBrush;

            case WindowNative.WM_DPICHANGED:
                OnDpiChanged((uint)(wParam & 0xFFFF), (Native.RECT*)lParam);
                return 0;

            case Native.WM_CLOSE:
                Screen.Closing();
                Native.DestroyWindow(hwnd);
                return 0;

            case WindowNative.WM_DESTROY:
                Release();
                return 0;

            default:
                return Native.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private void Paint(nint hwnd)
    {
        WindowNative.PAINTSTRUCT paint;
        var dc = WindowNative.BeginPaint(hwnd, &paint);
        if (_dirty)
        {
            _dirty = false;
            Screen.Paint(_canvas);
        }

        var area = paint.Paint;
        WindowNative.BitBlt(dc, area.Left, area.Top, area.Width, area.Height, _canvas.Dc, area.Left, area.Top, WindowNative.SRCCOPY);
        WindowNative.EndPaint(hwnd, &paint);
    }

    private void OnDpiChanged(uint dpi, Native.RECT* suggested)
    {
        _dpi = dpi;
        var old = _canvas;
        _canvas = NewCanvas();
        Screen.SetCanvas(_canvas);
        old.Dispose();
        Native.SetWindowPos(_hwnd, 0, suggested->Left, suggested->Top, suggested->Width, suggested->Height, WindowNative.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        if (_editVisible)
        {
            PlaceEdit();
        }

        if (_iconAccent is { } accent)
        {
            SetIcon(accent);
        }

        Invalidate();
    }

    private float DesignX(nint lParam) => (short)(lParam & 0xFFFF) / Scale;

    private float DesignY(nint lParam) => (short)((lParam >> 16) & 0xFFFF) / Scale;

    private void TrackLeave()
    {
        if (_trackingLeave)
        {
            return;
        }

        _trackingLeave = true;
        WindowNative.TRACKMOUSEEVENT track = default;
        track.Size = (uint)sizeof(WindowNative.TRACKMOUSEEVENT);
        track.Flags = WindowNative.TME_LEAVE;
        track.Target = _hwnd;
        WindowNative.TrackMouseEvent(&track);
    }

    private string EditText()
    {
        var length = WindowNative.GetWindowTextLengthW(_edit);
        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = stackalloc char[length + 1];
        var read = Native.GetWindowTextW(_edit, buffer, length + 1);
        return new string(buffer, 0, read);
    }

    // Tab, Enter and Escape belong to the window. A single-line EDIT would only beep at them.
    private nint HandleEdit(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowNative.WM_KEYDOWN when (uint)wParam is Vk.Tab or Vk.Enter or Vk.Escape:
                Screen.Key((uint)wParam, Modifiers(), KeyRepeat.FromLParam(lParam));
                return 0;
            case WindowNative.WM_KEYDOWN when wParam == 'A' && Modifiers() == HotkeyGesture.ModControl:
                WindowNative.SendMessageW(_edit, WindowNative.EM_SETSEL, 0, -1);
                return 0;

            // Ctrl+A arrives as character 1. The other three would only beep.
            case WindowNative.WM_CHAR when wParam is 1 or 9 or 13 or 27:
                return 0;
            default:
                return WindowNative.CallWindowProcW(_editOriginal, hwnd, message, wParam, lParam);
        }
    }

    // Nothing of the window outlives it: bitmap, fonts, brush, timer. The notification and the tray keep their own.
    private void Release()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (_meterOn)
        {
            Native.KillTimer(_hwnd, MeterTimer);
            _meterOn = false;
        }

        if (_waveOn)
        {
            Native.KillTimer(_hwnd, WaveTimer);
            _waveOn = false;
        }

        _canvas.Dispose();
        if (_editFont != 0)
        {
            Native.DeleteObject(_editFont);
            _editFont = 0;
        }

        Native.DeleteObject(_editBrush);
        DestroyIcons();
        _hwnd = 0;
        _edit = 0;
        _current = null;
        Closed();
    }

    public void Dispose()
    {
        if (_hwnd != 0)
        {
            Screen.Closing();
            Native.DestroyWindow(_hwnd);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        // An exception crossing this boundary would end the process, and the buffer with it.
        try
        {
            // Messages sent during CreateWindow arrive before the screen exists.
            return _current is { } window && window._hwnd == hwnd && window.Screen is not null
                ? window.Handle(hwnd, message, wParam, lParam)
                : Native.DefWindowProcW(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static nint EditWndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            // While the window is being destroyed the box still needs its own procedure, to free what it holds.
            return _current is { } window && window._edit == hwnd
                ? window.HandleEdit(hwnd, message, wParam, lParam)
                : WindowNative.CallWindowProcW(_editOriginal, hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
            return 0;
        }
    }
}
