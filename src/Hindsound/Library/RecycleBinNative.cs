using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Hindsound.Library;

/// <summary>
/// IFileOperation and the Recycle Bin, by COM. The generated interfaces work in the trimmed exe, where COM by reflection does not.
/// Every pointer to a shell item stays a pointer: only the order of the slots matters, and nothing is marshalled for nothing.
/// </summary>
internal static partial class RecycleBinNative
{
    private const uint FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000, FOFX_EARLYFAILURE = 0x00100000;
    private const uint CLSCTX_INPROC_SERVER = 1;

    /// <summary>To the bin, with no question, no window and no error box, and stop at the first file that can't go.</summary>
    internal const uint Flags = FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOFX_EARLYFAILURE;

    private static readonly Guid FileOperationClass = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>Whether the drive of this root has a Recycle Bin the system can read.</summary>
    public static bool HasBin(string root)
    {
        var info = new RecycleBinInfo { Size = (uint)Marshal.SizeOf<RecycleBinInfo>() };
        return SHQueryRecycleBinW(root, ref info) >= 0;
    }

    /// <summary>
    /// Queues the deletion of every file in one operation and performs it, with the sink counting into <paramref name="tally"/>.
    /// Call it on a thread of a single-threaded apartment. Returns the code of the operation, or of the first step that failed
    /// before it: then nothing was done.
    /// </summary>
    public static int Recycle(IReadOnlyList<string> paths, RecycleTally tally)
    {
        var clsid = FileOperationClass;
        var iid = typeof(IFileOperation).GUID;
        var hr = CoCreateInstance(in clsid, 0, CLSCTX_INPROC_SERVER, in iid, out var raw);
        if (hr < 0)
        {
            return hr;
        }

        IFileOperation operation;
        try
        {
            operation = (IFileOperation)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(raw, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(raw);
        }

        var items = new List<nint>(paths.Count);
        uint cookie = 0;
        var advised = false;
        try
        {
            hr = operation.SetOperationFlags(Flags);
            if (hr < 0)
            {
                return hr;
            }

            hr = operation.Advise(new ProgressSink(tally), out cookie);
            if (hr < 0)
            {
                return hr;
            }

            advised = true;
            var itemId = ShellItemId;
            foreach (var path in paths)
            {
                hr = SHCreateItemFromParsingName(path, 0, in itemId, out var item);
                if (hr < 0)
                {
                    return hr;
                }

                items.Add(item);
                hr = operation.DeleteItem(item, 0);
                if (hr < 0)
                {
                    return hr;
                }
            }

            // One operation for every file: the sink sees each one before it goes.
            return operation.PerformOperations();
        }
        finally
        {
            if (advised)
            {
                operation.Unadvise(cookie);
            }

            foreach (var item in items)
            {
                Marshal.Release(item);
            }

            ((ComObject)(object)operation).FinalRelease();
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint context, in Guid iid, out nint result);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string root, ref RecycleBinInfo info);

    /// <summary>SHQUERYRBINFO, as a 64-bit process lays it out (shellapi.h packs it by 1 only in 32 bits).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        public uint Size;
        public long Bytes;
        public long Items;
    }

    // Every slot up to the last one used must be declared, in order: the position in the table is what counts.
    [GeneratedComInterface]
    [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    internal partial interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage(nint message);
        [PreserveSig] int SetProgressDialog(nint dialog);
        [PreserveSig] int SetProperties(nint properties);
        [PreserveSig] int SetOwnerWindow(nint owner);
        [PreserveSig] int ApplyPropertiesToItem(nint item);
        [PreserveSig] int ApplyPropertiesToItems(nint items);
        [PreserveSig] int RenameItem(nint item, nint newName, nint sink);
        [PreserveSig] int RenameItems(nint items, nint newName);
        [PreserveSig] int MoveItem(nint item, nint folder, nint newName, nint sink);
        [PreserveSig] int MoveItems(nint items, nint folder);
        [PreserveSig] int CopyItem(nint item, nint folder, nint copyName, nint sink);
        [PreserveSig] int CopyItems(nint items, nint folder);
        [PreserveSig] int DeleteItem(nint item, nint sink);
        [PreserveSig] int DeleteItems(nint items);
        [PreserveSig] int NewItem(nint folder, uint attributes, nint name, nint templateName, nint sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted(out int aborted);
    }

    [GeneratedComInterface]
    [Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
    internal partial interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, nint newName);
        [PreserveSig] int PostRenameItem(uint flags, nint item, nint newName, int result, nint created);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint folder, nint newName);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint folder, nint newName, int result, nint created);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint folder, nint newName);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint folder, nint newName, int result, nint created);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
        [PreserveSig] int PreNewItem(uint flags, nint folder, nint newName);
        [PreserveSig] int PostNewItem(uint flags, nint folder, nint newName, nint templateName, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    /// <summary>Hands the deletions to the tally, which decides; everything else is let through.</summary>
    [GeneratedComClass]
    internal sealed partial class ProgressSink(RecycleTally tally) : IFileOperationProgressSink
    {
        public int StartOperations() => 0;

        public int FinishOperations(int result) => 0;

        public int PreRenameItem(uint flags, nint item, nint newName) => 0;

        public int PostRenameItem(uint flags, nint item, nint newName, int result, nint created) => 0;

        public int PreMoveItem(uint flags, nint item, nint folder, nint newName) => 0;

        public int PostMoveItem(uint flags, nint item, nint folder, nint newName, int result, nint created) => 0;

        public int PreCopyItem(uint flags, nint item, nint folder, nint newName) => 0;

        public int PostCopyItem(uint flags, nint item, nint folder, nint newName, int result, nint created) => 0;

        public int PreDeleteItem(uint flags, nint item) => tally.BeforeDelete(flags);

        public int PostDeleteItem(uint flags, nint item, int result, nint created) => tally.AfterDelete(result, created != 0);

        public int PreNewItem(uint flags, nint folder, nint newName) => 0;

        public int PostNewItem(uint flags, nint folder, nint newName, nint templateName, uint attributes, int result, nint created) => 0;

        public int UpdateProgress(uint workTotal, uint workSoFar) => 0;

        public int ResetTimer() => 0;

        public int PauseTimer() => 0;

        public int ResumeTimer() => 0;
    }
}
