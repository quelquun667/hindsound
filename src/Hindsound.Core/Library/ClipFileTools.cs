using System.IO;

namespace Hindsound.Library;

/// <summary>What became of files sent to the Recycle Bin.</summary>
public enum RecycleOutcome
{
    /// <summary>Every file is in the Recycle Bin.</summary>
    Recycled,

    /// <summary>The Recycle Bin can't take them (a drive with none, a file too big for it). Seen before anything moved: every file is where it was.</summary>
    CannotRecycle,

    /// <summary>A file was no longer there when the bin was to take it. Seen before anything moved: the others are where they were.</summary>
    Gone,

    /// <summary>Something else went wrong. Any of the files may still be there: look at the disk again before saying more.</summary>
    Failed,
}

/// <summary>
/// The Recycle Bin of the system. Never deletes a file for good: a file the bin can't take stays where it is. Tests use a fake;
/// only a test of the Device category reaches the real one.
/// </summary>
internal interface IRecycleBin
{
    /// <summary>Sends the files to the bin, all of them or none as far as the system lets it know beforehand. Never throws.</summary>
    /// <param name="paths">Full paths of files, all in the same folder.</param>
    RecycleOutcome Recycle(IReadOnlyList<string> paths);
}

/// <summary>Where there is no Recycle Bin to reach (a test, a probe, a system without one): every file stays where it is.</summary>
internal sealed class NoRecycleBin : IRecycleBin
{
    public static readonly NoRecycleBin Instance = new();

    public RecycleOutcome Recycle(IReadOnlyList<string> paths) => RecycleOutcome.CannotRecycle;
}

/// <summary>Why a clip can't take a name, or what its renaming did.</summary>
public enum ClipRenameOutcome
{
    Done,

    /// <summary>Nothing typed, or only spaces.</summary>
    Empty,

    /// <summary>One of the characters a file name can't hold, or a control character.</summary>
    BadCharacters,

    /// <summary>A name Windows keeps for itself: CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, with or without an extension.</summary>
    Reserved,

    /// <summary>Ends with a dot or a space, which Windows drops without a word.</summary>
    TrailingDotOrSpace,

    /// <summary>
    /// A clip in one file whose name would end as a track does: the Library would join it to the other track of that name, and
    /// show them as one clip. See <see cref="ClipNames.TrackSuffixText"/>.
    /// </summary>
    TrackSuffix,

    /// <summary>The full path of a file of the clip would be too long.</summary>
    TooLong,

    /// <summary>Another file has the name of one of the files of the clip, in any case.</summary>
    Taken,

    /// <summary>The clip is no longer where it was. Nothing was renamed.</summary>
    Gone,

    /// <summary>The system refused. Every file of the clip has its old name.</summary>
    Failed,
}

/// <summary>What a deletion did. Never a deletion for good.</summary>
public enum ClipDeleteOutcome
{
    /// <summary>Every file of the clip is in the Recycle Bin.</summary>
    Recycled,

    /// <summary>The Recycle Bin can't take it: the files are still there, untouched.</summary>
    CannotRecycle,

    /// <summary>The clip was already gone.</summary>
    Gone,

    /// <summary>Something else went wrong: a new reading tells what is left.</summary>
    Failed,
}

/// <summary>The names of the files of a clip, as a renaming or a copy sees them.</summary>
internal static class ClipNames
{
    /// <summary>What the two files of a clip saved as separate tracks end with, before the extension.</summary>
    public static readonly string[] TrackSuffixes = ["_system", "_mic"];

    /// <summary>The sentence of <see cref="ClipRenameOutcome.TrackSuffix"/>.</summary>
    public const string TrackSuffixText = "A name can't end with _system or _mic";

    /// <summary>The name the user edits: the file name without its extension, and without the suffix of its track for a clip in two files.</summary>
    public static string EditableName(ClipGroup line)
    {
        var name = Path.GetFileNameWithoutExtension(line.Main.Path);
        return name[..^SuffixOf(line, line.Main.Path).Length];
    }

