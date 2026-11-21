using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Mac.Window;

/// <summary>
/// The fallback font of the text on macOS: CoreText picks the font of the system for a character (CTFontCreateForString,
/// as the text of the system does), and its file is given to FreeType, with the face of that font in a collection (.ttc)
/// found by its PostScript name. Used on the loop thread only.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class CoreTextFallback : IFontFallback
{
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int PathBytes = 4096;

    private nint _regular;
    private nint _bold;
    private nint _freeType;
    private bool _disposed;

    public void CheckLoadable()
    {
        if (!NativeLibrary.TryLoad(CoreText, out _))
        {
            throw new TextUnavailableException("CoreText cannot be loaded.");
        }
    }

    public (byte[] Path, int Index)? Find(int codePoint, int weight)
    {
        if (_disposed || !Rune.IsValid(codePoint))
        {
            return null;
        }

        var baseFont = BaseFont(weight >= 180);
        if (baseFont == 0)
        {
            return null;
        }

        var text = new Rune(codePoint).ToString();
        var str = CFString(text);
        nint found = 0;
        try
        {
            found = CTFontCreateForString(baseFont, str, new CFRange(0, text.Length));
            if (found == 0)
            {
                return null;
            }

            var path = FileOf(found);
            if (path is null)
            {
                return null;
            }

            var postScript = Text(CTFontCopyPostScriptName(found), release: true);
            return (path, FaceIndex(path, postScript));
        }
        finally
        {
            if (found != 0)
            {
                CFRelease(found);
            }

            CFRelease(str);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var font in new[] { _regular, _bold })
        {
            if (font != 0)
            {
                CFRelease(font);
            }
        }

        if (_freeType != 0)
        {
            TextNative.FT_Done_FreeType(_freeType);
        }
    }

    /// <summary>The PostScript name and the file of the font CoreText picks for <paramref name="text"/>, for the tests.</summary>
    internal (string? PostScript, string? File) Describe(string text, bool bold)
    {
        var str = CFString(text);
        var found = CTFontCreateForString(BaseFont(bold), str, new CFRange(0, text.Length));
        CFRelease(str);
        if (found == 0)
        {
            return (null, null);
        }

        var path = FileOf(found);
        var name = Text(CTFontCopyPostScriptName(found), release: true);
        CFRelease(found);
        return (name, path is null ? null : Encoding.UTF8.GetString(path, 0, path.Length - 1));
    }

    private nint BaseFont(bool bold)
    {
        ref var font = ref bold ? ref _bold : ref _regular;
        if (font == 0)
        {
            var name = CFString(bold ? "Helvetica-Bold" : "Helvetica");
            font = CTFontCreateWithName(name, 14, 0);
            CFRelease(name);
        }

        return font;
    }

    // The file of the font as the system's bytes, ended by a zero, as FontFile.Open takes them.
    private static byte[]? FileOf(nint font)
    {
        var attribute = *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(CoreText), "kCTFontURLAttribute");
        var url = CTFontCopyAttribute(font, attribute);
        if (url == 0)
        {
            return null;
        }

        try
        {
            var buffer = new byte[PathBytes];
            fixed (byte* bytes = buffer)
            {
                if (CFURLGetFileSystemRepresentation(url, 1, bytes, PathBytes) == 0)
                {
                    return null;
                }

                var length = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(bytes).Length;
                return buffer.AsSpan(0, length + 1).ToArray();
            }
        }
        finally
        {
            CFRelease(url);
        }
    }

    // A collection holds several faces: the one CoreText picked is the face whose PostScript name it gave. 0 when the file
    // has one face, or when no face matches (FreeType then draws the first one).
    private int FaceIndex(byte[] path, string? postScript)
    {
        if (postScript is null)
        {
            return 0;
        }

        // The FreeType of the app, never the runtime's own search for the Linux name (which finds nothing on macOS): this
        // fallback may be asked before any FreeTypeText has installed the resolver.
        Hindsound.Unix.Window.UnixLibraries.InstallResolver();
        if (_freeType == 0 && TextNative.FT_Init_FreeType(out _freeType) != 0)
        {
            _freeType = 0;
            return 0;
        }

        fixed (byte* file = path)
        {
            if (TextNative.FT_New_Face(_freeType, file, -1, out var probe) != 0)
            {
                return 0;
            }

            // FT_FaceRec begins with num_faces, an FT_Long.
            var faces = (int)Math.Min(*(long*)probe, 64);
            TextNative.FT_Done_Face(probe);
            for (var i = 0; faces > 1 && i < faces; i++)
            {
                if (TextNative.FT_New_Face(_freeType, file, i, out var face) != 0)
                {
                    continue;
                }

                var name = Marshal.PtrToStringUTF8((nint)FT_Get_Postscript_Name(face));
                TextNative.FT_Done_Face(face);
                if (name == postScript)
                {
                    return i;
                }
            }
        }

        return 0;
    }

    private static nint CFString(string text)
    {
        fixed (char* chars = text)
        {
            return CFStringCreateWithCharacters(0, (ushort*)chars, text.Length);
        }
    }

    private static string? Text(nint cfString, bool release)
    {
        if (cfString == 0)
        {
            return null;
        }

        try
        {
            var length = CFStringGetLength(cfString);
            var chars = new char[length];
            fixed (char* buffer = chars)
            {
                CFStringGetCharacters(cfString, new CFRange(0, length), (ushort*)buffer);
            }

            return new string(chars);
        }
        finally
        {
            if (release)
            {
                CFRelease(cfString);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    [LibraryImport(CoreText)]
    private static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

    [LibraryImport(CoreText)]
    private static partial nint CTFontCreateForString(nint current, nint text, CFRange range);

    [LibraryImport(CoreText)]
    private static partial nint CTFontCopyPostScriptName(nint font);

    [LibraryImport(CoreText)]
    private static partial nint CTFontCopyAttribute(nint font, nint attribute);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringCreateWithCharacters(nint allocator, ushort* chars, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundation)]
    private static partial void CFStringGetCharacters(nint text, CFRange range, ushort* buffer);

    [LibraryImport(CoreFoundation)]
    private static partial byte CFURLGetFileSystemRepresentation(nint url, byte resolveAgainstBase, byte* buffer, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);

    // Through the resolver of Hindsound.Unix: the same libfreetype.6.dylib of the bundle the text uses.
    private static byte* FT_Get_Postscript_Name(nint face) =>
        ((delegate* unmanaged<nint, byte*>)NativeLibrary.GetExport(Hindsound.Unix.Window.UnixLibraries.MacHandle(TextNative.FreeType), "FT_Get_Postscript_Name"))(face);
}
