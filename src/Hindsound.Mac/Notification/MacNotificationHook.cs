using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Unix.Notification;

namespace Hindsound.Mac.Notification;

/// <summary>
/// What the NSWindow of the notification needs beyond SDL's flags: above the menu bar's level of floating windows, on every
/// Space and over an app in full screen, out of the window cycle, still there when the app is not the active one, a click
/// that does not bring the app forward, and clicks passing through it when the app asks.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacNotificationHook : INotificationWindowHook
{
    public string NativeWindowProperty => "SDL.window.cocoa.window";

    public void Created(nint nativeWindow)
    {
        foreach (var message in MacNotificationWindow.ToSend(selector => Messages.RespondsTo(nativeWindow, selector)))
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

        /// <summary>Whether the objects of a class answer the message: instancesRespondToSelector:.</summary>
        public static bool InstancesRespondTo(string className, string selector) =>
            RespondsTo(Class(className), "instancesRespondToSelector:") && ((delegate* unmanaged<nint, nint, nint, byte>)MsgSend)(Class(className), Selector("instancesRespondToSelector:"), Selector(selector)) != 0;

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
    /// What a non-activating NSPanel sets on its window: a click on it is delivered without making the app the active one.
    /// SDL makes an NSWindow, which has the method without the style; an AppKit without it leaves the click as it was.
    /// </summary>
    internal const string PreventsActivation = "_setPreventsActivation:";

    /// <summary>
    /// The messages sent to the NSWindow when it is made, in order. The last one does what WM_MOUSEACTIVATE answered with
    /// MA_NOACTIVATE does on Windows: the click on the notification is its action, and the app in front (a game, a call)
    /// stays in front; only a window the action opens comes forward, as on Windows. Measured on the runner before it (run
    /// 45029672018): a click on the notification made Hindsound the app in front, in place of the Finder.
    /// </summary>
    internal static readonly IReadOnlyList<WindowMessage> OnCreate =
    [
        new("setCollectionBehavior:", (nint)Behavior, WindowArgument.UInt),
        new("setLevel:", Level, WindowArgument.Int),
        new("setHidesOnDeactivate:", 0, WindowArgument.Bool),
        new(PreventsActivation, 1, WindowArgument.Bool, OnlyIfAnswered: true),
    ];

    /// <summary>
    /// The messages of <see cref="OnCreate"/> the window gets, in order: a message that is not public only when
    /// <paramref name="answers"/> (respondsToSelector: of the window) says the window has it, so an AppKit without it gets
    /// nothing it would refuse.
    /// </summary>
    internal static IEnumerable<WindowMessage> ToSend(Func<string, bool> answers) =>
        OnCreate.Where(message => !message.OnlyIfAnswered || answers(message.Selector));
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
/// <param name="OnlyIfAnswered">Sent only when the window answers it: a method of AppKit that is not public.</param>
internal readonly record struct WindowMessage(string Selector, nint Value, WindowArgument Kind, bool OnlyIfAnswered = false);
