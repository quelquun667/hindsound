using System.IO;
using System.IO.Enumeration;
using Hindsound.Export;

namespace Hindsound.Library;

/// <summary>What to read. The folders are given as stored: <see cref="ClipFolders.Resolve"/> applies to each.</summary>
public sealed record ClipScanRequest
{
    public string? OutputFolder { get; init; }

    /// <summary>The earlier output folders, from the settings.</summary>
    public IReadOnlyList<string> KnownFolders { get; init; } = [];

    /// <summary>The folders the index of the library names: a labeled clip stays visible wherever it was saved.</summary>
    public IReadOnlyList<string> IndexFolders { get; init; } = [];

    public string RescueFolder { get; init; } = ClipSaver.DefaultRescueFolder;

    /// <summary>
    /// When the oldest clip still being saved was asked for, or null if none is. A clip is reserved empty and then filled
    /// piece by piece, track after track, so a file written since then may not be whole.
    /// </summary>
    public DateTime? ClipStartedUtc { get; init; }
}

/// <summary>Every clip found, newest first, and the folders really listed: one absent or refused is not among them.</summary>
public sealed record ClipListing(IReadOnlyList<ClipGroup> Groups, IReadOnlyList<string> FoldersRead);

/// <summary>The lines to show, newest first, and how many clips the cap left out.</summary>
public sealed record ClipLines(IReadOnlyList<ClipGroup> Lines, int LeftOut);

/// <summary>
/// Lists the clips on disk: the first level of each folder, <c>*.mp3</c> and <c>*.wav</c>, from the names, sizes and dates
/// the folder listing gives. No file is opened and no folder is created. A problem with a folder only leaves it out.
/// </summary>
public static class ClipScanner
{
    public const int MaxLines = 2000;

    private const string SystemSuffix = "_system", MicSuffix = "_mic";

    // A FAT or exFAT drive keeps write times to 2 s, and the system clock file times come from is coarser than UtcNow. A
    // file hidden too early only comes back at the next scan; a half-written one shown is a clip that seems broken.
    private static readonly TimeSpan WriteClockMargin = TimeSpan.FromSeconds(2);

    // The listing of the folder itself must fail when refused: read as empty, every clip in it would count as gone.
    // A hidden or system file is no clip of ours, and is said so here rather than left to the default of the options.
    private static readonly EnumerationOptions FirstLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>
    /// Never throws for a folder or a file. Slow disks make it slow: not for the thread of the window. The result holds every
    /// clip found, since the index needs them all to recognize a moved clip; <see cref="Cap"/> then gives the lines.
    /// </summary>
    /// <param name="journal">Told of a failure: only the type of the exception and its code, never a path or a message.</param>
    public static ClipListing Scan(ClipScanRequest request, Action<string>? journal = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hideFrom = request.ClipStartedUtc - WriteClockMargin;
        var files = new List<ClipFile>();
        var read = new List<string>();
        foreach (var folder in FoldersToRead(request, journal))
        {
            if (ReadFolder(folder, hideFrom, files, journal))
            {
                read.Add(folder);
            }
        }

        return new ClipListing(GroupNewestFirst(files), read);
    }

    /// <summary>The full paths of the folders to read, each once, in the order they are read.</summary>
    public static IReadOnlyList<string> FoldersToRead(ClipScanRequest request, Action<string>? journal = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<string>();
        Add(request.OutputFolder);
        foreach (var folder in request.KnownFolders)
        {
            Add(folder);
        }

        Add(request.RescueFolder);
        foreach (var folder in request.IndexFolders)
        {
            Add(folder);
        }

        return folders;

        void Add(string? stored)
        {
            if (ClipFolders.Resolve(stored, journal) is { } full && seen.Add(ClipFolders.Key(full)))
            {
                folders.Add(full);
            }
        }
    }

    /// <summary>
    /// The <paramref name="max"/> most recent clips, which the sentence under the list can truthfully announce, then any
    /// older clip the user labeled: a favorite must never vanish because many newer clips came after it.
    /// </summary>
    /// <param name="newestFirst">As <see cref="Scan"/> gives them.</param>
    /// <param name="isLabeled">Asked only of the clips past the cap.</param>
    public static ClipLines Cap(IReadOnlyList<ClipGroup> newestFirst, Func<ClipGroup, bool> isLabeled, int max = MaxLines)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        ArgumentNullException.ThrowIfNull(isLabeled);

