using System.Runtime.InteropServices;
using Hindsound.Library;

namespace Hindsound.Ui;

/// <summary>How a gesture on a line of the library ended. The words on screen are the caller's.</summary>
internal enum ShellOutcome
{
    Done,

    /// <summary>None of the files of the line is there any more.</summary>
    Gone,

    Failed,
}

/// <summary>The one call that reaches the Explorer, so that a test never opens a window.</summary>
internal interface IFolderShell
{
    /// <summary>Shows the folder with these files selected. Answers an HRESULT.</summary>
    int ShowSelected(string folder, IReadOnlyList<string> files);
}

/// <summary>
/// What a line of the library asks of Windows: show its files, or copy them. A line can hold two tracks, and one of them can
/// be gone: both gestures act on what is left. A file name can hold a first name, so nothing here writes a path or a message
/// to the log, only the type of an error and its HRESULT.
/// </summary>
internal static class ShellFiles
{
    /// <summary>
    /// Off the thread of the message loop: the shell can take as long as it likes, and it needs COM, which the single-threaded
    /// apartment of <see cref="OffLoop"/> brings. <paramref name="done"/> runs through <paramref name="post"/>, which knows
    /// nothing of the window: the shell can answer after the window that asked is gone, so what <paramref name="done"/> does
    /// must check, on the loop, that the window is still the one that asked.
    /// </summary>
    public static Thread? ShowInFolderOffLoop(
        ClipGroup group,
        IFolderShell shell,
        Action<Action> post,
        Action<ShellOutcome> done,
        Action<string>? journal = null,
        Func<Action, Action<Exception>, Thread>? start = null,
        Func<string, bool>? folderExists = null) =>
        Launch(
            "show in folder",
            () =>
            {
                var outcome = ShowInFolder(group, shell, File.Exists, journal, folderExists);
                post(() => done(outcome));
            },
            post,
            done,
            journal,
            start);

    /// <summary>
    /// Like <see cref="Copy"/>, but the look at the files, which can wait on a disk asleep or a share that answers slowly, runs
    /// off the loop, as <see cref="ShowInFolderOffLoop"/> does. The rest, the clipboard, runs through <paramref name="post"/>,
    /// on the loop, and so does <paramref name="done"/> when the thread can't start.
    /// </summary>
    public static Thread? CopyOffLoop(
        ClipGroup group,
        Func<string, bool> exists,
        Action<Action> post,
        Func<IReadOnlyList<string>, bool> trySet,
        Action<Func<bool>, Action<bool>> clipboard,
        Action<ShellOutcome> done,
        Action<string>? journal = null,
        Func<Action, Action<Exception>, Thread>? start = null,
        Func<string, bool>? folderExists = null) =>
        Launch(
            "copy",
            () =>
            {
                var paths = StillThere(group, exists, journal);
                post(() => CopyThese(() => paths, () => NoneLeft(group, folderExists, journal), trySet, clipboard, done, journal));
            },
            post,
            done,
            journal,
            start);

    private static Thread? Launch(
        string what,
        Action work,
        Action<Action> post,
        Action<ShellOutcome> done,
        Action<string>? journal,
        Func<Action, Action<Exception>, Thread>? start)
    {
        try
        {
            return (start ?? OffLoop.Start)(work, ex => journal?.Invoke($"{what}: failed ({ClipFolders.Describe(ex)})"));
        }
        catch (Exception ex)
        {
            // Thrown on the caller's side: the gesture must still be answered, and not from inside the click.
            journal?.Invoke($"{what}: no thread ({ClipFolders.Describe(ex)})");
            try
            {
                post(() => done(ShellOutcome.Failed));
            }
            catch (Exception postEx)
            {
                journal?.Invoke($"{what}: no answer ({ClipFolders.Describe(postEx)})");
            }

            return null;
        }
    }

