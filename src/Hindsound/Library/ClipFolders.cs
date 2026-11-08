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

    /// <summary>Whether two full paths name the same folder.</summary>
    public static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);

    internal static string Key(string fullPath) => Path.TrimEndingDirectorySeparator(fullPath);

    /// <summary>All that a failure may tell: the message of a file error holds the full path.</summary>
    internal static string Describe(Exception ex) =>
        string.Create(CultureInfo.InvariantCulture, $"{ex.GetType().Name}, 0x{ex.HResult:X8}");
}
