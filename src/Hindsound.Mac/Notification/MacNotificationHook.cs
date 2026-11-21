using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Unix.Notification;

namespace Hindsound.Mac.Notification;

/// <summary>
/// What the NSWindow of the notification needs beyond SDL's flags: above the menu bar's level of floating windows, on every
/// Space and over an app in full screen, out of the window cycle, still there when the app is not the active one, clicks that
/// leave the app in front where it was (<see cref="NonActivatingClicks"/>), and clicks passing through it when the app asks.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacNotificationHook : INotificationWindowHook
{
    /// <summary>NSWindowCollectionBehavior: CanJoinAllSpaces (1 &lt;&lt; 0), Stationary (1 &lt;&lt; 4), IgnoresCycle (1 &lt;&lt; 6), FullScreenAuxiliary (1 &lt;&lt; 8).</summary>
    internal const nuint Behavior = (1 << 0) | (1 << 4) | (1 << 6) | (1 << 8);

    /// <summary>NSStatusWindowLevel: above the floating windows SDL's "always on top" gives.</summary>
    internal const nint Level = 25;

    public string NativeWindowProperty => "SDL.window.cocoa.window";

    public void Created(nint nativeWindow)
    {
        Messages.SendUInt(nativeWindow, "setCollectionBehavior:", Behavior);
        Messages.SendInt(nativeWindow, "setLevel:", Level);
        Messages.SendBool(nativeWindow, "setHidesOnDeactivate:", false);
        NonActivatingClicks.ApplyToTree(Messages.Send(nativeWindow, "contentView"));
    }

    public void ClickThrough(nint nativeWindow, bool on)
    {
        Messages.SendBool(nativeWindow, "setIgnoresMouseEvents:", on);

        // Again at each opening: the view of the renderer is added under the content view after the window is made.
        NonActivatingClicks.ApplyToTree(Messages.Send(nativeWindow, "contentView"));
    }

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
