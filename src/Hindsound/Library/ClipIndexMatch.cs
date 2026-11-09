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

    private static string FolderKey(string folder) => ClipFolders.Key(folder);
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

    // How far a date may have moved when a clip is copied to a FAT (2 s) or exFAT (10 ms) drive, as the scanner allows for.
    private static readonly TimeSpan CoarseClock = TimeSpan.FromSeconds(2);

    private const long CoarseGrid = 10 * TimeSpan.TicksPerMillisecond;

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
                onDisk[ClipFolders.Key(file.Path)] = file;
            }
        }

        var links = new Dictionary<LibraryFile, ClipFile>();
        var lost = new List<LibraryFile>();
        var changed = new List<(LibraryFile File, ClipFile OnDisk)>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            foreach (var file in clip.Files)
            {
                if (!onDisk.TryGetValue(ClipFolders.Key(file.Path), out var same))
                {
                    lost.Add(file);
                    continue;
                }

                named.Add(ClipFolders.Key(file.Path));
                if (KeyOf(same) == KeyOf(file))
                {
                    links[file] = same;
                    claimed.Add(ClipFolders.Key(same.Path));
                }
                else
                {
                    changed.Add((file, same));
                }
            }
        }

        if (changed.Count > 0)
        {
            // The name of an entry, with another size or date, is that entry edited. Unless one lost entry, and only one, has
            // exactly this size and date, and no other file does: then the name was taken over by a renamed file, and the
            // dead clip does not hand its labels on.
            var lostByKey = new Dictionary<(long Size, long Written), List<LibraryFile>>();
            foreach (var file in lost)
            {
                if (!lostByKey.TryGetValue(KeyOf(file), out var owners))
                {
                    owners = [];
                    lostByKey[KeyOf(file)] = owners;
                }

                owners.Add(file);
            }

            var diskByKey = new Dictionary<(long Size, long Written), int>();
            foreach (var file in onDisk.Values)
            {
                diskByKey[KeyOf(file)] = diskByKey.GetValueOrDefault(KeyOf(file)) + 1;
            }

            var displaced = new List<LibraryFile>();
            foreach (var (file, same) in changed)
            {
                if (lostByKey.TryGetValue(KeyOf(same), out var owners) && owners.Count == 1 && diskByKey[KeyOf(same)] == 1)
                {
                    links[owners[0]] = same;
                    claimed.Add(ClipFolders.Key(same.Path));
                    displaced.Add(file);
                }
            }

            foreach (var (file, same) in changed)
            {
                if (displaced.Contains(file))
                {
                    continue;
                }

                // Another file already holds this name by its size and date: the entry here is a stale record of it.
                if (claimed.Contains(ClipFolders.Key(same.Path)))
                {
                    displaced.Add(file);
                }
                else
                {
                    links[file] = same;
                }
            }

            lost.RemoveAll(links.ContainsKey);
            lost.AddRange(displaced);
        }

        if (lost.Count == 0)
        {
            return links;
        }

        // A file another entry names is that entry's: it is no link for a lost one. It still counts as a candidate, though,
        // because a clip labeled among identical copies is no reason to pick one of them for the dead one.
        // Moving a whole folder also comes here, its old place being absent.
        var free = new Dictionary<(long Size, long Written), List<ClipFile>>();
        foreach (var file in onDisk.Values)
        {
            if (!scan.WasRead(Path.GetDirectoryName(file.Path)))
            {
                continue;
            }

            var key = KeyOf(file);
            if (!free.TryGetValue(key, out var candidates))
            {
                candidates = [];
                free[key] = candidates;
            }

            candidates.Add(file);
        }

        var wanted = new Dictionary<(long Size, long Written), List<LibraryFile>>();
        foreach (var file in lost)
        {
            var key = KeyOf(file);
            if (!wanted.TryGetValue(key, out var askers))
            {
                askers = [];
                wanted[key] = askers;
            }

            askers.Add(file);
        }

        // A doubt on either side is no link: labels on the wrong clip are worse than labels that wait.
        foreach (var (key, askers) in wanted)
        {
            if (free.TryGetValue(key, out var candidates))
            {
                LinkUnlessDoubtful(askers, candidates, named, links);
            }
        }

        // The exact date comes first: on a disk that keeps it to the tick, two tracks of one size written a second apart would
        // be a doubt here. A copy onto a FAT or exFAT drive moves every date a little, and the scanner knows those drives.
        var left = lost.FindAll(file => !links.ContainsKey(file));
        if (left.Count > 0)
        {
            // A file just linked by its exact date is that lost file's. One named by an entry stays a candidate, for the doubt.
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in lost)
            {
                if (links.TryGetValue(file, out var linked))
                {
                    taken.Add(ClipFolders.Key(linked.Path));
                }
            }

            var spare = free.Values.SelectMany(static list => list).Where(file => !taken.Contains(ClipFolders.Key(file.Path))).ToList();
            foreach (var file in left)
            {
                var near = spare.FindAll(candidate => IsNear(file, candidate));
                if (near.Count > 0)
                {
                    var askers = left.FindAll(other => near.Exists(candidate => IsNear(other, candidate)));
                    LinkUnlessDoubtful(askers, near, named, links);
                }
            }
        }

        return links;
    }

    /// <summary>
    /// The lost files that asked for a place, and the files on disk that could be it. One and one is a link. The two tracks of
    /// a clip may share a size and a date, so with two and two the suffixes _system and _mic tell which is which.
    /// </summary>
    private static void LinkUnlessDoubtful(List<LibraryFile> askers, List<ClipFile> candidates, HashSet<string> named, Dictionary<LibraryFile, ClipFile> links)
    {
        if (askers.Count != candidates.Count || candidates.Exists(candidate => named.Contains(ClipFolders.Key(candidate.Path))))
        {
            return;
        }

        if (askers.Count == 1)
        {
            links[askers[0]] = candidates[0];
            return;
        }

        if (askers.Count != 2)
        {
            return;
        }

        var (first, second) = (askers[0], askers[1]);
        if (Track(candidates[0].Path) + Track(candidates[1].Path) != SystemTrack + MicTrack || Track(first.Path) + Track(second.Path) != SystemTrack + MicTrack)
        {
            return;
        }

        var (ofFirst, ofSecond) = Track(first.Path) == Track(candidates[0].Path) ? (candidates[0], candidates[1]) : (candidates[1], candidates[0]);
        links[first] = ofFirst;
        links[second] = ofSecond;
    }

    private const int SystemTrack = 1, MicTrack = 2;

    /// <summary><see cref="SystemTrack"/> for a name ending in the suffix of a system track, <see cref="MicTrack"/> for a mic track, 0 for any other.</summary>
    private static int Track(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path.AsSpan());
        return stem.EndsWith("_system", StringComparison.OrdinalIgnoreCase) ? SystemTrack
            : stem.EndsWith("_mic", StringComparison.OrdinalIgnoreCase) ? MicTrack
            : 0;
    }

    // Only a date that sits on the grid of such a drive is a date such a drive could have given: a file that differs by a few
    // ticks on a disk that keeps them is another file.
    // The other track of the same clip is written a second apart and may be as large: a system track is never a mic track.
    private static bool IsNear(LibraryFile file, ClipFile candidate) =>
        file.Size == candidate.Size
        && candidate.WrittenUtc.Ticks % CoarseGrid == 0
        && (file.Written - candidate.WrittenUtc).Duration() <= CoarseClock
        && (Track(file.Path) == 0 || Track(candidate.Path) == 0 || Track(file.Path) == Track(candidate.Path));

    private static (long Size, long Written) KeyOf(ClipFile file) => (file.Size, file.WrittenUtc.Ticks);

    private static (long Size, long Written) KeyOf(LibraryFile file) => (file.Size, file.Written.Ticks);
}
