using System.Runtime.InteropServices;

namespace Hindsound.Ui;

/// <summary>Every Win32 call of the interface. LibraryImport and blittable structs only.</summary>
internal static unsafe partial class Native
{
    public const uint WM_CLOSE = 0x0010, WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016, WM_TIMER = 0x0113,
        WM_LBUTTONUP = 0x0202, WM_MOUSEACTIVATE = 0x0021, WM_NULL = 0, WM_CONTEXTMENU = 0x007B,
        WM_APP = 0x8000;
    public const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    public const nint MA_NOACTIVATE = 3;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008, WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TRANSPARENT = 0x00000020;
    public const int GWL_EXSTYLE = -20;

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1;
    public static readonly nint HWND_TOPMOST = -1;

    public const uint ULW_ALPHA = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    public const int SM_CXSMICON = 49, SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80, NOTIFYICON_VERSION_4 = 4;

    public const uint MF_STRING = 0, MF_GRAYED = 1, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002,
        TPM_NONOTIFY = 0x0080;

    public const int ANTIALIASED_QUALITY = 4, BK_TRANSPARENT = 1, FIXED_PITCH_MODERN = 0x31;

    public const uint CF_HDROP = 15, GMEM_MOVEABLE = 0x0002;
    public const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public POINT Pt;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint Size, Style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> WndProc;
        public int ClsExtra, WndExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint IconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UPDATELAYEREDWINDOWINFO
    {
        public uint Size;
        public nint HdcDst;
        public POINT* PptDst;
        public SIZE* Psize;
        public nint HdcSrc;
        public POINT* PptSrc;
        public uint CrKey;
        public BLENDFUNCTION* Pblend;
        public uint Flags;
        public RECT* PrcDirty;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int Icon;
        public uint XHotspot, YHotspot;
        public nint Mask, Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint Size;
        public nint Hwnd;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State, StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TEXTMETRICW
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang,
            DigitizedAspectX, DigitizedAspectY;
        public char FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSet, WorkingSet, QuotaPeakPaged, QuotaPaged, QuotaPeakNonPaged, QuotaNonPaged, Pagefile, PeakPagefile, Private;
    }

    private const string U = "user32.dll", G = "gdi32.dll", K = "kernel32.dll", S = "shell32.dll";

    [LibraryImport(U)] public static partial ushort RegisterClassExW(WNDCLASSEXW* cls);
    [LibraryImport(U, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport(U)] public static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [LibraryImport(U)] public static partial int DestroyWindow(nint hwnd);
    [LibraryImport(U)] public static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport(U)] public static partial int TranslateMessage(MSG* msg);
    [LibraryImport(U)] public static partial nint DispatchMessageW(MSG* msg);
    [LibraryImport(U)] public static partial int PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [LibraryImport(U)] public static partial void PostQuitMessage(int code);
    [LibraryImport(U, StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessageW(string name);
    [LibraryImport(U)] public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport(U)] public static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport(U)] public static partial int UpdateLayeredWindowIndirect(nint hwnd, UPDATELAYEREDWINDOWINFO* info);
    [LibraryImport(U)] public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [LibraryImport(U)] public static partial int ShowWindow(nint hwnd, int cmd);
    [LibraryImport(U)] public static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport(U)] public static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport(U)] public static partial nint GetForegroundWindow();
    [LibraryImport(U)] public static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport(U)] public static partial int GetWindowTextW(nint hwnd, char* text, int count);
    [LibraryImport(U)] public static partial int GetClassNameW(nint hwnd, char* text, int count);
    [LibraryImport(U)] public static partial int GetWindowRect(nint hwnd, RECT* rect);
    [LibraryImport(U)] public static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport(U)] public static partial nint MonitorFromPoint(POINT point, uint flags);
    [LibraryImport(U)] public static partial int GetCursorPos(POINT* point);
    [LibraryImport(U)] public static partial int GetMonitorInfoW(nint monitor, MONITORINFO* info);
    [LibraryImport(U)] public static partial int GetSystemMetrics(int index);
    [LibraryImport(U)] public static partial nint CreatePopupMenu();
    [LibraryImport(U, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [LibraryImport(U)] public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint param);
    [LibraryImport(U)] public static partial int DestroyMenu(nint menu);
    [LibraryImport(U)] public static partial nint CreateIconIndirect(ICONINFO* info);
    [LibraryImport(U)] public static partial int DestroyIcon(nint icon);
    [LibraryImport(U)] public static partial int OpenClipboard(nint owner);
    [LibraryImport(U)] public static partial int EmptyClipboard();
    [LibraryImport(U)] public static partial nint SetClipboardData(uint format, nint data);
    [LibraryImport(U)] public static partial int CloseClipboard();
    [LibraryImport(U, StringMarshalling = StringMarshalling.Utf16)] public static partial int ShutdownBlockReasonCreate(nint hwnd, string reason);
    [LibraryImport(U)] public static partial int ShutdownBlockReasonDestroy(nint hwnd);
    [LibraryImport(U, StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindowW(string cls, string? title);
    [LibraryImport(U)] public static partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);
    [LibraryImport(U)] public static partial int AllowSetForegroundWindow(uint processId);

    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint monitor, int type, uint* x, uint* y);

    [LibraryImport(S)] public static partial int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);
    [LibraryImport(S)] public static partial int SHQueryUserNotificationState(int* state);
    [LibraryImport(S, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecuteW(nint hwnd, string? verb, string file, string? parameters, string? directory, int show);

    [LibraryImport("winmm.dll")] public static partial int PlaySoundW(byte* sound, nint module, uint flags);

    [LibraryImport(G)] public static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport(G)] public static partial int DeleteDC(nint dc);
    [LibraryImport(G)] public static partial nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);
    [LibraryImport(G)] public static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport(G)] public static partial int DeleteObject(nint obj);
    [LibraryImport(G)] public static partial nint GetStockObject(int index);
    [LibraryImport(G)] public static partial nint CreateBitmap(int w, int h, uint planes, uint bits, void* data);
    [LibraryImport(G, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strike,
        uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitchFamily, string face);
    [LibraryImport(G)] public static partial uint SetTextColor(nint dc, uint color);
    [LibraryImport(G)] public static partial int SetBkMode(nint dc, int mode);
    [LibraryImport(G, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int TextOutW(nint dc, int x, int y, string text, int length);
    [LibraryImport(G, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetTextExtentPoint32W(nint dc, string text, int length, SIZE* size);
    [LibraryImport(G)] public static partial int GetTextMetricsW(nint dc, TEXTMETRICW* metrics);

    [LibraryImport(K, StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? name);
    [LibraryImport(K)] public static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport(K)] public static partial nint GlobalFree(nint memory);
    [LibraryImport(K)] public static partial void* GlobalLock(nint memory);
    [LibraryImport(K)] public static partial int GlobalUnlock(nint memory);
    [LibraryImport(K)] public static partial nint GetCurrentProcess();
    [LibraryImport(K)] public static partial int GetProcessTimes(nint process, long* creation, long* exit, long* kernel, long* user);
    [LibraryImport(K)] public static partial int K32GetProcessMemoryInfo(nint process, PROCESS_MEMORY_COUNTERS_EX* counters, uint size);
}
