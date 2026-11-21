using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Mac.Notification;

/// <summary>
/// A click on the notification leaves the app in front where it was, as on Windows. A plain NSWindow activates its app on a
/// mouse-down (seen in the proof of M2b: <c>focus.after_click app_active=1</c>); AppKit's public way out, the one a view that
/// drags without activating uses, is <c>shouldDelayWindowOrderingForEvent:</c> answering YES and
/// <c>[NSApp preventWindowOrdering]</c> in <c>mouseDown:</c>. SDL's content view gets them through a subclass of its own class
/// made at run time, with no instance variable added (the way key-value observing subclasses an object), and
/// <c>acceptsFirstMouse:</c> YES so that the click reaches the notification although its window is never key.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class NonActivatingClicks
{
    /// <summary>The start of the name of each subclass, followed by the name of the class it extends.</summary>
    public const string Prefix = "HindsoundNonActivating_";

    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, nint> Made = [];

    /// <summary>
    /// Gives <paramref name="view"/> the clicks that do not activate the app. False, and the view untouched, when the runtime
    /// refuses: the notification then works as before, with the app made active by a click.
    /// </summary>
    public static bool Apply(nint view)
    {
        if (view == 0)
        {
            return false;
        }

        var original = object_getClass(view);
        if (original == 0)
        {
            return false;
        }

        if (Marshal.PtrToStringUTF8(class_getName(original)) is { } name && name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return true;
        }

        var subclass = SubclassOf(original);
        if (subclass == 0)
        {
            return false;
        }

        object_setClass(view, subclass);
        return true;
    }

    private static nint SubclassOf(nint original)
    {
        lock (Gate)
        {
            if (Made.TryGetValue(original, out var known))
            {
                return known;
            }

            var name = Prefix + Marshal.PtrToStringUTF8(class_getName(original));
            var subclass = objc_getClass(name);
            if (subclass == 0)
            {
                subclass = objc_allocateClassPair(original, name, 0);
                if (subclass == 0)
                {
                    return 0;
                }

                var added = class_addMethod(subclass, sel_registerName("shouldDelayWindowOrderingForEvent:"), (nint)(delegate* unmanaged<nint, nint, nint, byte>)&Yes, "B@:@") != 0
                    && class_addMethod(subclass, sel_registerName("acceptsFirstMouse:"), (nint)(delegate* unmanaged<nint, nint, nint, byte>)&Yes, "B@:@") != 0
                    && class_addMethod(subclass, sel_registerName("mouseDown:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&MouseDown, "v@:@") != 0;
                if (!added)
                {
                    objc_disposeClassPair(subclass);
                    return 0;
                }

                objc_registerClassPair(subclass);
            }

            Made[original] = subclass;
            return subclass;
        }
    }

    [UnmanagedCallersOnly]
    private static byte Yes(nint self, nint selector, nint ev) => 1;

    /// <summary>No ordering and no activation for this mouse-down, then the view's own handling of it, which SDL reads.</summary>
    [UnmanagedCallersOnly]
    private static void MouseDown(nint self, nint selector, nint ev)
    {
        var application = ((delegate* unmanaged<nint, nint, nint>)MsgSend)(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        if (application != 0)
        {
            ((delegate* unmanaged<nint, nint, void>)MsgSend)(application, sel_registerName("preventWindowOrdering"));
        }

        // The class above this one, found from the subclass made here even if something subclassed the object again since.
        var cls = object_getClass(self);
        while (cls != 0 && Marshal.PtrToStringUTF8(class_getName(cls)) is { } name && !name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            cls = class_getSuperclass(cls);
        }

        var super = new ObjcSuper(self, class_getSuperclass(cls));
        ((delegate* unmanaged<ObjcSuper*, nint, nint, void>)MsgSendSuper)(&super, selector, ev);
    }

    private static readonly nint Library = NativeLibrary.Load(Runtime);
    private static readonly nint MsgSend = NativeLibrary.GetExport(Library, "objc_msgSend");
    private static readonly nint MsgSendSuper = NativeLibrary.GetExport(Library, "objc_msgSendSuper");

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ObjcSuper(nint receiver, nint superClass)
    {
        public readonly nint Receiver = receiver;
        public readonly nint SuperClass = superClass;
    }

    [LibraryImport(Runtime)]
    private static partial nint object_getClass(nint value);

    [LibraryImport(Runtime)]
    private static partial nint object_setClass(nint value, nint cls);

    [LibraryImport(Runtime)]
    private static partial nint class_getName(nint cls);

    [LibraryImport(Runtime)]
    private static partial nint class_getSuperclass(nint cls);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(Runtime)]
    private static partial void objc_registerClassPair(nint cls);

    [LibraryImport(Runtime)]
    private static partial void objc_disposeClassPair(nint cls);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial byte class_addMethod(nint cls, nint name, nint implementation, string types);
}
