using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.App;

/// <summary>
/// The AppKit calls of the loop and of the icon of the menu bar, each through objc_msgSend cast to its exact C signature, as
/// <see cref="ObjC"/> does for Foundation. AppKit is loaded once, so that NSApplication, NSStatusBar and NSMenu exist.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class Cocoa
{
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string System = "/usr/lib/libSystem.B.dylib";

    /// <summary>NSEventTypeApplicationDefined.</summary>
    public const nuint ApplicationDefined = 15;

    /// <summary>NSApplicationActivationPolicyAccessory: an app of the menu bar, with no Dock icon and no menu of its own.</summary>
    public const nint AccessoryPolicy = 1;

    private static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(Runtime), "objc_msgSend");
    private static readonly nint AppKitHandle = NativeLibrary.Load(AppKit);

    /// <summary>Whether the calling thread is the main thread of the process, the only one AppKit may be driven from.</summary>
    public static bool OnMainThread => pthread_main_np() != 0;

    /// <summary>The class of that name, AppKit included; 0 when none has it.</summary>
    public static nint Class(string name)
    {
        GC.KeepAlive(AppKitHandle);
        return ObjC.Class(name);
    }

    /// <summary>The shared NSApplication, made on first use: call it on the main thread.</summary>
    public static nint SharedApplication() => ObjC.Send(Class("NSApplication"), "sharedApplication");

    public static nint Send(nint receiver, string selector, nint first, nint second, nint third) =>
        ((delegate* unmanaged<nint, nint, nint, nint, nint, nint>)MsgSend)(receiver, ObjC.Selector(selector), first, second, third);

    public static nint Send(nint receiver, string selector, double value) =>
        ((delegate* unmanaged<nint, nint, double, nint>)MsgSend)(receiver, ObjC.Selector(selector), value);

    public static nint SendInt(nint receiver, string selector, nint value) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)MsgSend)(receiver, ObjC.Selector(selector), value);

    public static void SendFlag(nint receiver, string selector, bool value) =>
        ((delegate* unmanaged<nint, nint, byte, void>)MsgSend)(receiver, ObjC.Selector(selector), value ? (byte)1 : (byte)0);

    public static nint ReadInt(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, nint>)MsgSend)(receiver, ObjC.Selector(selector));

    public static double ReadDouble(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, double>)MsgSend)(receiver, ObjC.Selector(selector));

    /// <summary>-[NSApplication nextEventMatchingMask:untilDate:inMode:dequeue:].</summary>
    public static nint NextEvent(nint app, nint until, nint mode) =>
        ((delegate* unmanaged<nint, nint, nuint, nint, nint, byte, nint>)MsgSend)(app, ObjC.Selector("nextEventMatchingMask:untilDate:inMode:dequeue:"), nuint.MaxValue, until, mode, 1);

    /// <summary>-[NSApplication postEvent:atStart:], which AppKit allows from any thread.</summary>
    public static void PostEvent(nint app, nint nsEvent) =>
        ((delegate* unmanaged<nint, nint, nint, byte, void>)MsgSend)(app, ObjC.Selector("postEvent:atStart:"), nsEvent, 0);

    /// <summary>An autoreleased application-defined NSEvent carrying <paramref name="data1"/>.</summary>
    public static nint ApplicationEvent(nint data1) =>
        ((delegate* unmanaged<nint, nint, nuint, Size, nuint, double, nint, nint, short, nint, nint, nint>)MsgSend)(
            Class("NSEvent"),
            ObjC.Selector("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
            ApplicationDefined, default, 0, 0, 0, 0, 0, data1, 0);

    /// <summary>A new NSBitmapImageRep of RGBA bytes, 8 bits a sample, alpha not premultiplied into nothing (owned).</summary>
    public static nint NewBitmap(int width, int height, nint colorSpaceName) =>
        ((delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, byte, byte, nint, nint, nint, nint>)MsgSend)(
            ObjC.Send(Class("NSBitmapImageRep"), "alloc"),
            ObjC.Selector("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bytesPerRow:bitsPerPixel:"),
            0, width, height, 8, 4, 1, 0, colorSpaceName, width * 4, 32);

    /// <summary>A message that takes one NSSize (two doubles, passed in floating-point registers on Apple silicon).</summary>
    public static nint SendSize(nint receiver, string selector, double width, double height) =>
        ((delegate* unmanaged<nint, nint, Size, nint>)MsgSend)(receiver, ObjC.Selector(selector), new Size(width, height));

    public static Size ReadSize(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, Size>)MsgSend)(receiver, ObjC.Selector(selector));

    /// <summary>A global NSString constant of AppKit or Foundation, such as NSDefaultRunLoopMode.</summary>
    public static nint Constant(string name) => *(nint*)NativeLibrary.GetExport(AppKitHandle, name);

    /// <summary>
    /// A new Objective-C class, subclass of NSObject, whose methods take one object and return nothing (<c>v@:@</c>), each
    /// calling back into C#. Registered once per process under its name.
    /// </summary>
    public static nint DefineClass(string name, IReadOnlyList<(string Selector, nint Implementation)> methods)
    {
        if (ObjC.Class(name) is var existing and not 0)
        {
            return existing;
        }

        var created = objc_allocateClassPair(ObjC.Class("NSObject"), name, 0);
        if (created == 0)
        {
            throw new InvalidOperationException("objc_allocateClassPair failed for " + name);
        }

        foreach (var (selector, implementation) in methods)
        {
            if (class_addMethod(created, ObjC.Selector(selector), implementation, "v@:@") == 0)
            {
                throw new InvalidOperationException("class_addMethod failed for " + selector);
            }
        }

        objc_registerClassPair(created);
        return created;
    }

    /// <summary>NSSize, NSPoint: two CGFloat.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Size(double width, double height)
    {
        public readonly double Width = width;
        public readonly double Height = height;
    }

    [LibraryImport(System)]
    private static partial int pthread_main_np();

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial byte class_addMethod(nint cls, nint name, nint implementation, string types);

    [LibraryImport(Runtime)]
    private static partial void objc_registerClassPair(nint cls);
}
