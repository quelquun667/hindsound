using System.Globalization;
using System.Runtime.Versioning;
using Hindsound.Library;
using Hindsound.Settings;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The Trash of the desktop, as the Trash specification of freedesktop.org (1.0) lays it out, which the file managers read.
/// A file goes by rename and never by copy: a file on the file system of the home goes to $XDG_DATA_HOME/Trash; one on another
/// goes to the trash of that file system, $topdir/.Trash/$uid when the administrator made .Trash (a real folder with the sticky
/// bit), else $topdir/.Trash-$uid, which is made on the spot. A file no trash can take stays where it is, and the answer says so.
/// A link goes as a link, never as the file it points to. A file is not deleted for good, ever.
/// </summary>
/// <remarks>
/// For each file the .trashinfo is created first, and only if the name is new (a name taken in the trash is never reused), then
/// the file is renamed into files/. If the rename fails the .trashinfo is removed, and the files already moved are put back.
/// </remarks>
internal sealed class FreedesktopTrash : IRecycleBin
{
    private readonly Action<string> _journal;
    private readonly ITrashFileSystem _files;
    private readonly string? _dataHome;
    private readonly Func<DateTime> _now;

    /// <param name="journal">Told of what went wrong, by code only: never a path, never a name.</param>
    /// <param name="files">The file system the trash works on.</param>
    /// <param name="dataHome">$XDG_DATA_HOME, a full path; null when there is none, and then nothing is recycled.</param>
    /// <param name="now">The local time, for the DeletionDate.</param>
    internal FreedesktopTrash(Action<string> journal, ITrashFileSystem files, string? dataHome, Func<DateTime>? now = null)
    {
        _journal = journal;
        _files = files;
        _dataHome = dataHome;
        _now = now ?? (() => DateTime.Now);
    }

