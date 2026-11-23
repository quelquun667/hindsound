using System.Runtime.InteropServices;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>The languages the user put first in the Windows settings, for the interface to follow when its setting says so.</summary>
internal static unsafe partial class WindowsLanguages
{
    private const uint MuiLanguageName = 0x8;
    private const int LocaleNameMaxLength = 85;

    /// <summary>
    /// <c>GetUserPreferredUILanguages</c>, as names; when it gives none, the default language of the interface. Never the culture
    /// of the process, which another setting decides. Never throws: nothing read gives an empty list.
    /// </summary>
    public static IReadOnlyList<string> Preferred()
    {
        try
        {
            return FromPreferred() is { Count: > 0 } preferred ? preferred : FromDefault();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> FromPreferred()
    {
        uint length = 0;
        if (!GetUserPreferredUILanguages(MuiLanguageName, out _, null, ref length) || length == 0)
        {
            return [];
        }

        var buffer = new char[length];
        fixed (char* chars = buffer)
        {
            if (!GetUserPreferredUILanguages(MuiLanguageName, out _, chars, ref length))
            {
                return [];
            }
        }

        return UiLanguages.FromMultiString(buffer.AsSpan(0, (int)Math.Min(length, (uint)buffer.Length)));
    }

    private static IReadOnlyList<string> FromDefault()
    {
        var name = stackalloc char[LocaleNameMaxLength];
        var written = LCIDToLocaleName(GetUserDefaultUILanguage(), name, LocaleNameMaxLength, 0);
        return written > 1 ? UiLanguages.Clean([new string(name, 0, written - 1)]) : [];
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserPreferredUILanguages(uint flags, out uint count, char* buffer, ref uint length);

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();

    [LibraryImport("kernel32.dll")]
    private static partial int LCIDToLocaleName(uint locale, char* name, int size, uint flags);
}
