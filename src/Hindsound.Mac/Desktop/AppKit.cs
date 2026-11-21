using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Desktop;

/// <summary>
/// What the desktop parts call beyond <see cref="ObjC"/>: AppKit loaded (NSPasteboard, NSWorkspace, NSScreen, NSEvent), the
/// shapes of objc_msgSend they need (each cast to the exact C signature, as on Apple silicon a wrong prototype reads the
/// wrong registers), and the window list of CoreGraphics.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class AppKit
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string AppKitPath = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements.</summary>
    public const uint OnScreenWindows = 1 | 16;

    private static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(Runtime), "objc_msgSend");

    // Loaded once, so that its classes are registered with the runtime.
    private static readonly nint AppKitHandle = NativeLibrary.Load(AppKitPath);

    /// <summary>The class of that name, AppKit's included; 0 when no loaded framework has it.</summary>
    public static nint Class(string name)
    {
        GC.KeepAlive(AppKitHandle);
        return ObjC.Class(name);
    }

    public static int SendInt(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, int>)MsgSend)(receiver, ObjC.Selector(selector));

    public static double SendDouble(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, double>)MsgSend)(receiver, ObjC.Selector(selector));

    public static CGRect SendRect(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, CGRect>)MsgSend)(receiver, ObjC.Selector(selector));

    public static CGPoint SendPoint(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, CGPoint>)MsgSend)(receiver, ObjC.Selector(selector));

    public static nint SendAt(nint receiver, string selector, nuint index) =>
        ((delegate* unmanaged<nint, nint, nuint, nint>)MsgSend)(receiver, ObjC.Selector(selector), index);

    /// <summary>+[NSArray arrayWithObjects:count:]: an autoreleased array that holds its own reference to each object.</summary>
    public static nint Array(nint* objects, nuint count) =>
        ((delegate* unmanaged<nint, nint, nint*, nuint, nint>)MsgSend)(Class("NSArray"), ObjC.Selector("arrayWithObjects:count:"), objects, count);

    /// <summary>-[NSFileManager trashItemAtURL:resultingItemURL:error:].</summary>
    public static bool Trash(nint manager, nint url, nint* resulting, nint* error) =>
        ((delegate* unmanaged<nint, nint, nint, nint*, nint*, byte>)MsgSend)(manager, ObjC.Selector("trashItemAtURL:resultingItemURL:error:"), url, resulting, error) != 0;

    /// <summary>The value of <paramref name="key"/> in an NSDictionary (or a CFDictionary, the same object); nil when absent.</summary>
    public static nint Get(nint dictionary, string key)
    {
        var text = ObjC.NSString(key);
        try
        {
            return ObjC.Send(dictionary, "objectForKey:", text);
        }
        finally
        {
            ObjC.Release(text);
        }
    }

    /// <summary>The windows on screen, front to back, as CFDictionaries; owned by the caller (<see cref="CFRelease"/>). 0 when refused.</summary>
    [LibraryImport(CoreGraphics)]
    public static partial nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [LibraryImport(CoreFoundation)]
    public static partial void CFRelease(nint value);
}

/// <summary>CGPoint: two CGFloat, 64-bit on Apple silicon.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGPoint(double x, double y)
{
    public readonly double X = x;
    public readonly double Y = y;
}

/// <summary>CGRect: its origin and its size, four CGFloat.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGRect(double x, double y, double width, double height)
{
    public readonly double X = x;
    public readonly double Y = y;
    public readonly double Width = width;
    public readonly double Height = height;
}
