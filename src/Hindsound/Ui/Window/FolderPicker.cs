using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Hindsound.Ui.Window;

/// <summary>The system's folder picker, by COM. The generated interfaces work in the trimmed exe.</summary>
internal static unsafe partial class FolderPicker
{
    private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_NOCHANGEDIR = 0x8, SIGDN_FILESYSPATH = 0x80058000;
    private static readonly Guid DialogClass = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

    private static IFileOpenDialog Create()
    {
        var clsid = DialogClass;
        var iid = typeof(IFileOpenDialog).GUID;
        nint raw;
        var hr = WindowNative.CoCreateInstance(&clsid, 0, WindowNative.CLSCTX_INPROC_SERVER, &iid, &raw);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return (IFileOpenDialog)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(raw, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    /// <summary>Shows the dialog. Null if the user backed out or if it could not open.</summary>
    public static string? Pick(nint owner, string title, string startFolder)
    {
        try
        {
            var dialog = Create();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR);
            dialog.SetTitle(title);
            if (TryItem(startFolder) is { } start)
            {
                dialog.SetFolder(start);
            }

            if (dialog.Show(owner) != 0)
            {
                return null;
            }

            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out var raw);
            var path = Marshal.PtrToStringUni(raw);
            Marshal.FreeCoTaskMem(raw);
            return path;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            UiErrors.Report(ex);
            return null;
        }
    }

    /// <summary>The dialog can be created and its options read and written, with nothing shown. For the checks of the trimmed exe.</summary>
    public static bool SelfCheck()
    {
        var dialog = Create();
        dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
        dialog.GetOptions(out var options);
        dialog.SetTitle("check");
        return (options & FOS_PICKFOLDERS) != 0;
    }

    private static IShellItem? TryItem(string path)
    {
        var iid = typeof(IShellItem).GUID;
        nint raw;
        if (SHCreateItemFromParsingName(path, 0, &iid, &raw) != 0)
        {
            return null;
        }

        try
        {
            return (IShellItem)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(raw, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint context, Guid* iid, nint* result);

    // Every slot up to the last one used must be declared, in order: the position in the table is what counts.
    [GeneratedComInterface]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    internal partial interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint owner);
        void SetFileTypes(uint count, nint specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [GeneratedComInterface]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    internal partial interface IShellItem
    {
        void BindToHandler(nint context, nint handler, nint iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out nint name);
    }
}