        if (newestFirst.Count <= max)
        {
            return new ClipLines(newestFirst, 0);
        }

        var lines = new List<ClipGroup>(max);
        var leftOut = 0;
        for (var i = 0; i < newestFirst.Count; i++)
        {
            if (i < max || isLabeled(newestFirst[i]))
            {
                lines.Add(newestFirst[i]);
            }
            else
            {
                leftOut++;
            }
        }

        return new ClipLines(lines, leftOut);
    }

    /// <summary>False if the folder could not be listed to its end. What was found before the failure is kept.</summary>
    private static bool ReadFolder(string folder, DateTime? hideFrom, List<ClipFile> files, Action<string>? journal)
    {
        try
        {
            // A file left out for a save in progress is a file the folder holds: the folder is then not read to its end, or the
            // index would take that clip for gone, and count the days of a clip that is there.
            var heldBack = false;
            var listing = new FileSystemEnumerable<ClipFile>(
                folder,
                static (ref FileSystemEntry entry) => new ClipFile(entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                FirstLevel)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                {
                    if (entry.IsDirectory || !IsAudioName(entry.FileName))
                    {
                        return false;
                    }

                    // The date before the size: a clip just reserved is empty, and is held back all the same.
                    if (hideFrom is { } limit && entry.LastWriteTimeUtc.UtcDateTime >= limit)
                    {
                        heldBack = true;
                        return false;
                    }

                    return entry.Length > 0;
                },
            };

            foreach (var file in listing)
            {
                files.Add(file);
            }

            return !heldBack;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or DriveNotFoundException)
        {
            // Nothing to tell: no clip saved yet, no rescue folder, or a disk unplugged.
            return false;
        }
        catch (Exception ex)
        {
            journal?.Invoke($"clip scan: a folder can't be listed ({ClipFolders.Describe(ex)})");
            return false;
        }
    }

    private static bool IsAudioName(ReadOnlySpan<char> name)
    {
        var extension = Path.GetExtension(name);
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ClipGroup> GroupNewestFirst(List<ClipFile> files)
    {
        // The two tracks of a clip differ only by their suffix (see FileNameTemplate.ReserveUniquePaths).
        var systemTracks = new Dictionary<string, ClipFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (WithoutSuffix(file.Path, SystemSuffix) is { } clip)
            {
                systemTracks[clip] = file;
            }
        }

        var groups = new List<(DateTime Written, ClipGroup Group)>(files.Count);
        foreach (var file in files)
        {
            if (WithoutSuffix(file.Path, SystemSuffix) is not null)
            {
                continue;
            }

            if (WithoutSuffix(file.Path, MicSuffix) is { } clip && systemTracks.Remove(clip, out var system))
            {
                groups.Add((Later(system.WrittenUtc, file.WrittenUtc), new ClipGroup([system, file])));
            }
            else
            {
                groups.Add((file.WrittenUtc, new ClipGroup([file])));
            }
        }

        foreach (var system in systemTracks.Values)
        {
            groups.Add((system.WrittenUtc, new ClipGroup([system])));
        }

        // The path decides between equal dates, or the lines would swap places from one scan to the next.
        groups.Sort(static (a, b) =>
        {
            var byDate = b.Written.CompareTo(a.Written);
            return byDate != 0 ? byDate : string.Compare(a.Group.Main.Path, b.Group.Main.Path, StringComparison.OrdinalIgnoreCase);
        });

        var sorted = new List<ClipGroup>(groups.Count);
        foreach (var (_, group) in groups)
        {
            sorted.Add(group);
        }

        return sorted;
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>The path without its suffix if its name ends with it (extension kept, to pair only files of one format), or null.</summary>
    private static string? WithoutSuffix(string path, string suffix)
    {
        var extension = Path.GetExtension(path.AsSpan());
        var stem = path.AsSpan(0, path.Length - extension.Length);
        return stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? string.Concat(stem[..^suffix.Length], extension)
            : null;
    }
}