    /// <summary>The trash of the user of this system: $XDG_DATA_HOME/Trash, ~/.local/share/Trash when it is not set.</summary>
    [SupportedOSPlatform("linux")]
    public static FreedesktopTrash ForUser(Action<string> journal) =>
        ForUser(folder => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify), journal);

    /// <param name="resolve">Gives the path of a special folder, as <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> does. For tests.</param>
    [SupportedOSPlatform("linux")]
    internal static FreedesktopTrash ForUser(Func<Environment.SpecialFolder, string?> resolve, Action<string> journal) =>
        new(journal, new LinuxTrashFileSystem(), UserFolders.Find(Environment.SpecialFolder.LocalApplicationData, resolve));

    public RecycleOutcome Recycle(IReadOnlyList<string> paths)
    {
        try
        {
            return paths.Count == 0 ? RecycleOutcome.Recycled : Send(paths);
        }
        catch (Exception ex)
        {
            _journal(Failure(ex));
            return RecycleOutcome.Failed;
        }
    }

    private RecycleOutcome Send(IReadOnlyList<string> paths)
    {
        // Everything is looked at before anything moves: what is refused here leaves every file where it was. A path
        // listed twice is one file: the second move would find nothing to move.
        var found = new List<(string Path, TrashStat Stat)>();
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            if (!path.StartsWith('/'))
            {
                return NotDone("path", Errno.Invalid);
            }

            found.Add((path, _files.Stat(path, follow: false)));
        }

        if (found.Any(file => file.Stat.Error is Errno.NoEntry or Errno.NotDirectory))
        {
            return RecycleOutcome.Gone;
        }

        foreach (var (_, stat) in found)
        {
            if (!stat.Found)
            {
                return NotDone("look", stat.Error);
            }

            if (stat.Kind is TrashKind.Directory or TrashKind.Other)
            {
                return NotDone("kind", 0);
            }
        }

        var places = new Dictionary<(string Folder, ulong Device), TrashPlace>();
        var plans = new List<Plan>();
        foreach (var (path, stat) in found)
        {
            var folder = _files.RealPath(TrashInfo.Parent(path) ?? "/");
            if (folder is null)
            {
                return NotDone("folder", Errno.NoEntry);
            }

            if (!places.TryGetValue((folder, stat.Device), out var place))
            {
                if (PlaceFor(folder, stat.Device) is not { } made)
                {
                    return RecycleOutcome.CannotRecycle;
                }

                places[(folder, stat.Device)] = place = made;
            }

            plans.Add(new Plan(path, TrashInfo.NameOf(path), folder, place));
        }

        var date = _now();
        var moved = new List<Moved>();
        try
        {
            foreach (var plan in plans)
            {
                var step = Move(plan, date, out var error);
                if (step is { } done)
                {
                    moved.Add(done);
                    continue;
                }

                var back = PutBack(moved);
                _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: not done ({error.Step}, errno {error.Code})"));
                return back ? RecycleOutcome.CannotRecycle : RecycleOutcome.Failed;
            }
        }
        catch (Exception ex)
        {
            // An exception between two moves must not leave the clip split: what is in the trash goes back first.
            _journal(Failure(ex));
            return PutBack(moved) ? RecycleOutcome.CannotRecycle : RecycleOutcome.Failed;
        }

        return RecycleOutcome.Recycled;
    }

    private Moved? Move(Plan plan, DateTime date, out (string Step, int Code) error)
    {
        var place = plan.Place;
        var original = TrashInfo.OriginalPath(place.Top, plan.Folder, plan.Name);
        var content = TrashInfo.Content(original, date);
        var lost = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var name = TrashInfo.UniqueName(plan.Name, candidate => lost.Contains(candidate) || Taken(place, candidate));
            if (name is null)
            {
                error = ("name", Errno.Exists);
                return null;
            }

            var info = TrashInfo.Join(place.InfoFolder, name + TrashInfo.InfoSuffix);
            var made = _files.CreateFile(info, content);
            if (made == Errno.Exists)
            {
                // Another program took this name between the look and the creation: the next one.
                lost.Add(name);
                continue;
            }

            if (made != 0)
            {
                error = ("info", made);
                return null;
            }

            var target = TrashInfo.Join(place.FilesFolder, name);
            var renamed = _files.Rename(plan.Path, target);
            if (renamed != 0)
            {
                _files.Delete(info);
                error = ("move", renamed);
                return null;
            }

            error = default;
            return new Moved(plan.Path, target, info);
        }

        error = ("name", Errno.Exists);
        return null;
    }

    /// <summary>A name is taken when a file or a link, even a broken one, or a .trashinfo has it, or when that can't be told.</summary>
    private bool Taken(TrashPlace place, string name) =>
        _files.Stat(TrashInfo.Join(place.FilesFolder, name), follow: false).Error != Errno.NoEntry
        || _files.Stat(TrashInfo.Join(place.InfoFolder, name + TrashInfo.InfoSuffix), follow: false).Error != Errno.NoEntry;

    /// <summary>Puts back, last first, the files already in the trash. True when every one is where it was.</summary>
    private bool PutBack(List<Moved> moved)
    {
        var all = true;
        for (var i = moved.Count - 1; i >= 0; i--)
        {
            var error = _files.Rename(moved[i].Target, moved[i].Path);
            if (error == 0)
            {
                _files.Delete(moved[i].Info);
                continue;
            }

            all = false;
            _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: put back failed (errno {error})"));
        }

        return all;
    }

    private static string Failure(Exception ex) =>
        string.Create(CultureInfo.InvariantCulture, $"recycle bin: failed ({ex.GetType().Name}, 0x{ex.HResult:X8})");

    private RecycleOutcome NotDone(string step, int error)
    {
        _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: not done ({step}, errno {error})"));
        return RecycleOutcome.CannotRecycle;
    }

    /// <summary>
    /// The trash for a file in <paramref name="folder"/> (a real path) on <paramref name="device"/>: the one of the home when
    /// the file is on the same file system, else the one of the top directory of its own. Null when none can take it.
    /// </summary>
    private TrashPlace? PlaceFor(string folder, ulong device)
    {
        if (_dataHome is null || !_dataHome.StartsWith('/'))
        {
            _journal("recycle bin: not done (no data folder)");
            return null;
        }

        if (DeviceOfHome() is not { } home)
        {
            _journal("recycle bin: not done (home, errno 2)");
            return null;
        }

        // On the file system of the home, the home trash only: another trash would only scatter the files.
        if (home == device)
        {
            return OwnedTrash(TrashInfo.Join(_dataHome, "Trash"), top: null, strict: false, "home");
        }

        var top = TopDirectory(folder, device);
        var uid = _files.UserId.ToString(CultureInfo.InvariantCulture);
        var shared = TrashInfo.Join(top, ".Trash");
        var stat = _files.Stat(shared, follow: false);
        if (stat is { Found: true, Kind: TrashKind.Directory, Sticky: true }
            && OwnedTrash(TrashInfo.Join(shared, uid), top, strict: true, "shared") is { } inShared)
        {
            return inShared;
        }

        // A .Trash that is a link, or has no sticky bit, is not to be trusted (anyone could have set it up): .Trash-$uid then.
        return OwnedTrash(TrashInfo.Join(top, ".Trash-" + uid), top, strict: true, "own");
    }

    /// <summary>The device of the file system the home trash is, or would be made on: the first folder that exists from $XDG_DATA_HOME up.</summary>
    private ulong? DeviceOfHome()
    {
        for (var folder = _dataHome; folder is not null; folder = TrashInfo.Parent(folder))
        {
            var stat = _files.Stat(folder, follow: true);
            if (stat.Found)
            {
                return stat.Device;
            }
        }

        return null;
    }

    /// <summary>
    /// The mount point of the file system holding <paramref name="folder"/>: the highest folder above it, itself included, on
    /// the same device.
    /// </summary>
    private string TopDirectory(string folder, ulong device)
    {
        var top = folder;
        while (TrashInfo.Parent(top) is { } above && _files.Stat(above, follow: true) is { Found: true } stat && stat.Device == device)
        {
            top = above;
        }

        return top;
    }

    /// <summary>
    /// The trash at <paramref name="folder"/>, made with its info and files folders when they are missing, all readable by the
    /// user alone. <paramref name="strict"/> for a trash on a shared file system: real folders, owned by the user. For the home
    /// a link to a folder is fine (a dotfile manager makes them), and so are the folders above it.
    /// </summary>
    private TrashPlace? OwnedTrash(string folder, string? top, bool strict, string code)
    {
        if (!Folder(folder, strict, code) || !Folder(TrashInfo.Join(folder, "info"), strict, code) || !Folder(TrashInfo.Join(folder, "files"), strict, code))
        {
            return null;
        }

        return new TrashPlace(TrashInfo.Join(folder, "info"), TrashInfo.Join(folder, "files"), top);
    }

    private bool Folder(string path, bool strict, string code)
    {
        var stat = _files.Stat(path, follow: !strict);
        if (stat.Error == Errno.NoEntry)
        {
            if (!Parents(TrashInfo.Parent(path), strict))
            {
                _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: not done (folder {code}, errno {Errno.NoEntry})"));
                return false;
            }

            var made = _files.MakeDirectory(path);
            if (made != 0 && made != Errno.Exists)
            {
                _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: not done (folder {code}, errno {made})"));
                return false;
            }

            stat = _files.Stat(path, follow: !strict);
        }

        if (stat.Found && stat.Kind == TrashKind.Directory && (!strict || stat.Owner == _files.UserId))
        {
            return true;
        }

        _journal(string.Create(CultureInfo.InvariantCulture, $"recycle bin: not done (folder {code}, errno {stat.Error})"));
        return false;
    }

    /// <summary>Makes the folders above the home trash that are missing ($XDG_DATA_HOME, as the base directory specification asks, 0700). Never above a strict trash: its top directory exists.</summary>
    private bool Parents(string? folder, bool strict)
    {
        if (folder is null)
        {
            return false;
        }

        var stat = _files.Stat(folder, follow: true);
        if (stat.Found)
        {
            return stat.Kind == TrashKind.Directory;
        }

        if (strict || stat.Error != Errno.NoEntry || !Parents(TrashInfo.Parent(folder), strict))
        {
            return false;
        }

        var made = _files.MakeDirectory(folder);
        return made == 0 || made == Errno.Exists;
    }

    /// <param name="Top">The top directory the Path of a .trashinfo is relative to; null for the home trash, whose Path is absolute.</param>
    private sealed record TrashPlace(string InfoFolder, string FilesFolder, string? Top);

    private sealed record Plan(string Path, string Name, string Folder, TrashPlace Place);

    private sealed record Moved(string Path, string Target, string Info);
}
