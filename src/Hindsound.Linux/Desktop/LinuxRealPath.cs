using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The one real path of the app: <c>realpath(3)</c> of the C library, every link followed at each component, ".." taken after
/// the links before it, a loop refused (ELOOP). The trash and the entry of the menu both ask it.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class LinuxRealPath
{
    private const string LibC = "libc.so.6";

    /// <summary>The real path of <paramref name="path"/>, or null when it does not exist, is a loop or cannot be read.</summary>
    public static string? Of(string path)
    {
        var resolved = RealPathC(path, 0);
        if (resolved == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            Free(resolved);
        }
    }

    [LibraryImport(LibC, EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint RealPathC(string path, nint resolved);

    [LibraryImport(LibC, EntryPoint = "free")]
    private static partial void Free(nint memory);
}
