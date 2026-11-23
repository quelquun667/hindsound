using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Ui;

namespace Hindsound.Mac.Interop;

/// <summary>
/// The languages the user put first in the system settings, from <c>CFLocaleCopyPreferredLanguages</c>: BCP-47 tags such as
/// <c>fr-FR</c>, first first. The culture of the process is the invariant one on a Mac, so this is the only place they come from.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacLanguages
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange(nint location, nint length)
    {
        public nint Location = location;
        public nint Length = length;
    }

    /// <summary>Never throws: nothing read gives an empty list.</summary>
    public static IReadOnlyList<string> Preferred()
    {
        try
        {
            var array = CFLocaleCopyPreferredLanguages();
            if (array == 0)
            {
                return [];
            }

            try
            {
                var tags = new List<string?>();
                var count = CFArrayGetCount(array);
                for (nint i = 0; i < count; i++)
                {
                    tags.Add(Text(CFArrayGetValueAtIndex(array, i)));
                }

                return UiLanguages.Clean(tags);
            }
            finally
            {
                // A Copy function: the array is the caller's, and its strings are held by it.
                CFRelease(array);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
    }

    private static string? Text(nint cfString)
    {
        if (cfString == 0)
        {
            return null;
        }

        var length = CFStringGetLength(cfString);
        if (length is <= 0 or > 64)
        {
            return null;
        }

        var chars = stackalloc char[(int)length];
        CFStringGetCharacters(cfString, new CFRange(0, length), chars);
        return new string(chars, 0, (int)length);
    }

    [LibraryImport(CoreFoundation)]
    private static partial nint CFLocaleCopyPreferredLanguages();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundation)]
    private static partial void CFStringGetCharacters(nint text, CFRange range, char* buffer);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