    /// <summary>The path each file of the line takes under <paramref name="name"/>, in the order of the files: same folder, same suffix, same extension.</summary>
    public static IReadOnlyList<string> TargetPaths(ClipGroup line, string name) =>
        [.. line.Files.Select(file => Path.Combine(Path.GetDirectoryName(file.Path) ?? "", name + SuffixOf(line, file.Path) + Path.GetExtension(file.Path)))];

    /// <summary>
    /// Why the clip can't be given this name, or null if it can. The one rule: the dialog asks it at each key with what the
    /// listing knows, the renaming asks it again against the disk.
    /// </summary>
    /// <param name="line">The clip to rename.</param>
    /// <param name="typed">The name as typed, without extension.</param>
    /// <param name="taken">Whether a path is already used by another file. The files of the line itself never count.</param>
    public static ClipRenameOutcome? Refusal(ClipGroup line, string typed, Func<string, bool> taken)
    {
        // The full path where long paths are not turned on, its closing zero left out; the name of one file on NTFS and exFAT.
        const int MaxPathLength = 260, MaxFileNameLength = 255;

        // A name of spaces alone is no name. Spaces at the start are kept: Windows takes them as they are.
        if (string.IsNullOrWhiteSpace(typed))
        {
            return ClipRenameOutcome.Empty;
        }

        if (typed.Any(c => char.IsControl(c) || @"\/:*?""<>|".Contains(c)))
        {
            return ClipRenameOutcome.BadCharacters;
        }

        if (typed[^1] is '.' or ' ')
        {
            return ClipRenameOutcome.TrailingDotOrSpace;
        }

        // A device for Windows: the part before the first dot, spaces at its end dropped, in any case. Not a path, so not compared
        // by the rule of paths: put in capitals, then compared letter by letter. Superscript one, two and three count as digits.
        var dot = typed.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot < 0 ? typed : typed[..dot]).TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789\u00B9\u00B2\u00B3".Contains(stem[3])))
        {
            return ClipRenameOutcome.Reserved;
        }

        // Only a clip in two files keeps the suffix of each track: one file named so would be taken for a track.
        if (line.Files.Count < 2 && TrackSuffixes.Any(suffix => typed.EndsWith(suffix, PathRules.Comparison)))
        {
            return ClipRenameOutcome.TrackSuffix;
        }

        var targets = TargetPaths(line, typed);
        if (targets.Any(path => path.Length >= MaxPathLength || Path.GetFileName(path).Length > MaxFileNameLength))
        {
            return ClipRenameOutcome.TooLong;
        }

        // The files of the line never stand in their own way, so a change of case alone is a renaming like any other.
        var own = new HashSet<string>(line.Files.Select(file => file.Path), PathRules.Comparer);
        return targets.Any(path => !own.Contains(path) && taken(path)) ? ClipRenameOutcome.Taken : null;
    }

    private static string SuffixOf(ClipGroup line, string path)
    {
        if (line.Files.Count < 2)
        {
            return "";
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return TrackSuffixes.FirstOrDefault(suffix => name.Length > suffix.Length && name.EndsWith(suffix, PathRules.Comparison)) is { } found
            ? name[^found.Length..]
            : "";
    }
}

/// <summary>The labels of the index as files change name. Written next to the settings, as the rest of the index.</summary>
internal interface IClipIndex
{
    /// <summary>The star and the collections of <paramref name="from"/> now belong to the files at <paramref name="to"/>: a renaming, or a replaced original.</summary>
    LibraryOutcome MoveLabels(ClipGroup from, IReadOnlyList<string> to);

    /// <summary>The files at <paramref name="to"/> join the collections of <paramref name="from"/>, without its star: a copy.</summary>
    LibraryOutcome CopyCollections(ClipGroup from, IReadOnlyList<string> to);
}
