using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hindsound.Audio;

/// <summary>One line of the process list.</summary>
/// <param name="Id">The process id.</param>
/// <param name="ParentId">The id of the process that started it, which may be gone or reused since.</param>
/// <param name="ExeName">The exe file name, such as "Discord.exe".</param>
public sealed record ProcessEntry(int Id, int ParentId, string ExeName);

/// <summary>Lists the running processes, in the order the system gives them.</summary>
public interface IProcessSnapshot
{
    IReadOnlyList<ProcessEntry> Take();
}

/// <summary>
/// The list from a ToolHelp snapshot. This takes no handle to any process, and must stay that way: a game under an
/// anti-cheat must see nothing but the audio API of Windows. So no System.Diagnostics.Process and no OpenProcess.
/// </summary>
public sealed class ToolHelpProcessSnapshot : IProcessSnapshot
{
    private const uint SnapProcess = 0x2;
    private const int NoMoreFiles = 18;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessFirst(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessNext(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    public IReadOnlyList<ProcessEntry> Take()
    {
        using var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot.IsInvalid)
        {
            throw new Win32Exception();
        }

        var processes = new List<ProcessEntry>();
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        var more = ProcessFirst(snapshot, ref entry);
        while (more)
        {
            processes.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile));
            more = ProcessNext(snapshot, ref entry);
        }

        var error = Marshal.GetLastWin32Error();
        if (error != NoMoreFiles)
        {
            throw new Win32Exception(error);
        }

        return processes;
    }
}

/// <summary>Finds which process to capture for an exe name.</summary>
public static class SourceAppFinder
{
    /// <summary>
    /// The process to capture: a capture follows the children of its target, so for an application made of many
    /// processes it is the ancestor with the same exe name. With several instances it is the oldest, taken as the
    /// first one listed: Windows lists processes in the order they started, in practice though not by contract, and
    /// a start date cannot be had without a handle to the process.
    /// </summary>
    /// <returns>The process id, or null if the application is not running.</returns>
    public static int? FindCaptureTarget(IReadOnlyList<ProcessEntry> processes, string exeName)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var named = processes.Where(process => string.Equals(process.ExeName, exeName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var ids = named.Select(process => process.Id).ToHashSet();
        foreach (var process in named)
        {
            if (process.ParentId == process.Id || !ids.Contains(process.ParentId))
            {
                return process.Id;
            }
        }

        // Each one has a parent of the same name, which only reused ids can do: any of them is as good.
        return named.Length == 0 ? null : named[0].Id;
    }
}

/// <summary>Turns audio sessions into the applications to offer.</summary>
public static class AudioAppList
{
    /// <param name="sessions">The process of each session and the name the session gives, if any.</param>
    /// <param name="processes">The process list, which gives the exe names.</param>
    /// <param name="ownProcessId">This app, left out.</param>
    public static IReadOnlyList<AudioAppInfo> Build(IEnumerable<(int ProcessId, string? DisplayName)> sessions, IReadOnlyList<ProcessEntry> processes, int ownProcessId)
    {
        var byId = new Dictionary<int, string>();
        foreach (var process in processes)
        {
            byId[process.Id] = process.ExeName;
        }

        var apps = new Dictionary<string, AudioAppInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (processId, displayName) in sessions)
        {
            if (processId == 0 || processId == ownProcessId || !byId.TryGetValue(processId, out var exeName))
            {
                continue;
            }

            // A name from the session may be a resource reference such as "@%SystemRoot%\...", no use to a person.
            var usable = !string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith('@');
            var fallback = Path.GetFileNameWithoutExtension(exeName);

            // Several processes of one application: the first name that is not just the exe name wins.
            if (!apps.TryGetValue(exeName, out var known) || (usable && known.Name == fallback))
            {
                apps[exeName] = new AudioAppInfo(exeName, usable ? displayName! : fallback);
            }
        }

        return [.. apps.Values];
    }
}