    public static ShellOutcome ShowInFolder(ClipGroup group, IFolderShell shell, Func<string, bool> exists, Action<string>? journal = null, Func<string, bool>? folderExists = null)
    {
        try
        {
            var paths = StillThere(group, exists, journal);
            if (paths.Count == 0)
            {
                return NoneLeft(group, folderExists, journal);
            }

            if (Path.GetDirectoryName(paths[0]) is not { } folder)
            {
                return ShellOutcome.Failed;
            }

            // The call selects inside one folder. The two tracks of a line sit side by side, and a third path has no place in it.
            var files = paths.Where(path => string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)).ToList();
            var result = shell.ShowSelected(folder, files);
            if (result < 0)
            {
                journal?.Invoke($"show in folder: the shell refused (0x{result:X8})");
                return ShellOutcome.Failed;
            }

            return ShellOutcome.Done;
        }
        catch (Exception ex)
        {
            journal?.Invoke($"show in folder: failed ({ClipFolders.Describe(ex)})");
            return ShellOutcome.Failed;
        }
    }

    /// <summary>
    /// Copies what is left of the line, through the way to the clipboard the app already has: <paramref name="clipboard"/> gets
    /// one attempt to try and to retry, and answers whether it worked. <paramref name="done"/> runs once. It looks at the files
    /// on the thread of the caller: from the loop, <see cref="CopyOffLoop"/> is the way.
    /// </summary>
    public static void Copy(
        ClipGroup group,
        Func<string, bool> exists,
        Func<IReadOnlyList<string>, bool> trySet,
        Action<Func<bool>, Action<bool>> clipboard,
        Action<ShellOutcome> done,
        Action<string>? journal = null,
        Func<string, bool>? folderExists = null) =>
        CopyThese(() => StillThere(group, exists, journal), () => NoneLeft(group, folderExists, journal), trySet, clipboard, done, journal);

    private static void CopyThese(
        Func<IReadOnlyList<string>> find,
        Func<ShellOutcome> noneLeft,
        Func<IReadOnlyList<string>, bool> trySet,
        Action<Func<bool>, Action<bool>> clipboard,
        Action<ShellOutcome> done,
        Action<string>? journal)
    {
        var answered = false;
        void Answer(ShellOutcome outcome)
        {
            if (!answered)
            {
                answered = true;
                done(outcome);
            }
        }

        try
        {
            var paths = find();
            if (paths.Count == 0)
            {
                Answer(noneLeft());
                return;
            }

            clipboard(() => Attempt(paths, trySet, journal), copied => Answer(copied ? ShellOutcome.Done : ShellOutcome.Failed));
        }
        catch (Exception ex)
        {
            journal?.Invoke($"copy: failed ({ClipFolders.Describe(ex)})");
            Answer(ShellOutcome.Failed);
        }
    }

    /// <summary>
    /// What a line with no file left is. Gone only where its folder can be seen: a drive unplugged or a folder refused says
    /// nothing about the clip, which may be there when it is back. Without <paramref name="folderExists"/>, always gone.
    /// </summary>
    private static ShellOutcome NoneLeft(ClipGroup group, Func<string, bool>? folderExists, Action<string>? journal)
    {
        if (folderExists is null || group.Files.Count == 0)
        {
            return ShellOutcome.Gone;
        }

        try
        {
            return Path.GetDirectoryName(group.Main.Path) is { } folder && !folderExists(folder) ? ShellOutcome.Failed : ShellOutcome.Gone;
        }
        catch (Exception ex)
        {
            journal?.Invoke($"clip files: a folder can't be checked ({ClipFolders.Describe(ex)})");
            return ShellOutcome.Failed;
        }
    }

    // The retries run later, from the loop: nothing around them could catch what comes out of one.
    private static bool Attempt(IReadOnlyList<string> paths, Func<IReadOnlyList<string>, bool> trySet, Action<string>? journal)
    {
        try
        {
            return trySet(paths);
        }
        catch (Exception ex)
        {
            journal?.Invoke($"copy: failed ({ClipFolders.Describe(ex)})");
            return false;
        }
    }

    /// <summary>The files of the line still on disk, in their order, spelled the way the shell and a paste can read them.</summary>
    internal static IReadOnlyList<string> StillThere(ClipGroup group, Func<string, bool> exists, Action<string>? journal = null)
    {
        var paths = new List<string>(group.Files.Count);
        foreach (var file in group.Files)
        {
            try
            {
                if (exists(file.Path))
                {
                    paths.Add(ClipFolders.Key(file.Path));
                }
            }
            catch (Exception ex)
            {
                journal?.Invoke($"clip files: one can't be checked ({ClipFolders.Describe(ex)})");
            }
        }

        return paths;
    }
}

/// <summary>With <c>--offscreen</c> nothing is ever shown. The log says so, without the path.</summary>
internal sealed class OffscreenFolderShell(Action<string>? journal) : IFolderShell
{
    public int ShowSelected(string folder, IReadOnlyList<string> files)
    {
        journal?.Invoke($"show in folder: {files.Count} file(s) would be selected now");
        return 0;
    }
}

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
