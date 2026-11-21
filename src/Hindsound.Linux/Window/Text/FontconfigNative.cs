using System.Runtime.InteropServices;

namespace Hindsound.Linux.Window.Text;

/// <summary>The calls of fontconfig the fallback of the Linux text makes (<see cref="FontFallback"/>), none newer than those
/// of Ubuntu 22.04.</summary>
internal static unsafe partial class FontconfigNative
{
    public const string Fontconfig = "libfontconfig.so.1";

    // FcResultMatch, FcMatchPattern.
    public const int FcResultMatch = 0;
    public const int FcMatchPattern = 0;

    [LibraryImport(Fontconfig)]
    public static partial nint FcInitLoadConfigAndFonts();

    [LibraryImport(Fontconfig)]
    public static partial void FcConfigDestroy(nint config);

    [LibraryImport(Fontconfig)]
    public static partial nint FcPatternCreate();

    [LibraryImport(Fontconfig)]
    public static partial void FcPatternDestroy(nint pattern);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternAddCharSet(nint pattern, string obj, nint charset);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternAddBool(nint pattern, string obj, int value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternAddInteger(nint pattern, string obj, int value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetCharSet(nint pattern, string obj, int n, out nint charset);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetString(nint pattern, string obj, int n, out byte* value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetInteger(nint pattern, string obj, int n, out int value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetBool(nint pattern, string obj, int n, out int value);

    [LibraryImport(Fontconfig)]
    public static partial nint FcCharSetCreate();

    [LibraryImport(Fontconfig)]
    public static partial int FcCharSetAddChar(nint charset, uint ucs4);

    [LibraryImport(Fontconfig)]
    public static partial int FcCharSetHasChar(nint charset, uint ucs4);

    [LibraryImport(Fontconfig)]
    public static partial void FcCharSetDestroy(nint charset);

    [LibraryImport(Fontconfig)]
    public static partial int FcConfigSubstitute(nint config, nint pattern, int kind);

    [LibraryImport(Fontconfig)]
    public static partial void FcDefaultSubstitute(nint pattern);

    [LibraryImport(Fontconfig)]
    public static partial nint FcFontSort(nint config, nint pattern, int trim, nint charsets, out int result);

    [LibraryImport(Fontconfig)]
    public static partial void FcFontSetDestroy(nint fontSet);
}
