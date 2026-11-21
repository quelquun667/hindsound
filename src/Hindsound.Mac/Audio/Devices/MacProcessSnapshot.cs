using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Hindsound.Audio;

namespace Hindsound.Mac.Audio.Devices;

/// <summary>What the kernel says of one process, before it is named.</summary>
/// <param name="Id">Its pid.</param>
/// <param name="ParentId">The pid of its parent (1, launchd, for an app or a service macOS started).</param>
/// <param name="Path">The path of its program, if the kernel gives it.</param>
/// <param name="Command">The kernel's short name of the program (16 bytes at most).</param>
internal sealed record MacProcessFacts(int Id, int ParentId, string? Path, string Command);

/// <summary>
/// How a process of macOS is named for the settings and for the capture of one application, from facts only, so it is tested
/// everywhere. An application is a bundle: a program inside "Safari.app" is "Safari", the name of the outermost ".app"
/// folder of its path, as the Finder shows it (the Info.plist is not read: the folder is what the person sees, and reading a
/// file for each of hundreds of processes at each listing would cost for nothing). So the helpers of an application, which
/// it starts from inside its bundle ("Google Chrome Helper (Renderer).app" inside "Google Chrome.app"), carry its name, and
/// <see cref="SourceAppFinder"/> finds the application as their ancestor of the same name. A service macOS starts for an
/// application (an XPC service such as com.apple.WebKit.WebContent for Safari) is a child of launchd, not of the
/// application: the system knows the application it runs for, its "responsible" process, and the service takes its name
/// and has it as parent, so that Safari's sound, played by its WebKit services, is Safari's. Any other program, outside a
/// bundle ("afplay" in /usr/bin), keeps its file name.
/// </summary>
internal static class MacProcessNames
{
    private const string BundleSuffix = ".app";
    private const int Launchd = 1;

    /// <summary>The name of the outermost ".app" folder that holds <paramref name="path"/>, or null when it is in none.</summary>
    public static string? BundleName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var parts = path.Split('/');

        // The last part is the program itself: a bundle holds it, so it is never the bundle.
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (part.Length > BundleSuffix.Length && part.EndsWith(BundleSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return part[..^BundleSuffix.Length];
            }
        }

        return null;
    }

    /// <summary>The file name of the program, else the kernel's short name.</summary>
    public static string FileName(MacProcessFacts facts)
    {
        var name = facts.Path is null ? null : facts.Path[(facts.Path.LastIndexOf('/') + 1)..];
        return string.IsNullOrEmpty(name) ? facts.Command : name;
    }

    /// <summary>The process list, named, lowest pid first.</summary>
    /// <param name="processes">What the kernel gave.</param>
    /// <param name="responsibleFor">The process a pid runs for, as macOS keeps it; itself, or null when unknown.</param>
    public static IReadOnlyList<ProcessEntry> Name(IReadOnlyList<MacProcessFacts> processes, Func<int, int?> responsibleFor)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(responsibleFor);
        var byId = new Dictionary<int, MacProcessFacts>();
        foreach (var process in processes)
        {
            byId[process.Id] = process;
        }

        var entries = new List<ProcessEntry>(processes.Count);
        foreach (var process in processes)
        {
            if (BundleName(process.Path) is { } bundle)
            {
                entries.Add(new ProcessEntry(process.Id, process.ParentId, bundle));
                continue;
            }

            // Asked only for what launchd started: a program a shell started runs for the terminal, and stays itself.
            if (process.ParentId == Launchd
                && responsibleFor(process.Id) is { } owner
                && owner != process.Id
                && owner > Launchd
                && byId.TryGetValue(owner, out var application)
                && BundleName(application.Path) is { } ownerName)
            {
                entries.Add(new ProcessEntry(process.Id, owner, ownerName));
                continue;
            }

            entries.Add(new ProcessEntry(process.Id, process.ParentId, FileName(process)));
        }

        entries.Sort((a, b) => a.Id.CompareTo(b.Id));
        return entries;
    }
}

