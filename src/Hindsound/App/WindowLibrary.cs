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
    private readonly Func<IPreviewPlayer>? _player;
    private readonly Func<ClipGroup, bool>? _mayPlay;

    /// <summary>Made at the first Play, so that a window where nothing is played never loads MCI. Stopped when the window goes.</summary>
    private ClipPreview? _preview;

    // What each action asked for is waiting to be told, by the action itself: two of the same kind can be on their way.
    private readonly Dictionary<LibraryAction, Action<LibraryOutcome>?> _waiting = new(ReferenceEqualityComparer.Instance);
    private LibraryRead? _index;
    private IndexMatch? _labels;
    private bool _closed;

    /// <param name="screen">The screen of the window, the only one this ever answers to.</param>
    /// <param name="store">The index beside the settings. Written only by an action the screen asks for, after a fresh look.</param>
    /// <param name="folders">Where to look, asked at each look: the settings can change, and so can the clips being saved.</param>
    /// <param name="post">Runs an action on the thread of the loop.</param>
    /// <param name="shell">Shows a file in the Explorer.</param>
    /// <param name="exists">Whether a file is still there. Off the loop: a disk can take its time.</param>
    /// <param name="trySet">One try at the clipboard.</param>
    /// <param name="clipboard">The app's way to the clipboard, which holds the tries and <c>--no-clipboard</c>.</param>
    /// <param name="journal">Told of failures, by type and code only.</param>
    /// <param name="clipStarted">When the oldest press yet to end was made, asked when a look starts. Without it, the moment in the request stands.</param>
    /// <param name="folderExists">Whether a folder can be seen. With it, a clip found missing from a folder that can't be seen is not said to be gone.</param>
    /// <param name="player">What plays a clip: MCI, or a fake. Without it, Play does nothing.</param>
    /// <param name="mayPlay">Whether this clip may be played at all. Without it, any clip may. A clip refused is told as one that can't be played.</param>
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
        Func<DateTime?>? clipStarted = null,
        Func<IPreviewPlayer>? player = null,
        Func<ClipGroup, bool>? mayPlay = null)
    {
        _player = player;
        _mayPlay = mayPlay;
        _folderExists = folderExists;
        _screen = screen;
        _folders = folders;
        _post = post;
        _shell = shell;
        _exists = exists;
        _trySet = trySet;
        _clipboard = clipboard;
        _journal = journal;
        _reads = new LibraryReads(store, post, OnReading, screen.LibraryLengthsKnown, journal) { ClipStarted = clipStarted, ActionFailed = OnActionFailed, ActionOutcomeKnown = OnActionKnown };
    }

    /// <summary>The index as the latest look found it. Null until a look has come back, a damaged file included: its sentence needs it.</summary>
    public LibraryData? Index => _index?.Data;

    public LibraryStatus IndexStatus => _index?.Status ?? LibraryStatus.Missing;

    public ClipLabels LabelsOf(ClipGroup line) => _labels?.LabelsOf(line) ?? new ClipLabels(false, []);

    // Each action looks first, off the loop, and takes its clip from that look by its path: the line on screen may be older.
    public void SetFavorite(ClipGroup line, bool favorite) => Act(() => LibraryAction.SetFavorite(line, favorite), null);

    public void SetIn(ClipGroup line, string collectionId, bool inCollection) =>
        Act(() => LibraryAction.SetInCollection(line, collectionId, inCollection), outcome => _screen.CollectionTickAnswered(collectionId, outcome));

    public void CreateCollection(string name) => Act(() => LibraryAction.CreateCollection(name), _screen.CollectionBoxAnswered);

    public void RenameCollection(string collectionId, string name) =>
        Act(() => LibraryAction.RenameCollection(collectionId, name), _screen.CollectionBoxAnswered);

    public void DeleteCollection(string collectionId) => Act(() => LibraryAction.DeleteCollection(collectionId), _screen.CollectionBoxAnswered);

    public void StartOver() => Act(LibraryAction.StartOver, _screen.CollectionBoxAnswered);

    public ClipLines? Lines { get; private set; }

    /// <summary>A look is under way, or the durations of the last one are still being read.</summary>
    internal bool IsWorking => _reads.IsWorking;

    /// <summary>The actions asked for whose answer has not come yet. For a test that each way out of an action takes it off.</summary>
    internal int ActionsWaiting => _waiting.Count;

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

    public void Play(ClipGroup line)
    {
        if (_closed || _player is null)
        {
            return;
        }

        if (_mayPlay is { } may && !may(line))
        {
            // Told on the loop, as a real failure would be: the screen gets every state the same way.
            _journal("preview: not played, not a file this run may play");
            _post(() => Tell(new PreviewChange(PreviewState.Error, line)));
            return;
        }

        (_preview ??= NewPreview()).Play(line);
    }

    public void StopPlaying() => _preview?.Stop();

    /// <summary>The preview of this window, for a test. Null until the first Play.</summary>
    internal ClipPreview? Preview => _preview;

    private ClipPreview NewPreview()
    {
        ClipPreview? made = null;

        // A state can still be on its way after the window has gone: only the preview this window holds, still open, reaches the
        // screen. A window makes one preview at most and lets it go only in Close, so the identity is a second lock on _closed.
        made = new ClipPreview(_player!(), change => _post(() =>
        {
            if (ReferenceEquals(_preview, made))
            {
                Tell(change);
            }
        }), _journal);
        return made;
    }

    private void Tell(PreviewChange change)
    {
        if (!_closed)
        {
            _screen.PreviewChanged(change);
        }
    }

    /// <summary>A clip has ended, or the window came back to the front: worth a look only if the list is what is on screen.</summary>
    public void LookIfShown() => _screen.ReadLibraryAgain();

    /// <summary>The window is gone. Nothing more reaches its screen, whatever was on its way.</summary>
    /// <param name="workEnded">Runs on the loop if a look was still going on, once it has ended.</param>
    public void Close(Action? workEnded = null)
    {
        _closed = true;
        _waiting.Clear();

        // The sound stops with the window, and its thread goes. Never waits for MCI.
        _preview?.Dispose();
        _preview = null;
        _reads.Close(workEnded);
    }

    private void Act(Func<LibraryAction> make, Action<LibraryOutcome>? answer)
    {
        if (_closed)
        {
            return;
        }

        LibraryAction? action = null;
        try
        {
            action = make();
            _waiting[action] = answer;
            _reads.Act(action, _folders());
        }
        catch (Exception ex)
        {
            // An action left waiting would keep every star asked for lit until the window closes.
            if (action is not null)
            {
                _waiting.Remove(action, out _);
            }

            // Nothing was written. The screen still gets its answer, on the loop as always, or a box would wait for ever.
            _journal($"library action: not asked ({ClipFolders.Describe(ex)})");
            _post(() => Tell(answer, LibraryOutcome.NotWritten));
        }
    }

    private void OnReading(LibrarySnapshot snapshot)
    {
        if (_closed)
        {
            return;
        }

        // The lines, the index and its labels: what the screen draws until the next look. The folders and every clip found stay out.
        Lines = snapshot.Lines;
        _index = snapshot.Library;
        _labels = snapshot.Labels;
        if (snapshot.Acted is not { } acted)
        {
            // A look made while an action is on its way was made before it was written.
            _screen.LibraryReadingArrived(_waiting.Count > 0);
            return;
        }

        _waiting.Remove(acted.Action, out var answer);
        Tell(answer, acted.Outcome);
    }

    private void OnActionFailed(LibraryAction action)
    {
        if (!_closed)
        {
            _waiting.Remove(action, out var answer);
            Tell(answer, LibraryOutcome.NotWritten);
        }
    }

    // The action was applied and what came after it failed: its outcome is the file's, so it is said as it is.
    private void OnActionKnown(LibraryAction action, LibraryOutcome outcome)
    {
        if (_closed)
        {
            return;
        }

        _waiting.Remove(action, out var answer);
        Tell(answer, outcome);

        // The reading that went with it never came: look again, so that the screen shows what the file holds. That look would put
        // out the sentence of the answer, which the user has not had time to read.
        _screen.KeepSentenceThroughNextReading();
        ReadAgain();
    }

    /// <summary>The reading first, then the answer: in the other order, the reading would wipe the sentence of the answer.</summary>
    private void Tell(Action<LibraryOutcome>? answer, LibraryOutcome outcome)
    {
        if (_closed)
        {
            return;
        }

        // The actions still waiting come after this one: the stars they ask for are not in this reading.
        _screen.LibraryReadingArrived(_waiting.Count > 0);
        answer?.Invoke(outcome);
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
