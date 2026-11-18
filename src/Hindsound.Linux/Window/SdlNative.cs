using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Linux.Window;

/// <summary>
/// The calls of SDL 3.2 the window makes, as declared in SDL3/SDL_init.h, SDL_hints.h, SDL_video.h, SDL_surface.h,
/// SDL_events.h, SDL_mouse.h, SDL_error.h and SDL_stdinc.h of release 3.2.0, and the structures it reads, at their exact C
/// layout for 64-bit Linux. Nothing here is newer than 3.2.0. libSDL3.so.0 is found next to the program first, then in the
/// system (<see cref="SdlLibrary"/>). Partial: the keyboard, text and clipboard calls live in a file of their own.
/// </summary>
/// <remarks>
/// SDL's bool is C99 bool, one byte: every bool crosses as <see cref="UnmanagedType.U1"/>. A string SDL returns is SDL's
/// own memory, so it comes back as a pointer, read with <see cref="Utf8"/>, never freed by the marshaller.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static unsafe partial class SdlNative
{
    private const string Library = SdlLibrary.SystemName;

    // The resolver has to be in place before the first call into the library loads it.
    static SdlNative() => SdlLibrary.InstallResolver();

    // SDL_InitFlags.
    public const uint InitEvents = 0x00004000;
    public const uint InitVideo = 0x00000020;

    // Hints.
    public const string HintAppId = "SDL_APP_ID";
    public const string HintVideoDriver = "SDL_VIDEO_DRIVER";

    /// <summary>"1" keeps SDL from catching SIGINT and SIGTERM: the app has its own way to end on them.</summary>
    public const string HintNoSignalHandlers = "SDL_NO_SIGNAL_HANDLERS";

    /// <summary>"1" lets the screen saver start while the window is open: SDL stops it by default, as a game would.</summary>
    public const string HintAllowScreensaver = "SDL_VIDEO_ALLOW_SCREENSAVER";

    /// <summary>The application id the window carries (the Wayland app id, the X11 class), that of the desktop entry.</summary>
    public const string AppId = "io.github.quelquun667.Hindsound";

    /// <summary>The video driver of SDL that needs no display: windows in memory only, for checks and tests.</summary>
    public const string OffscreenDriver = "offscreen";

    // SDL_WindowFlags (Uint64).
    public const ulong WindowOccluded = 0x0000000000000004;
    public const ulong WindowHidden = 0x0000000000000008;
    public const ulong WindowBorderless = 0x0000000000000010;
    public const ulong WindowResizable = 0x0000000000000020;
    public const ulong WindowMinimized = 0x0000000000000040;
    public const ulong WindowInputFocus = 0x0000000000000200;
    public const ulong WindowMouseFocus = 0x0000000000000400;
    public const ulong WindowHighPixelDensity = 0x0000000000002000;
    public const ulong WindowMouseCapture = 0x0000000000004000;
    public const ulong WindowUtility = 0x0000000000020000;

    /// <summary>SDL_WINDOWPOS_CENTERED: centred on the primary display.</summary>
    public const int WindowPosCentered = 0x2FFF0000;

    // SDL_PixelFormat. ARGB8888 is a 32-bit 0xAARRGGBB word, B G R A in memory on little-endian: the layout of Pixels.
    public const uint PixelFormatArgb8888 = 0x16362004;
    public const uint PixelFormatXrgb8888 = 0x16161804;

    // SDL_EventType, those the window reads.
    public const uint EventQuit = 0x100;
    public const uint EventDisplayOrientation = 0x151;
    public const uint EventDisplayAdded = 0x152;
    public const uint EventDisplayRemoved = 0x153;
    public const uint EventDisplayMoved = 0x154;
    public const uint EventDisplayDesktopModeChanged = 0x155;
    public const uint EventDisplayCurrentModeChanged = 0x156;
    public const uint EventDisplayContentScaleChanged = 0x157;
    public const uint EventWindowShown = 0x202;
    public const uint EventWindowHidden = 0x203;
    public const uint EventWindowExposed = 0x204;
    public const uint EventWindowMoved = 0x205;
    public const uint EventWindowResized = 0x206;
    public const uint EventWindowPixelSizeChanged = 0x207;
    public const uint EventWindowMinimized = 0x209;
    public const uint EventWindowMaximized = 0x20A;
    public const uint EventWindowRestored = 0x20B;
    public const uint EventWindowMouseEnter = 0x20C;
    public const uint EventWindowMouseLeave = 0x20D;
    public const uint EventWindowFocusGained = 0x20E;
    public const uint EventWindowFocusLost = 0x20F;
    public const uint EventWindowCloseRequested = 0x210;
    public const uint EventWindowDisplayChanged = 0x213;
    public const uint EventWindowDisplayScaleChanged = 0x214;
    public const uint EventWindowOccluded = 0x216;
    public const uint EventWindowDestroyed = 0x219;
    public const uint EventKeyDown = 0x300;
    public const uint EventKeyUp = 0x301;
    public const uint EventTextEditing = 0x302;
    public const uint EventTextInput = 0x303;
    public const uint EventKeymapChanged = 0x304;
    public const uint EventMouseMotion = 0x400;
    public const uint EventMouseButtonDown = 0x401;
    public const uint EventMouseButtonUp = 0x402;
    public const uint EventMouseWheel = 0x403;
    public const uint EventUser = 0x8000;

    // Mouse buttons (SDL_BUTTON_*) and the direction of a wheel (SDL_MouseWheelDirection).
    public const byte ButtonLeft = 1;
    public const byte ButtonMiddle = 2;
    public const byte ButtonRight = 3;
    public const uint MouseWheelNormal = 0;
    public const uint MouseWheelFlipped = 1;

    /// <summary>SDL_Rect.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int X;
        public int Y;
        public int W;
        public int H;

        public Rect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }
    }

    /// <summary>SDL_Surface: the public part SDL lets a program read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct Surface
    {
        [FieldOffset(0)] public uint Flags;
        [FieldOffset(4)] public uint Format;
        [FieldOffset(8)] public int W;
        [FieldOffset(12)] public int H;
        [FieldOffset(16)] public int Pitch;
        [FieldOffset(24)] public void* Pixels;
        [FieldOffset(32)] public int Refcount;
        [FieldOffset(40)] public void* Reserved;
    }

    /// <summary>SDL_CommonEvent: the head every event starts with.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct CommonEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Reserved;
        [FieldOffset(8)] public ulong Timestamp;
    }

    /// <summary>SDL_DisplayEvent (SDL_EVENT_DISPLAY_*).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct DisplayEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint DisplayId;
        [FieldOffset(20)] public int Data1;
        [FieldOffset(24)] public int Data2;
    }

    /// <summary>SDL_WindowEvent (SDL_EVENT_WINDOW_*): data1 and data2 hold a size, a position or a display, by type.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct WindowEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(20)] public int Data1;
        [FieldOffset(24)] public int Data2;
    }

    /// <summary>SDL_KeyboardEvent (SDL_EVENT_KEY_DOWN, SDL_EVENT_KEY_UP).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct KeyboardEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(20)] public uint Which;

        /// <summary>SDL_Scancode: the physical key.</summary>
        [FieldOffset(24)] public int Scancode;

        /// <summary>SDL_Keycode: the key of the layout.</summary>
        [FieldOffset(28)] public uint Key;

        /// <summary>SDL_Keymod.</summary>
        [FieldOffset(32)] public ushort Mod;
        [FieldOffset(34)] public ushort Raw;
        [FieldOffset(36)] public byte DownByte;
        [FieldOffset(37)] public byte RepeatByte;

        public readonly bool Down => DownByte != 0;

        public readonly bool Repeat => RepeatByte != 0;
    }

    /// <summary>SDL_TextEditingEvent (SDL_EVENT_TEXT_EDITING): the text being composed, valid until the next event.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct TextEditingEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public byte* Text;
        [FieldOffset(32)] public int Start;
        [FieldOffset(36)] public int Length;

        public readonly string TextString => Utf8(Text);
    }

    /// <summary>SDL_TextInputEvent (SDL_EVENT_TEXT_INPUT): UTF-8, valid until the next event.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct TextInputEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public byte* Text;

        public readonly string TextString => Utf8(Text);
    }

    /// <summary>SDL_MouseMotionEvent: x and y in window coordinates (not pixels), fractional.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct MouseMotionEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(20)] public uint Which;

        /// <summary>SDL_MouseButtonFlags: bit (button - 1) for each button held.</summary>
        [FieldOffset(24)] public uint State;
        [FieldOffset(28)] public float X;
        [FieldOffset(32)] public float Y;
        [FieldOffset(36)] public float XRel;
        [FieldOffset(40)] public float YRel;
    }

    /// <summary>SDL_MouseButtonEvent (down or up): x and y in window coordinates.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct MouseButtonEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(20)] public uint Which;
        [FieldOffset(24)] public byte Button;
        [FieldOffset(25)] public byte DownByte;
        [FieldOffset(26)] public byte Clicks;
        [FieldOffset(27)] public byte Padding;
        [FieldOffset(28)] public float X;
        [FieldOffset(32)] public float Y;

        public readonly bool Down => DownByte != 0;
    }

    /// <summary>
    /// SDL_MouseWheelEvent: y is positive away from the user; when <see cref="Direction"/> is flipped, x and y are the other
    /// way. MouseX and MouseY are where the pointer is, in window coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct MouseWheelEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(20)] public uint Which;
        [FieldOffset(24)] public float X;
        [FieldOffset(28)] public float Y;
        [FieldOffset(32)] public uint Direction;
        [FieldOffset(36)] public float MouseX;
        [FieldOffset(40)] public float MouseY;
    }

    /// <summary>SDL_UserEvent, for the types SDL_RegisterEvents gives.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct UserEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ulong Timestamp;
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(20)] public int Code;
        [FieldOffset(24)] public void* Data1;
        [FieldOffset(32)] public void* Data2;
    }

    /// <summary>SDL_Event: the union, 128 bytes. <see cref="Type"/> says which member to read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(0)] public CommonEvent Common;
        [FieldOffset(0)] public DisplayEvent Display;
        [FieldOffset(0)] public WindowEvent Window;
        [FieldOffset(0)] public KeyboardEvent Key;
        [FieldOffset(0)] public TextEditingEvent Edit;
        [FieldOffset(0)] public TextInputEvent Text;
        [FieldOffset(0)] public MouseMotionEvent Motion;
        [FieldOffset(0)] public MouseButtonEvent Button;
        [FieldOffset(0)] public MouseWheelEvent Wheel;
        [FieldOffset(0)] public UserEvent User;
    }

    /// <summary>A UTF-8 string of SDL's, copied; "" for null.</summary>
    public static string Utf8(byte* text) => text is null ? "" : Marshal.PtrToStringUTF8((nint)text) ?? "";

    /// <summary>The last error SDL set on this thread.</summary>
    public static string Error() => Utf8(GetError());

    // SDL_init.h.

    [LibraryImport(Library, EntryPoint = "SDL_InitSubSystem")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool InitSubSystem(uint flags);

    [LibraryImport(Library, EntryPoint = "SDL_QuitSubSystem")]
    public static partial void QuitSubSystem(uint flags);

    [LibraryImport(Library, EntryPoint = "SDL_WasInit")]
    public static partial uint WasInit(uint flags);

    [LibraryImport(Library, EntryPoint = "SDL_Quit")]
    public static partial void Quit();

    // SDL_hints.h.

    [LibraryImport(Library, EntryPoint = "SDL_SetHint", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetHint(string name, string? value);

    // SDL_video.h: windows.

    [LibraryImport(Library, EntryPoint = "SDL_CreateWindow", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint CreateWindow(string title, int w, int h, ulong flags);

    [LibraryImport(Library, EntryPoint = "SDL_DestroyWindow")]
    public static partial void DestroyWindow(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_ShowWindow")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ShowWindow(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_HideWindow")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HideWindow(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_RaiseWindow")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RaiseWindow(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_SetWindowTitle", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetWindowTitle(nint window, string title);

    [LibraryImport(Library, EntryPoint = "SDL_SetWindowIcon")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetWindowIcon(nint window, Surface* icon);

    [LibraryImport(Library, EntryPoint = "SDL_GetWindowID")]
    public static partial uint GetWindowID(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_GetWindowFlags")]
    public static partial ulong GetWindowFlags(nint window);

    /// <summary>How much bigger than 96 dpi the content should be drawn: pixel density times the display's content scale.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetWindowDisplayScale")]
    public static partial float GetWindowDisplayScale(nint window);

    /// <summary>Pixels per window coordinate: above 1 on a Wayland output with a scale, 1 on X11.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetWindowPixelDensity")]
    public static partial float GetWindowPixelDensity(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_GetWindowSizeInPixels")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetWindowSizeInPixels(nint window, int* w, int* h);

    [LibraryImport(Library, EntryPoint = "SDL_GetWindowSize")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetWindowSize(nint window, int* w, int* h);

    [LibraryImport(Library, EntryPoint = "SDL_SetWindowSize")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetWindowSize(nint window, int w, int h);

    [LibraryImport(Library, EntryPoint = "SDL_SetWindowMinimumSize")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetWindowMinimumSize(nint window, int minW, int minH);

    [LibraryImport(Library, EntryPoint = "SDL_SetWindowPosition")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetWindowPosition(nint window, int x, int y);

    [LibraryImport(Library, EntryPoint = "SDL_GetWindowPosition")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetWindowPosition(nint window, int* x, int* y);

    // SDL_video.h: displays.

    [LibraryImport(Library, EntryPoint = "SDL_GetDisplayForWindow")]
    public static partial uint GetDisplayForWindow(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_GetPrimaryDisplay")]
    public static partial uint GetPrimaryDisplay();

    /// <summary>A 0-terminated array of display ids, to give back with <see cref="Free"/>; null on failure.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetDisplays")]
    public static partial uint* GetDisplays(int* count);

    /// <summary>The display less its panels and docks, in window coordinates.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetDisplayUsableBounds")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetDisplayUsableBounds(uint displayId, Rect* rect);

    [LibraryImport(Library, EntryPoint = "SDL_GetDisplayContentScale")]
    public static partial float GetDisplayContentScale(uint displayId);

    // SDL_video.h: the window's surface, drawn in software.

    /// <summary>The window's surface, owned by the window, valid until the window is resized or destroyed.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetWindowSurface")]
    public static partial Surface* GetWindowSurface(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_UpdateWindowSurface")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool UpdateWindowSurface(nint window);

    /// <summary>Shows the given rectangles of the surface, in pixels.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_UpdateWindowSurfaceRects")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool UpdateWindowSurfaceRects(nint window, Rect* rects, int count);

    [LibraryImport(Library, EntryPoint = "SDL_DestroyWindowSurface")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool DestroyWindowSurface(nint window);

    // SDL_surface.h.

    /// <summary>A surface over memory the caller owns and keeps alive until <see cref="DestroySurface"/>; SDL never frees it.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_CreateSurfaceFrom")]
    public static partial Surface* CreateSurfaceFrom(int width, int height, uint format, void* pixels, int pitch);

    [LibraryImport(Library, EntryPoint = "SDL_DestroySurface")]
    public static partial void DestroySurface(Surface* surface);

    /// <summary>Copies (with the source's blend mode) a rectangle of one surface to another; null rects mean whole surfaces.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_BlitSurface")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool BlitSurface(Surface* source, Rect* sourceRect, Surface* target, Rect* targetRect);

    // SDL_events.h.

    /// <summary>Waits up to <paramref name="timeoutMs"/> (-1: with no end) and takes the next event; false on timeout.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_WaitEventTimeout")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool WaitEventTimeout(Event* e, int timeoutMs);

    [LibraryImport(Library, EntryPoint = "SDL_PollEvent")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool PollEvent(Event* e);

    /// <summary>Adds an event to the queue. Safe from any thread. False if the queue refused it (full, or filtered).</summary>
    [LibraryImport(Library, EntryPoint = "SDL_PushEvent")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool PushEvent(Event* e);

    /// <summary>The first of <paramref name="count"/> new event types of the program's own, or 0 when none is left.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_RegisterEvents")]
    public static partial uint RegisterEvents(int count);

    // SDL_mouse.h.

    /// <summary>Keeps the mouse events coming to the window with the focus while a button is held outside it.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_CaptureMouse")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CaptureMouse([MarshalAs(UnmanagedType.U1)] bool enabled);

    /// <summary>Where the mouse is on the desktop, in screen coordinates. Wayland does not tell: it then gives a guess or 0.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetGlobalMouseState")]
    public static partial uint GetGlobalMouseState(float* x, float* y);

    // SDL_keyboard.h.

    /// <summary>The SDL_Keymod of the modifier keys held now.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetModState")]
    public static partial ushort GetModState();

    /// <summary>
    /// The keycode a scancode types on the layout with these modifiers applied (Shift, Caps Lock): '!' for the key 1 with
    /// Shift on a US layout. <c>key_event</c> false, as the keycode of a key event has no modifier applied.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetKeyFromScancode")]
    public static partial uint GetKeyFromScancode(uint scancode, ushort modstate, [MarshalAs(UnmanagedType.U1)] bool keyEvent);

    // SDL_video.h: what a window needs to fit its screen.

    /// <summary>SDL_Point.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>The display a point is on, or the closest one; 0 on error.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetDisplayForPoint")]
    public static partial uint GetDisplayForPoint(Point* point);

    /// <summary>The frame the desktop draws around the window, in window coordinates. False when it is not known yet.</summary>
    [LibraryImport(Library, EntryPoint = "SDL_GetWindowBordersSize")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetWindowBordersSize(nint window, int* top, int* left, int* bottom, int* right);

    [LibraryImport(Library, EntryPoint = "SDL_RestoreWindow")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RestoreWindow(nint window);

    // SDL_error.h and SDL_stdinc.h.

    [LibraryImport(Library, EntryPoint = "SDL_GetError")]
    public static partial byte* GetError();

    [LibraryImport(Library, EntryPoint = "SDL_free")]
    public static partial void Free(void* memory);
}
