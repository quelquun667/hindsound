using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Mac.Interop;

/// <summary>
/// The few Objective-C calls the Mac app makes from C#, through objc_msgSend cast to the exact C signature of each call (on
/// Apple silicon a call through the wrong prototype reads the wrong registers). An object made here with alloc/init is
/// owned (+1) and given back to <see cref="Release"/>; an object a method returns otherwise is autoreleased and lives as
/// long as the <see cref="Pool"/> around the call.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class ObjC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    private static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(Runtime), "objc_msgSend");

    // Loaded once, so that its classes (NSString, NSURL) are registered with the runtime.
    private static readonly nint FoundationHandle = NativeLibrary.Load(Foundation);

    /// <summary>The class of that name, or 0 when no loaded framework has it.</summary>
    public static nint Class(string name)
    {
        GC.KeepAlive(FoundationHandle);
        return objc_getClass(name);
    }

    public static nint Selector(string name) => sel_registerName(name);

    public static nint Send(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, nint>)MsgSend)(receiver, Selector(selector));

    public static nint Send(nint receiver, string selector, nint argument) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)MsgSend)(receiver, Selector(selector), argument);

    public static nint Send(nint receiver, string selector, nint first, byte second) =>
        ((delegate* unmanaged<nint, nint, nint, byte, nint>)MsgSend)(receiver, Selector(selector), first, second);

    public static nint Send(nint receiver, string selector, ushort* characters, nuint length) =>
        ((delegate* unmanaged<nint, nint, ushort*, nuint, nint>)MsgSend)(receiver, Selector(selector), characters, length);

    public static nuint SendUInt(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, nuint>)MsgSend)(receiver, Selector(selector));

    public static bool SendBool(nint receiver, string selector) =>
        ((delegate* unmanaged<nint, nint, byte>)MsgSend)(receiver, Selector(selector)) != 0;

    public static bool SendBool(nint receiver, string selector, nint argument) =>
        ((delegate* unmanaged<nint, nint, nint, byte>)MsgSend)(receiver, Selector(selector), argument) != 0;

    /// <summary>A new NSString with these UTF-16 code units exactly, owned by the caller.</summary>
    public static nint NSString(string text)
    {
        var allocated = Send(Class("NSString"), "alloc");
        fixed (char* chars = text)
        {
            return Send(allocated, "initWithCharacters:length:", (ushort*)chars, (nuint)text.Length);
        }
    }

    /// <summary>The UTF-16 code units of an NSString, or null for nil.</summary>
    public static string? String(nint nsString)
    {
        if (nsString == 0)
        {
            return null;
        }

        var length = (int)SendUInt(nsString, "length");
        var chars = new char[length];
        fixed (char* buffer = chars)
        {
            ((delegate* unmanaged<nint, nint, ushort*, Range, void>)MsgSend)(nsString, Selector("getCharacters:range:"), (ushort*)buffer, new Range(0, (nuint)length));
        }

        return new string(chars);
    }

    /// <summary>A new file NSURL for an absolute path, owned by the caller; 0 when the path is not absolute.</summary>
    public static nint FileUrl(string path, bool directory = false)
    {
        if (!path.StartsWith('/'))
        {
            return 0;
        }

        var text = NSString(path);
        try
        {
            return Send(Send(Class("NSURL"), "alloc"), "initFileURLWithPath:isDirectory:", text, directory ? (byte)1 : (byte)0);
        }
        finally
        {
            Release(text);
        }
    }

    /// <summary>The text of an NSURL, as <c>absoluteString</c> gives it (percent-encoded, file:///...).</summary>
    public static string? UrlText(nint url)
    {
        using var pool = new Pool();
        return String(Send(url, "absoluteString"));
    }

    public static void Release(nint value)
    {
        if (value != 0)
        {
            Send(value, "release");
        }
    }

    /// <summary>An autorelease pool for the calls in its scope; drained when disposed.</summary>
    public readonly struct Pool : IDisposable
    {
        private readonly nint _pool;

        public Pool() => _pool = objc_autoreleasePoolPush();

        public void Dispose() => objc_autoreleasePoolPop(_pool);
    }

    /// <summary>NSRange: location and length, two NSUInteger.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Range(nuint location, nuint length)
    {
        public readonly nuint Location = location;
        public readonly nuint Length = length;
    }

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(Runtime)]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport(Runtime)]
    private static partial void objc_autoreleasePoolPop(nint pool);
}
