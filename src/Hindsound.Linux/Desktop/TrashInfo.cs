using System.Globalization;
using System.Text;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// What the Trash specification of freedesktop.org (1.0) fixes about the text of a trash: the .trashinfo file, the way its
/// Path is written, the date, and the names a trash keeps. No file is touched here, so it all runs on any system. Paths are
/// those of Linux, with '/' only, whatever system the code runs on.
/// </summary>
internal static class TrashInfo
{
    public const string InfoSuffix = ".trashinfo";

    /// <summary>The longest a file name can be on the common file systems (ext4, btrfs, xfs), in bytes.</summary>
    private const int NameBytesMax = 255;

    /// <summary>How many names are tried for one file before giving up.</summary>
    private const int NamesTried = 1000;

    /// <summary>
    /// The text of a .trashinfo file, with line ends of LF as the specification writes them: the group, the Path and the
    /// DeletionDate.
    /// </summary>
    /// <param name="path">What the file was called: absolute for the trash of the home, relative to the top directory for another.</param>
    /// <param name="deleted">The local time of the deletion.</param>
    public static string Content(string path, DateTime deleted) =>
        "[Trash Info]\nPath=" + EncodePath(path) + "\nDeletionDate=" + DeletionDate(deleted) + "\n";

    /// <summary>Local time as the specification writes it, YYYY-MM-DDThh:mm:ss, with no zone.</summary>
    public static string DeletionDate(DateTime local) =>
        local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// The Path value: the UTF-8 bytes of the path, each one outside the unreserved characters of RFC 3986 and the slash
    /// written as %XX, so that a space, a '%', a '#' or an accent can't be read as anything else.
    /// </summary>
    public static string EncodePath(string path)
    {
        var text = new StringBuilder(path.Length + 16);
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'/')
            {
                text.Append((char)b);
            }
            else
            {
                text.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The name a file takes in a trash: its own while that is free, else the number before its extension (clip.wav, then
    /// clip.2.wav, clip.3.wav). A name too long to take ".trashinfo" after it is cut at a character, never in the middle of one.
    /// Null when no name is free.
    /// </summary>
    /// <param name="name">The name of the file, with no folder.</param>
    /// <param name="taken">Whether a name is in use in the trash, as a file or as a .trashinfo.</param>
    public static string? UniqueName(string name, Func<string, bool> taken)
    {
        var dot = name.LastIndexOf('.');
        var (stem, extension) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
        for (var number = 1; number <= NamesTried; number++)
        {
            var candidate = Fit(stem, extension, number == 1 ? "" : "." + number.ToString(CultureInfo.InvariantCulture));
            if (candidate is null)
            {
                return null;
            }

            if (!taken(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? Fit(string stem, string extension, string number)
    {
        var room = NameBytesMax - InfoSuffix.Length - Encoding.UTF8.GetByteCount(extension) - Encoding.UTF8.GetByteCount(number);
        if (room < 1)
        {
            return null;
        }

        var kept = new StringBuilder();
        var bytes = 0;
        foreach (var rune in stem.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > room)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.Length == 0 ? null : kept.ToString() + number + extension;
    }

    public static string Join(string folder, string name) => folder.EndsWith('/') ? folder + name : folder + "/" + name;

    /// <summary>The folder holding <paramref name="path"/>; null for the root.</summary>
    public static string? Parent(string path)
    {
        var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 || trimmed == "/" ? null : slash == 0 ? "/" : trimmed[..slash];
    }

    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The Path of a .trashinfo for the file <paramref name="name"/> of <paramref name="folder"/> (a real path): the full path
    /// for the trash of the home (no <paramref name="top"/>), else the path from the top directory, with no leading slash.
    /// </summary>
    public static string OriginalPath(string? top, string folder, string name)
    {
        if (top is null)
        {
            return Join(folder, name);
        }

        var below = top == "/" ? folder[1..] : folder.Length > top.Length ? folder[(top.Length + 1)..] : "";
        return below.Length == 0 ? name : below + "/" + name;
    }
}
