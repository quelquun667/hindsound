using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hindsound.Audio;

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