/// <summary>
/// The process list of macOS, read through libproc: every pid (proc_listpids), its parent and short name
/// (proc_pidinfo, PROC_PIDT_SHORTBSDINFO, which answers for the processes of other users too), the path of its program
/// (proc_pidpath), and for a child of launchd the process it runs for (responsibility_get_pid_responsible_for_pid, looked
/// up at run time: without it, a service keeps its own name). Named by <see cref="MacProcessNames"/>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacProcessSnapshot : IProcessSnapshot
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const string ResponsibleExport = "responsibility_get_pid_responsible_for_pid";
    private const uint AllPids = 1;
    private const int ShortBsdInfo = 13;
    private const int PathMax = 4096;

    private static readonly Lazy<nint> Responsible = new(FindResponsible);

    /// <summary>Whether macOS gives the process a service runs for; said by the proof of the CI.</summary>
    public static bool KnowsResponsible => Responsible.Value != 0;

    public IReadOnlyList<ProcessEntry> Take() => MacProcessNames.Name(ReadFacts(), ResponsibleFor);

    /// <summary>What the kernel says of each process that still runs when it is read.</summary>
    public static IReadOnlyList<MacProcessFacts> ReadFacts()
    {
        var facts = new List<MacProcessFacts>();
        foreach (var pid in ListPids())
        {
            if (pid > 0 && Read(pid) is { } process)
            {
                facts.Add(process);
            }
        }

        return facts;
    }

    /// <summary>The process <paramref name="pid"/> runs for, or null when macOS does not say.</summary>
    public static int? ResponsibleFor(int pid)
    {
        var function = Responsible.Value;
        if (function == 0)
        {
            return null;
        }

        var owner = ((delegate* unmanaged<int, int>)function)(pid);
        return owner > 0 ? owner : null;
    }

    private static int[] ListPids()
    {
        // The count changes between the calls: room for more, and the second call says how many it wrote.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var bytes = proc_listpids(AllPids, 0, null, 0);
            if (bytes <= 0)
            {
                return [];
            }

            var pids = new int[(bytes / sizeof(int)) + 64];
            int written;
            fixed (int* buffer = pids)
            {
                written = proc_listpids(AllPids, 0, buffer, pids.Length * sizeof(int));
            }

            if (written <= 0)
            {
                return [];
            }

            if (written < pids.Length * sizeof(int))
            {
                return pids.AsSpan(0, written / sizeof(int)).ToArray();
            }
        }

        return [];
    }

    private static MacProcessFacts? Read(int pid)
    {
        ShortInfo info;
        if (proc_pidinfo(pid, ShortBsdInfo, 0, &info, sizeof(ShortInfo)) != sizeof(ShortInfo) || info.Pid != (uint)pid)
        {
            // Ended between the listing and the read.
            return null;
        }

        var raw = new ReadOnlySpan<byte>(info.Command, 16);
        var end = raw.IndexOf((byte)0);
        var command = Encoding.UTF8.GetString(end >= 0 ? raw[..end] : raw);
        var path = new byte[PathMax];
        int length;
        fixed (byte* buffer = path)
        {
            length = proc_pidpath(pid, buffer, PathMax);
        }

        return new MacProcessFacts(pid, (int)info.ParentPid, length > 0 ? Encoding.UTF8.GetString(path, 0, length) : null, command);
    }

    private static nint FindResponsible()
    {
        try
        {
            return NativeLibrary.TryGetExport(NativeLibrary.Load(LibSystem), ResponsibleExport, out var function) ? function : 0;
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
    }

    [LibraryImport(LibSystem)]
    private static partial int proc_listpids(uint type, uint typeInfo, int* buffer, int bufferSize);

    [LibraryImport(LibSystem)]
    private static partial int proc_pidinfo(int pid, int flavor, ulong argument, void* buffer, int bufferSize);

    [LibraryImport(LibSystem)]
    private static partial int proc_pidpath(int pid, byte* buffer, uint bufferSize);

    /// <summary>struct proc_bsdshortinfo of sys/proc_info.h: 64 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShortInfo
    {
        public uint Pid;
        public uint ParentPid;
        public uint GroupId;
        public uint Status;
        public fixed byte Command[16];
        public uint Flags;
        public uint Uid;
        public uint Gid;
        public uint RealUid;
        public uint RealGid;
        public uint SavedUid;
        public uint SavedGid;
        public uint Reserved;
    }
}
