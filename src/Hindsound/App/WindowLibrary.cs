using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>
/// What the Library page of one window gets from the disk and the shell. It lives and dies with that window, so the durations
/// already read go when the window goes, and with no window there is no one to look at a folder. It asks for nothing by itself:
/// a look happens when the screen asks for one, and the answer comes back through the message loop. Everything here runs on
/// the thread of that loop.
/// </summary>
internal sealed class WindowLibrary : ILibraryPanel
{
    private readonly SettingsScreen _screen;
    private readonly Func<ClipScanRequest> _folders;
    private readonly Action<Action> _post;
    private readonly IFolderShell _shell;
    private readonly Func<string, bool> _exists;
    private readonly Func<IReadOnlyList<string>, bool> _trySet;
    private readonly Action<Func<bool>, Action<bool>> _clipboard;
    private readonly Action<string> _journal;
    private readonly Func<string, bool>? _folderExists;
    private readonly LibraryReads _reads;
    private bool _closed;

    /// <param name="screen">The screen of the window, the only one this ever answers to.</param>
    /// <param name="store">The index beside the settings. Read here, never written: no action on it exists yet.</param>
    /// <param name="folders">Where to look, asked at each look: the settings can change, and so can the clips being saved.</param>
    /// <param name="post">Runs an action on the thread of the loop.</param>
    /// <param name="shell">Shows a file in the Explorer.</param>
    /// <param name="exists">Whether a file is still there. Off the loop: a disk can take its time.</param>
    /// <param name="trySet">One try at the clipboard.</param>
    /// <param name="clipboard">The app's way to the clipboard, which holds the tries and <c>--no-clipboard</c>.</param>
    /// <param name="journal">Told of failures, by type and code only.</param>
    /// <param name="clipStarted">When the oldest press yet to end was made, asked when a look starts. Without it, the moment in the request stands.</param>
    /// <param name="folderExists">Whether a folder can be seen. With it, a clip found missing from a folder that can't be seen is not said to be gone.</param>
    public WindowLibrary(
        SettingsScreen screen,
        LibraryStore store,
        Func<ClipScanRequest> folders,
        Action<Action> post,
        IFolderShell shell,
        Func<string, bool> exists,
        Func<IReadOnlyList<string>, bool> trySet,
        Action<Func<bool>, Action<bool>> clipboard,
        Action<string> journal,
        Func<string, bool>? folderExists = null,
        Func<DateTime?>? clipStarted = null)
    {
        _folderExists = folderExists;
        _screen = screen;
        _folders = folders;
        _post = post;
        _shell = shell;
        _exists = exists;
        _trySet = trySet;
        _clipboard = clipboard;
        _journal = journal;
        _reads = new LibraryReads(store, post, OnReading, screen.LibraryLengthsKnown, journal) { ClipStarted = clipStarted };
    }

    public ClipLines? Lines { get; private set; }

    /// <summary>A look is under way, or the durations of the last one are still being read.</summary>
    internal bool IsWorking => _reads.IsWorking;

    public TimeSpan? LengthOf(ClipGroup line) => _reads.LengthOf(line);

    public bool IsRescued(ClipGroup line) => ClipFolders.IsRescued(line, _folders());

    public void ReadAgain()
    {
        // From the end of a clip too: nothing here may stand in the way of what comes after it.
        try
        {
            _reads.Request(_folders());
        }
        catch (Exception ex)
        {
            _journal($"library read: not asked ({ClipFolders.Describe(ex)})");
        }
    }

    public void Copy(ClipGroup line) =>
        ShellFiles.CopyOffLoop(line, _exists, _post, _trySet, _clipboard, outcome => Answer(line, outcome), _journal, folderExists: _folderExists);

    public void ShowInFolder(ClipGroup line) =>
        ShellFiles.ShowInFolderOffLoop(line, _shell, _post, outcome => Answer(line, outcome), _journal, folderExists: _folderExists);

    /// <summary>A clip has ended, or the window came back to the front: worth a look only if the list is what is on screen.</summary>
    public void LookIfShown() => _screen.ReadLibraryAgain();

    /// <summary>The window is gone. Nothing more reaches its screen, whatever was on its way.</summary>
    /// <param name="workEnded">Runs on the loop if a look was still going on, once it has ended.</param>
    public void Close(Action? workEnded = null)
    {
        _closed = true;
        _reads.Close(workEnded);
    }

    private void OnReading(LibrarySnapshot snapshot)
    {
        // Only the lines: the labels and the rest of the look have nothing to show yet, and would stay in memory until the next one.
        Lines = snapshot.Lines;
        _screen.LibraryReadingArrived();
    }

    // The shell can answer long after the window that asked has gone. This panel is that window's, so its close is the check.
    private void Answer(ClipGroup line, ShellOutcome outcome)
    {
        if (!_closed)
        {
            _screen.ClipGestureEnded(line.Main.Path, outcome);
        }
    }
}
