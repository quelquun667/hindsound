using System.IO;

namespace Hindsound.Library;

/// <summary>What one look at the clip folders found.</summary>
public sealed class ClipScan
{
    private readonly HashSet<string> _foldersRead;

    /// <param name="groups">Every clip found: a clip left out here is a clip the index takes for gone.</param>
    /// <param name="foldersRead">The folders really listed. One that is absent or refused the listing stays out: nothing in it can be called gone.</param>
    public ClipScan(IReadOnlyList<ClipGroup> groups, IEnumerable<string> foldersRead)
    {
        Groups = groups;
        _foldersRead = new HashSet<string>(foldersRead.Select(FolderKey), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>No folder read, so no entry of the index can lose anything. For an action that comes before the first look.</summary>
    public static ClipScan Nothing { get; } = new([], []);

    public IReadOnlyList<ClipGroup> Groups { get; }

    public bool WasRead(string? folder) => !string.IsNullOrEmpty(folder) && _foldersRead.Contains(FolderKey(folder));

    private static string FolderKey(string folder) => Path.TrimEndingDirectorySeparator(folder.Replace('/', '\\'));
}

/// <summary>The labels a line of the library shows.</summary>
public readonly record struct ClipLabels(bool Favorite, IReadOnlyList<string> CollectionIds)
{
    /// <summary>The scan's cap on lines keeps these whatever their age.</summary>
    public bool Any => Favorite || CollectionIds.Count > 0;
}

/// <summary>Which entries of the index each file of a scan belongs to. Computed for each read, and never written.</summary>
public sealed class IndexMatch
{
    private static readonly string[] NoIds = [];

    private readonly Dictionary<string, List<LibraryClip>> _byPath;

    internal IndexMatch(Dictionary<string, List<LibraryClip>> byPath)
    {
        _byPath = byPath;
    }

    public ClipLabels LabelsOf(ClipGroup group)
    {
        var favorite = false;
        List<string>? ids = null;
        foreach (var file in group.Files)
        {
            if (!_byPath.TryGetValue(file.Path, out var clips))
            {
                continue;
            }

            foreach (var clip in clips)
            {
                favorite |= clip.Favorite;
                foreach (var id in clip.Collections)
                {
                    ids ??= [];
                    if (!ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return new ClipLabels(favorite, ids is null ? NoIds : ids);
    }
}

/// <summary>
/// Finds the files of the entries of the index in a scan. Pure, so renames and deletions are tested without a disk. A clip is
/// known by its folder and name, then by its size and date: the clip folder is never touched, so nothing else can identify it.
/// </summary>
public static class ClipIndexMatch
{
    /// <summary>How long an entry outlives its files. Long enough for a clip to come back from the Recycle Bin.</summary>
    public static readonly TimeSpan KeptMissing = TimeSpan.FromDays(30);

    /// <summary>The labels of what a scan found, for display. Changes nothing, not even in the entries.</summary>
    public static IndexMatch Match(IReadOnlyList<LibraryClip> clips, ClipScan scan)
    {
        var links = Link(clips, scan);
        var byPath = new Dictionary<string, List<LibraryClip>>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            foreach (var file in clip.Files)
            {
                if (!links.TryGetValue(file, out var found))
                {
                    continue;
                }

                if (!byPath.TryGetValue(found.Path, out var owners))
                {
                    owners = [];
                    byPath[found.Path] = owners;
                }

                owners.Add(clip);
            }
        }

        return new IndexMatch(byPath);
    }

    /// <summary>
    /// What an action writes along with its change: the entries, changed in place, follow their files, and the ones past
    /// <see cref="KeptMissing"/> are left out. Only an action comes here, so the count starts late and nothing goes too early.
    /// </summary>
    /// <param name="clips">The entries.</param>
    /// <param name="scan">The last look at the folders.</param>
    /// <param name="nowUtc">The time.</param>
    public static List<LibraryClip> Settled(IReadOnlyList<LibraryClip> clips, ClipScan scan, DateTime nowUtc)
    {
        var links = Link(clips, scan);
        var kept = new List<LibraryClip>(clips.Count);
        foreach (var clip in clips)
        {
            var found = false;
            foreach (var file in clip.Files)
            {
                if (links.TryGetValue(file, out var onDisk))
                {
                    file.Path = onDisk.Path;
                    file.Size = onDisk.Size;
                    file.Written = onDisk.WrittenUtc;
                    found = true;
                }
            }

            if (found)
            {
                // One surviving track is enough. The lost one stays in the entry: if it comes back, it is still this clip.
                clip.MissingSince = null;
                kept.Add(clip);
                continue;
            }

            // A folder not read may still hold the file: an unplugged disk starts no count, and ends none.
            if (!clip.Files.All(file => scan.WasRead(Path.GetDirectoryName(file.Path))))
            {
                kept.Add(clip);
                continue;
            }

            clip.MissingSince ??= nowUtc;
            if (nowUtc - clip.MissingSince.Value < KeptMissing)
            {
                kept.Add(clip);
            }
        }

        return kept;
    }

    /// <summary>The folders the entries name, for the scan to read: a labeled clip stays visible after the output folder changes.</summary>
    public static IReadOnlyList<string> FoldersCited(IReadOnlyList<LibraryClip> clips)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            foreach (var file in clip.Files)
            {
                if (Path.GetDirectoryName(file.Path) is { Length: > 0 } folder)
                {
                    folders.Add(folder);
                }
            }
        }

        return [.. folders];
    }

    private static Dictionary<LibraryFile, ClipFile> Link(IReadOnlyList<LibraryClip> clips, ClipScan scan)
    {
        var onDisk = new Dictionary<string, ClipFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in scan.Groups)
        {
            foreach (var file in group.Files)
            {
                onDisk[file.Path] = file;
            }
        }

        var links = new Dictionary<LibraryFile, ClipFile>();
        var lost = new List<LibraryFile>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            foreach (var file in clip.Files)
            {
                if (onDisk.TryGetValue(file.Path, out var same))
                {
                    links[file] = same;
                    named.Add(file.Path);
                }
                else
                {
                    lost.Add(file);
                }
            }
        }

        if (lost.Count == 0)
        {
            return links;
        }

        // A file another entry names is that entry's. Moving a whole folder also comes here, its old place being absent.
        var free = new Dictionary<(long Size, long Written), List<ClipFile>>();
        foreach (var file in onDisk.Values)
        {
            if (named.Contains(file.Path) || !scan.WasRead(Path.GetDirectoryName(file.Path)))
            {
                continue;
            }

            var key = (file.Size, file.WrittenUtc.Ticks);
            if (!free.TryGetValue(key, out var candidates))
            {
                candidates = [];
                free[key] = candidates;
            }

            candidates.Add(file);
        }

        var wanted = new Dictionary<(long Size, long Written), int>();
        foreach (var file in lost)
        {
            var key = (file.Size, file.Written.Ticks);
            wanted[key] = wanted.GetValueOrDefault(key) + 1;
        }

        // A doubt on either side is no link: labels on the wrong clip are worse than labels that wait.
        foreach (var file in lost)
        {
            var key = (file.Size, file.Written.Ticks);
            if (wanted[key] == 1 && free.TryGetValue(key, out var candidates) && candidates.Count == 1)
            {
                links[file] = candidates[0];
            }
        }

        return links;
    }
}
