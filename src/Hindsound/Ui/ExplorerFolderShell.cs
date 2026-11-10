using System.Runtime.InteropServices;
using Hindsound.Library;

namespace Hindsound.Ui;

/// <summary>The real call. Run it on a thread that has COM: without it the shell refuses.</summary>
internal sealed unsafe partial class ExplorerFolderShell : IFolderShell
{
    public int ShowSelected(string folder, IReadOnlyList<string> files) => WithItems(folder, files, Open);

    private static int Open(nint folder, nint[] children)
    {
        fixed (nint* items = children)
        {
            return SHOpenFolderAndSelectItems(folder, (uint)children.Length, items, 0);
        }
    }

    /// <summary>Turns the paths into what the call takes, and frees it afterwards. Apart, so a test can reach it without opening a window.</summary>
    internal static int WithItems(string folder, IReadOnlyList<string> files, Func<nint, nint[], int> use)
    {
        nint folderItem = 0;
        var fileItems = new nint[files.Count];
        try
        {
            var result = SHParseDisplayName(folder, 0, &folderItem, 0, null);
            if (result < 0)
            {
                return result;
            }

            var children = new nint[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                nint item = 0;
                result = SHParseDisplayName(files[i], 0, &item, 0, null);
                if (result < 0)
                {
                    return result;
                }

                fileItems[i] = item;

                // A pointer inside the full item, freed with it.
                children[i] = ILFindLastID(item);
            }

            return use(folderItem, children);
        }
        finally
        {
            foreach (var item in fileItems)
            {
                ILFree(item);
            }

            ILFree(folderItem);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, nint bindContext, nint* item, uint attributesWanted, uint* attributesFound);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint* items, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial nint ILFindLastID(nint item);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(nint item);
}
