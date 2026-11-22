using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Unix.Notification;

namespace Hindsound.Mac.Notification;

/// <summary>
/// What the NSWindow of the notification needs beyond SDL's flags: above the menu bar's level of floating windows, on every
/// Space and over an app in full screen, out of the window cycle, still there when the app is not the active one, and clicks
/// passing through it when the app asks.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacNotificationHook : INotificationWindowHook
{
    public string NativeWindowProperty => "SDL.window.cocoa.window";

    public void Created(nint nativeWindow)
    {
        foreach (var message in MacNotificationWindow.OnCreate)
        {
            switch (message.Kind)
            {
                case WindowArgument.UInt:
                    Messages.SendUInt(nativeWindow, message.Selector, (nuint)message.Value);
                    break;
                case WindowArgument.Int:
                    Messages.SendInt(nativeWindow, message.Selector, message.Value);
                    break;
                default:
                    Messages.SendBool(nativeWindow, message.Selector, message.Value != 0);
                    break;
            }
        }
    }

    public void ClickThrough(nint nativeWindow, bool on) => Messages.SendBool(nativeWindow, "setIgnoresMouseEvents:", on);

    /// <summary>objc_msgSend cast to the exact prototype of each call, as the Objective-C calls of the app do.</summary>
    internal static class Messages
    {
        private const string Runtime = "/usr/lib/libobjc.A.dylib";
        private static readonly nint Library = NativeLibrary.Load(Runtime);
        private static readonly nint MsgSend = NativeLibrary.GetExport(Library, "objc_msgSend");
        private static readonly delegate* unmanaged<byte*, nint> RegisterName = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(Library, "sel_registerName");
        private static readonly delegate* unmanaged<byte*, nint> GetClass = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(Library, "objc_getClass");

        public static nint Class(string name)
        {
            fixed (byte* utf8 = Utf8(name))
            {
                return GetClass(utf8);
            }
        }

        public static nint Selector(string name)
        {
            fixed (byte* utf8 = Utf8(name))
            {
                return RegisterName(utf8);
            }
        }

        public static nint Send(nint receiver, string selector) =>
            ((delegate* unmanaged<nint, nint, nint>)MsgSend)(receiver, Selector(selector));

        public static int SendInt32(nint receiver, string selector) =>
            ((delegate* unmanaged<nint, nint, int>)MsgSend)(receiver, Selector(selector));

        public static bool SendBool(nint receiver, string selector) =>
            ((delegate* unmanaged<nint, nint, byte>)MsgSend)(receiver, Selector(selector)) != 0;

        public static void SendUInt(nint receiver, string selector, nuint value) =>
            ((delegate* unmanaged<nint, nint, nuint, void>)MsgSend)(receiver, Selector(selector), value);

        public static void SendInt(nint receiver, string selector, nint value) =>
            ((delegate* unmanaged<nint, nint, nint, void>)MsgSend)(receiver, Selector(selector), value);

        public static void SendBool(nint receiver, string selector, bool value) =>
            ((delegate* unmanaged<nint, nint, byte, void>)MsgSend)(receiver, Selector(selector), value ? (byte)1 : (byte)0);

        /// <summary>Whether the object answers the message: respondsToSelector:.</summary>
        public static bool RespondsTo(nint receiver, string selector) =>
            receiver != 0 && ((delegate* unmanaged<nint, nint, nint, byte>)MsgSend)(receiver, Selector("respondsToSelector:"), Selector(selector)) != 0;

        /// <summary>The UTF-8 text of an NSString, or null.</summary>
        public static string? Text(nint nsString)
        {
            if (nsString == 0)
            {
                return null;
            }

            var utf8 = (byte*)Send(nsString, "UTF8String");
            return utf8 == null ? null : Marshal.PtrToStringUTF8((nint)utf8);
        }

        private static byte[] Utf8(string value) => System.Text.Encoding.UTF8.GetBytes(value + "\0");
    }
}

/// <summary>
/// What <see cref="MacNotificationHook"/> sends to the NSWindow of the notification, kept apart from the calls to AppKit: a
/// test reads it on any system.
/// </summary>
internal static class MacNotificationWindow
{
    /// <summary>NSWindowCollectionBehavior: CanJoinAllSpaces (1 &lt;&lt; 0), Stationary (1 &lt;&lt; 4), IgnoresCycle (1 &lt;&lt; 6), FullScreenAuxiliary (1 &lt;&lt; 8).</summary>
    internal const nuint Behavior = (1 << 0) | (1 << 4) | (1 << 6) | (1 << 8);

    /// <summary>NSStatusWindowLevel: above the floating windows SDL's "always on top" gives.</summary>
    internal const nint Level = 25;

    /// <summary>
    /// The messages sent to the NSWindow when it is made, in order. A click on the notification makes Hindsound the active app
    /// (the CI showed that no message to the window prevents it), and the window the click opens comes in front, as on Windows.
    /// </summary>
    internal static readonly IReadOnlyList<WindowMessage> OnCreate =
    [
        new("setCollectionBehavior:", (nint)Behavior, WindowArgument.UInt),
        new("setLevel:", Level, WindowArgument.Int),
        new("setHidesOnDeactivate:", 0, WindowArgument.Bool),
    ];
}

/// <summary>The type of the one argument of a message to the NSWindow.</summary>
internal enum WindowArgument
{
    /// <summary>An NSUInteger.</summary>
    UInt,

    /// <summary>An NSInteger.</summary>
    Int,

    /// <summary>A BOOL: 0 or 1.</summary>
    Bool,
}

/// <param name="Selector">The message.</param>
/// <param name="Value">Its argument.</param>
/// <param name="Kind">The type of the argument.</param>
internal readonly record struct WindowMessage(string Selector, nint Value, WindowArgument Kind);
