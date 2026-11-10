using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Linux.Input;

/// <summary>
/// The few calls of libX11 and libc the X11 shortcuts need. Every Xlib call is made on one thread, the shortcuts' own,
/// so Xlib needs no locking of its own. Types as on 64-bit Linux: Window, KeySym and Time are unsigned long.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class X11Native
{
    private const string X11 = "libX11.so.6";
    private const string LibC = "libc.so.6";

    public const int KeyPress = 2;
    public const int GrabModeAsync = 1;

    /// <summary>"A client attempted to grab a key/button combination already grabbed by another client."</summary>
    public const byte BadAccess = 10;

    /// <summary>The size of the XEvent union: 24 longs.</summary>
    public const int EventSize = 24 * 8;

    // Offsets in XKeyEvent: int type; unsigned long serial; Bool send_event; Display *display; Window window, root,
    // subwindow; Time time; int x, y, x_root, y_root; unsigned int state; unsigned int keycode.
    public const int KeyStateOffset = 80;
    public const int KeyCodeOffset = 84;

    // Offset of error_code in XErrorEvent: int type; Display *display; XID resourceid; unsigned long serial; unsigned char error_code.
    private const int ErrorCodeOffset = 32;

    public const short PollIn = 0x001;

    /// <summary>O_CLOEXEC of Linux on x86-64 (octal 02000000): the descriptor is closed in the programs the app starts.</summary>
    public const int CloseOnExec = 0x80000;

    private static int _lastError;

    // What the I/O error handler tells: the X11 layer that has the display. One display at a time in the app.
    private static Action? s_connectionLost;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport(X11)]
    public static partial nint XOpenDisplay(nint name);

    [LibraryImport(X11)]
    public static partial int XCloseDisplay(nint display);

    [LibraryImport(X11)]
    public static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(X11)]
    public static partial byte XKeysymToKeycode(nint display, nuint keysym);

    [LibraryImport(X11)]
    public static partial int XGrabKey(nint display, int keycode, uint modifiers, nuint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(X11)]
    public static partial int XUngrabKey(nint display, int keycode, uint modifiers, nuint grabWindow);

    [LibraryImport(X11)]
    public static partial int XSync(nint display, int discard);

    [LibraryImport(X11)]
    public static partial int XPending(nint display);

    [LibraryImport(X11)]
    public static partial int XNextEvent(nint display, nint eventReturn);

    [LibraryImport(X11)]
    public static partial int XConnectionNumber(nint display);

    [LibraryImport(X11)]
    public static partial nint XGetModifierMapping(nint display);

    [LibraryImport(X11)]
    public static partial int XFreeModifiermap(nint modmap);

    [LibraryImport(X11)]
    private static unsafe partial nint XSetErrorHandler(delegate* unmanaged[Cdecl]<nint, nint, int> handler);

    [LibraryImport(X11)]
    private static unsafe partial nint XSetIOErrorHandler(delegate* unmanaged[Cdecl]<nint, int> handler);

    [LibraryImport(LibC, SetLastError = true)]
    private static unsafe partial int pipe2(int* fds, int flags);

    [LibraryImport(LibC, SetLastError = true)]
    public static unsafe partial int poll(PollFd* fds, nuint count, int timeout);

    [LibraryImport(LibC, SetLastError = true)]
    public static unsafe partial nint read(int fd, byte* buffer, nuint count);

    [LibraryImport(LibC, SetLastError = true)]
    public static unsafe partial nint write(int fd, byte* buffer, nuint count);

    [LibraryImport(LibC)]
    public static partial int close(int fd);

    /// <summary>
    /// Puts in an error handler that only remembers the code. Xlib's own prints the error and ends the process, and a
    /// grab another client holds is such an error, sent back later.
    /// </summary>
    public static unsafe void KeepErrors() => XSetErrorHandler(&OnError);

    /// <summary>
    /// Puts in an I/O error handler, so that a lost X connection (the server ends at logout, or crashes) stops only the
    /// shortcuts. Xlib's own ends the whole process, and so does any handler that returns: this one tells
    /// <paramref name="lost"/>, then never returns, which leaves the thread of the shortcuts waiting for good. That thread
    /// is a background one: it does not hold the process at its end.
    /// </summary>
    public static unsafe void SurviveConnectionLoss(Action lost)
    {
        Volatile.Write(ref s_connectionLost, lost);
        XSetIOErrorHandler(&OnConnectionLost);
    }

    /// <summary>The I/O error handler in place, and the one of Xlib put back: for a test, which reads which one was there.</summary>
    internal static unsafe nint SwapToDefaultIOErrorHandler() => XSetIOErrorHandler(null);

    /// <summary>The address of the I/O error handler of the app, as Xlib holds it.</summary>
    internal static unsafe nint ConnectionLostHandler => (nint)(delegate* unmanaged[Cdecl]<nint, int>)&OnConnectionLost;

    /// <summary>
    /// The pipe that wakes the thread of the shortcuts, closed on exec: the programs the app starts later (xdg-open, xclip,
    /// wl-copy) do not hold it. False when it could not be made.
    /// </summary>
    public static unsafe bool WakePipe(out int read, out int write)
    {
        var fds = stackalloc int[2];
        if (pipe2(fds, CloseOnExec) != 0)
        {
            read = write = -1;
            return false;
        }

        read = fds[0];
        write = fds[1];
        return true;
    }

    /// <summary>The code of the last error since the previous call, 0 when none; clears it.</summary>
    public static byte TakeError() => (byte)Interlocked.Exchange(ref _lastError, 0);

    /// <summary>The mask Num Lock sets on this keyboard, from the modifier map; 0 when no modifier holds it.</summary>
    public static unsafe uint NumLockMask(nint display, byte numLockKeycode)
    {
        if (numLockKeycode == 0)
        {
            return 0;
        }

        var map = XGetModifierMapping(display);
        if (map == 0)
        {
            return 0;
        }

        try
        {
            // XModifierKeymap: int max_keypermod; KeyCode *modifiermap (8 modifiers of max_keypermod keycodes each).
            var perModifier = *(int*)map;
            var codes = *(byte**)(map + 8);
            for (var modifier = 0; modifier < 8; modifier++)
            {
                for (var k = 0; k < perModifier; k++)
                {
                    if (codes[(modifier * perModifier) + k] == numLockKeycode)
                    {
                        return 1u << modifier;
                    }
                }
            }

            return 0;
        }
        finally
        {
            XFreeModifiermap(map);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnError(nint display, nint errorEvent)
    {
        Volatile.Write(ref _lastError, Marshal.ReadByte(errorEvent, ErrorCodeOffset));
        return 0;
    }

    // "The called routine should not return. If the I/O error handler does return, the client process exits."
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnConnectionLost(nint display)
    {
        try
        {
            Volatile.Read(ref s_connectionLost)?.Invoke();
        }
        catch (Exception)
        {
            // Nothing may leave a handler called by Xlib; the wait below is what matters.
        }

        Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}
