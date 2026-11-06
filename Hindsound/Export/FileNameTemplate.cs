using System.IO;
using System.Text;

namespace Hindsound.Export;

/// <summary>Builds file names from the template <c>Hindsound_{app}_{date}_{time}</c>.</summary>
public static class FileNameTemplate
{
    /// <summary>Used when <c>{app}</c> is empty, or empty once the forbidden characters are removed.</summary>
    public const string UnknownApp = "Unknown";

    private const int MaxAppLength = 40;
    private const int MaxNameLength = 150;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Fills <c>{app}</c> (forbidden characters removed), <c>{date}</c> (<c>yyyy-MM-dd</c>) and <c>{time}</c>
    /// (<c>HH-mm-ss</c>, no colon since Windows forbids it), then removes whatever Windows does not allow in a file
    /// name. The result has no extension and cannot contain a folder separator.
    /// </summary>
    public static string Render(string? template, string? app, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(template)) template = ExportOptions.DefaultFileNameTemplate;

        var cleanApp = Sanitize(app ?? string.Empty);
        if (cleanApp.Length > MaxAppLength) cleanApp = cleanApp[..MaxAppLength].TrimEnd(' ', '.');
        if (cleanApp.Length == 0) cleanApp = UnknownApp;

        var name = template
            .Replace("{app}", cleanApp, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HH-mm-ss"), StringComparison.OrdinalIgnoreCase);

        name = Sanitize(name);
        if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd(' ', '.');
        if (name.Length == 0) name = "Hindsound";
        if (ReservedNames.Contains(name)) name = "_" + name;
        return name;
    }

    /// <summary>
    /// Returns the full paths for one clip, one per suffix, such that none of them exists yet. If the plain name is
    /// taken, " (2)", " (3)"... is added before the suffix, the same number for every file of the clip.
    /// </summary>
    /// <param name="suffixes">One entry per file, for example <c>""</c> or <c>"_system"</c> and <c>"_mic"</c>.</param>
    /// <param name="extension">With the dot, for example <c>.mp3</c>.</param>
    public static string[] ReserveUniquePaths(string folder, string baseName, IReadOnlyList<string> suffixes, string extension)
    {
        for (var attempt = 1; ; attempt++)
        {
            var stem = attempt == 1 ? baseName : $"{baseName} ({attempt})";
            var paths = suffixes.Select(s => Path.Combine(folder, stem + s + extension)).ToArray();
            if (!paths.Any(File.Exists)) return paths;
        }
    }

    /// <summary>Removes the characters Windows forbids in a file name, then trailing spaces and dots.</summary>
    internal static string Sanitize(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Array.IndexOf(invalid, c) < 0) builder.Append(c);
        }

        return builder.ToString().Trim().TrimEnd('.', ' ');
    }
}
