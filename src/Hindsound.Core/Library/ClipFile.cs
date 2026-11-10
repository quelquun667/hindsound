namespace Hindsound.Library;

/// <summary>One audio file on disk, as a scan read it.</summary>
public readonly record struct ClipFile(string Path, long Size, DateTime WrittenUtc);

/// <summary>One line of the library: a single file, or the two tracks of a clip saved as separate files.</summary>
public sealed record ClipGroup(IReadOnlyList<ClipFile> Files)
{
    /// <summary>The file the line is named after.</summary>
    public ClipFile Main => Files[0];
}
