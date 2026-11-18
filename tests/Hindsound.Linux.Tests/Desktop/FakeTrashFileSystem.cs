using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// A file system in memory that answers as Linux does where a trash looks: devices that differ from one mount to the next, a
/// rename across them that fails with EXDEV and moves nothing, a file that must not exist yet. Every change is kept in
/// <see cref="Steps"/>, in order. A test makes the faults it wants through the hooks.
/// </summary>
internal sealed class FakeTrashFileSystem : ITrashFileSystem
{
    private sealed record Node(TrashKind Kind, ulong Device, uint Owner, uint Mode, string Text = "", string? Target = null);

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);

    public FakeTrashFileSystem()
    {
        _nodes["/"] = new Node(TrashKind.Directory, 1, 0, 0x1ED);
    }

    public uint UserId { get; set; } = 1000;

    /// <summary>What was changed, in order: "create PATH", "rename FROM -> TO", "delete PATH", "mkdir PATH".</summary>
    public List<string> Steps { get; } = [];

    /// <summary>The errno a rename answers instead of moving, by destination; 0 or null for none.</summary>
    public Func<string, string, int>? RenameFault { get; set; }

    public Func<string, int>? CreateFault { get; set; }

    public Func<string, int>? MakeFault { get; set; }

    public Func<string, bool>? StatThrows { get; set; }

    /// <summary>Makes a folder, and the ones above it that are missing, on the device of the folder above unless told.</summary>
    public void Folder(string path, ulong? device = null, uint? owner = null, uint mode = 0x1ED)
    {
        var parent = TrashInfo.Parent(path);
        if (parent is not null && !_nodes.ContainsKey(parent))
        {
            Folder(parent);
        }

        _nodes[path] = new Node(TrashKind.Directory, device ?? (parent is null ? 1 : _nodes[parent].Device), owner ?? UserId, mode);
    }

    public void File(string path, string text = "audio")
    {
        var parent = TrashInfo.Parent(path)!;
        if (!_nodes.ContainsKey(parent))
        {
            Folder(parent);
        }

        _nodes[path] = new Node(TrashKind.File, _nodes[parent].Device, UserId, 0x1A4, text);
    }

    public void Link(string path, string target)
    {
        var parent = TrashInfo.Parent(path)!;
        if (!_nodes.ContainsKey(parent))
        {
            Folder(parent);
        }

        _nodes[path] = new Node(TrashKind.Symlink, _nodes[parent].Device, UserId, 0x1FF, Target: target);
    }

    public bool Exists(string path) => _nodes.ContainsKey(path);

    public TrashKind KindOf(string path) => _nodes[path].Kind;

    public string TextOf(string path) => _nodes[path].Text;

    public uint ModeOf(string path) => _nodes[path].Mode;

    public IEnumerable<string> Paths => _nodes.Keys;

    /// <summary>The path with the links of the folders above it resolved, as the system does, the last name left as it is.</summary>
    private string Inner(string path)
    {
        var parent = TrashInfo.Parent(path);
        return parent is null ? path : TrashInfo.Join(RealPath(parent) ?? parent, TrashInfo.NameOf(path));
    }

    public TrashStat Stat(string path, bool follow)
    {
        path = Inner(path);
        if (StatThrows?.Invoke(path) == true)
        {
            throw new IOException("fake");
        }

        if (!_nodes.TryGetValue(path, out var node))
        {
            return TrashStat.Missing(Errno.NoEntry);
        }

        if (follow && node.Kind == TrashKind.Symlink)
        {
            return _nodes.TryGetValue(node.Target!, out var target)
                ? new TrashStat(0, target.Kind, target.Device, target.Owner, target.Mode)
                : TrashStat.Missing(Errno.NoEntry);
        }

        return new TrashStat(0, node.Kind, node.Device, node.Owner, node.Mode);
    }

    public string? RealPath(string path)
    {
        var resolved = "";
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            resolved += "/" + part;
            while (_nodes.TryGetValue(resolved, out var node) && node.Kind == TrashKind.Symlink)
            {
                resolved = node.Target!;
            }
        }

        return resolved.Length == 0 ? "/" : _nodes.ContainsKey(resolved) ? resolved : null;
    }

    public int MakeDirectory(string path)
    {
        path = Inner(path);
        if (MakeFault?.Invoke(path) is { } fault and not 0)
        {
            return fault;
        }

        if (_nodes.ContainsKey(path))
        {
            return Errno.Exists;
        }

        if (!_nodes.TryGetValue(TrashInfo.Parent(path)!, out var parent) || parent.Kind != TrashKind.Directory)
        {
            return Errno.NoEntry;
        }

        Steps.Add("mkdir " + path);
        _nodes[path] = new Node(TrashKind.Directory, parent.Device, UserId, 0x1C0);
        return 0;
    }

    public int CreateFile(string path, string content)
    {
        path = Inner(path);
        if (CreateFault?.Invoke(path) is { } fault and not 0)
        {
            return fault;
        }

        if (_nodes.ContainsKey(path))
        {
            return Errno.Exists;
        }

        if (!_nodes.TryGetValue(TrashInfo.Parent(path)!, out var parent))
        {
            return Errno.NoEntry;
        }

        Steps.Add("create " + path);
        _nodes[path] = new Node(TrashKind.File, parent.Device, UserId, 0x180, content);
        return 0;
    }

    public int Rename(string from, string to)
    {
        from = Inner(from);
        to = Inner(to);
        if (RenameFault?.Invoke(from, to) is { } fault and not 0)
        {
            return fault;
        }

        if (!_nodes.TryGetValue(from, out var node))
        {
            return Errno.NoEntry;
        }

        if (!_nodes.TryGetValue(TrashInfo.Parent(to)!, out var parent))
        {
            return Errno.NoEntry;
        }

        if (_nodes.ContainsKey(to))
        {
            return Errno.Exists;
        }

        if (parent.Device != _nodes[TrashInfo.Parent(from)!].Device)
        {
            return Errno.CrossDevice;
        }

        Steps.Add($"rename {from} -> {to}");
        _nodes.Remove(from);
        _nodes[to] = node;
        return 0;
    }

    public void Delete(string path)
    {
        path = Inner(path);
        Steps.Add("delete " + path);
        _nodes.Remove(path);
    }
}
