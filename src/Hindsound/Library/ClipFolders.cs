using System.Globalization;
using System.IO;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Settings;

namespace Hindsound.Library;

/// <summary>Where clips are looked for, by the rule of the save itself: the library looks where the clips really went.</summary>
public static class ClipFolders
{
    /// <summary>
    /// The full path of a folder as stored: empty or the stored default give the real Music folder, a <c>%VARIABLE%</c> is
    /// expanded. Null for a folder no clip can have gone to: a save sends those to the rescue folder.
    /// </summary>
    /// <param name="journal">Told of a failure: a file name can hold a name the user chose, so never the path nor the message of the exception.</param>
    public static string? Resolve(string? stored, Action<string>? journal = null)
    {
        try
        {
            return ClipSaver.ExpandFolder(SettingsMapping.OutputFolder(stored));
        }
        catch (Exception ex)
        {
            journal?.Invoke($"clip scan: a folder can't be used ({Describe(ex)})");
            return null;
        }
    }

    /// <summary>
    /// The list of earlier folders once the output folder has changed from <paramref name="before"/> to
    /// <paramref name="after"/>, without which the clips left behind would vanish from the library without a word. Most
    /// recent first, so the cap drops the oldest. The texts stay as they were stored, and are compared by the folder they
    /// resolve to.
    /// </summary>
    public static List<string> AfterOutputChange(IReadOnlyList<string>? known, string? before, string? after)
    {
        var kept = new List<string>(ExportSettings.MaxKnownClipFolders);

        // The output folder is read anyway: it would take a place, and come back here by itself when it is left.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Resolve(after) is { } output)
        {
            seen.Add(Key(output));
        }

        Keep(string.IsNullOrWhiteSpace(before) ? ExportSettings.DefaultFolder : before);
        foreach (var folder in known ?? [])
        {
            Keep(folder);
        }

        return kept;

        void Keep(string? stored)
        {
            if (kept.Count < ExportSettings.MaxKnownClipFolders && !string.IsNullOrWhiteSpace(stored) && Resolve(stored) is { } full && seen.Add(Key(full)))
            {
                kept.Add(stored.Trim());
            }
        }
    }

    /// <summary>
    /// Whether a line of the library is a clip that could not go to the output folder, for its badge. No clip is rescued when
    /// the output folder is the rescue folder itself: the user chose it, and the badge would sit on every line. Touches no
    /// disk, but resolves both folders at each call: once for each line drawn, not for each line of the list.
    /// </summary>
    public static bool IsRescued(ClipGroup line, ClipScanRequest folders)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(folders);

        if (line.Files.Count == 0 || Resolve(folders.RescueFolder) is not { } rescue)
        {
            return false;
        }

        var key = Key(rescue);
        return !string.Equals(Resolve(folders.OutputFolder) is { } output ? Key(output) : null, key, StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(line.Main.Path) is { } folder
            && string.Equals(Key(folder), key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What two spellings of a folder, or of a file in it, share, compared without regard to case: with the same separators,
    /// without the one at the end, nor the <c>\\?\</c> or <c>\\.\</c> prefix of the long form of a path. The one comparison
    /// for the scan and for the index, or each would call a path by another name. A junction is another matter: finding
    /// where it leads would cost one opening of the folder at every look, so a folder reached by a junction and by its
    /// target is read twice.
    /// </summary>
    internal static string Key(string fullPath)
    {
        var path = fullPath.Replace('/', '\\');
        foreach (var prefix in LongPrefixes)
        {
            if (path.StartsWith(prefix + @"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return Path.TrimEndingDirectorySeparator(@"\\" + path[(prefix.Length + 4)..]);
            }
        }

        foreach (var prefix in LongPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Path.TrimEndingDirectorySeparator(path[prefix.Length..]);
            }
        }

        return Path.TrimEndingDirectorySeparator(path);
    }

    private static readonly string[] LongPrefixes = [@"\\?\", @"\\.\"];

    /// <summary>All that a failure may tell: the message of a file error holds the full path.</summary>
    internal static string Describe(Exception ex) =>
        string.Create(CultureInfo.InvariantCulture, $"{ex.GetType().Name}, 0x{ex.HResult:X8}");
}
