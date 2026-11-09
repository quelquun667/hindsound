namespace Hindsound.Library;

/// <summary>
/// A change to the index, asked for by the screen. It names the line by its path alone: the line on screen may be older than
/// the disk, so the clip it acts on is taken from the fresh look that comes first.
/// </summary>
public sealed class LibraryAction
{
    // Null for an action on the collections. Empty for a line with no file, which no look can find.
    private readonly string? _clipPath;
    private readonly Func<LibraryStore, ClipGroup, ClipScan, DateTime, LibraryChange> _apply;

    private LibraryAction(string? clipPath, Func<LibraryStore, ClipGroup, ClipScan, DateTime, LibraryChange> apply)
    {
        _clipPath = clipPath;
        _apply = apply;
    }

    public static LibraryAction SetFavorite(ClipGroup line, bool favorite) =>
        new(PathOf(line), (store, clip, scan, now) => store.SetFavorite(clip, favorite, scan, now));

    public static LibraryAction SetInCollection(ClipGroup line, string collectionId, bool inCollection) =>
        new(PathOf(line), (store, clip, scan, now) => store.SetInCollection(clip, collectionId, inCollection, scan, now));

    public static LibraryAction CreateCollection(string name) =>
        new(null, (store, _, scan, now) => store.CreateCollection(name, scan, now));

    public static LibraryAction RenameCollection(string collectionId, string name) =>
        new(null, (store, _, scan, now) => store.RenameCollection(collectionId, name, scan, now));

    public static LibraryAction DeleteCollection(string collectionId) =>
        new(null, (store, _, scan, now) => store.DeleteCollection(collectionId, scan, now));

    private static string PathOf(ClipGroup line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Files.Count > 0 ? line.Main.Path : "";
    }

    /// <summary>What the action did. With no clip of that name in the look: nothing written, and the look stands as it is.</summary>
    internal LibraryChange? Apply(LibraryStore store, IReadOnlyList<ClipGroup> found, ClipScan scan, DateTime nowUtc)
    {
        if (_clipPath is null)
        {
            return _apply(store, new ClipGroup([]), scan, nowUtc);
        }

        var key = ClipFolders.Key(_clipPath);
        foreach (var group in found)
        {
            if (string.Equals(ClipFolders.Key(group.Main.Path), key, StringComparison.OrdinalIgnoreCase))
            {
                return _apply(store, group, scan, nowUtc);
            }
        }

        return null;
    }

    /// <summary>
    /// The outcome when <see cref="Apply"/> found no clip. A clip is gone only where the folder was read: a folder held back for
    /// a save, refused or absent says nothing about the clips in it.
    /// </summary>
    internal LibraryOutcome OutcomeWithoutClip(ClipScan scan) =>
        string.IsNullOrEmpty(_clipPath) || scan.WasRead(Path.GetDirectoryName(_clipPath)) ? LibraryOutcome.ClipGone : LibraryOutcome.FolderNotRead;
}

/// <summary>What became of an action.</summary>
/// <param name="Action">The action, as it was asked for.</param>
/// <param name="Outcome">What happened. <see cref="LibraryOutcome.ClipGone"/> if the clip of its line has gone.</param>
/// <param name="CollectionId">The collection it created.</param>
public sealed record LibraryActed(LibraryAction Action, LibraryOutcome Outcome, string? CollectionId);

/// <summary>
/// What the screen keeps between two looks. Only the lines under the cap: every clip found stays out of it, or a folder of
/// music would stay in memory for as long as the window is open.
/// </summary>
/// <param name="Lines">The lines to show, and how many clips were left out.</param>
/// <param name="FoldersRead">The folders really listed.</param>
/// <param name="Library">The index as this look left it: its state, its collections.</param>
/// <param name="Labels">The labels of each line.</param>
/// <param name="Acted">The action this look was made for, or null.</param>
public sealed record LibrarySnapshot(ClipLines Lines, IReadOnlyList<string> FoldersRead, LibraryRead Library, IndexMatch Labels, LibraryActed? Acted);

/// <summary>
/// Reads the library off the thread of the window, on request and only then. One look at a time: requests made during a look
/// give one more look after it, whatever their number. An action on the index looks first, so it can't write from lines that
/// have grown old. Nothing is kept running: the work ends with the last request, in the thread pool, and <see cref="Close"/>
/// stops it. An instance lives as long as the view, and the durations already read die with it.
/// </summary>
public sealed class LibraryReads
{
    // A paint of the window takes tens of ms, so a thousand files read at once must not ask for one each.
    private const long LengthsToldEveryMs = 200;

    private readonly LibraryStore _store;
    private readonly Action<Action> _toLoop;
    private readonly Action<LibrarySnapshot> _delivered;
    private readonly Action _lengthsKnown;
    private readonly Action<string>? _journal;
    private readonly Func<ClipScanRequest, Action<string>?, CancellationToken, ClipListing> _scan;
    private readonly Func<string, Action<string>?, TimeSpan?> _readLength;
    private readonly Action<Action> _start;
    private readonly Func<DateTime> _utcNow;

    // No timer and no wait handle in it, so there is nothing to give back: a look still running may keep looking at it.
    private readonly CancellationTokenSource _close = new();

    private readonly object _gate = new();
    private readonly Queue<(LibraryAction Action, ClipScanRequest Folders)> _actions = new();
    private ClipScanRequest? _wanted;
    private bool _running;
    private bool _closed;
    private Action? _workEnded;
    private Dictionary<string, KnownLength> _lengths = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="store">The index.</param>
    /// <param name="toLoop">Runs an action on the thread of the window: every answer goes through it.</param>
    /// <param name="delivered">Takes a look, on the thread of the window. Never after <see cref="Close"/>.</param>
    /// <param name="lengthsKnown">More durations can be had from <see cref="LengthOf"/>, on the thread of the window. Never after <see cref="Close"/>.</param>
    /// <param name="journal">Told of a failure: only the type of the exception and its code, never a path or a message.</param>
    public LibraryReads(LibraryStore store, Action<Action> toLoop, Action<LibrarySnapshot> delivered, Action lengthsKnown, Action<string>? journal = null)
        : this(store, toLoop, delivered, lengthsKnown, journal, ClipScanner.Scan, AudioFileLength.Read, static work => _ = Task.Run(work), static () => DateTime.UtcNow)
    {
    }

    /// <summary>For the tests: a look that can be held, durations that can be counted, a thread that can be seen, the time.</summary>
    internal LibraryReads(
        LibraryStore store,
        Action<Action> toLoop,
        Action<LibrarySnapshot> delivered,
        Action lengthsKnown,
        Action<string>? journal,
        Func<ClipScanRequest, Action<string>?, CancellationToken, ClipListing> scan,
        Func<string, Action<string>?, TimeSpan?> readLength,
        Action<Action> start,
        Func<DateTime> utcNow)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(toLoop);
        ArgumentNullException.ThrowIfNull(delivered);
        ArgumentNullException.ThrowIfNull(lengthsKnown);
        _store = store;
        _toLoop = toLoop;
        _delivered = delivered;
        _lengthsKnown = lengthsKnown;
        _journal = journal;
        _scan = scan;
        _readLength = readLength;
        _start = start;
        _utcNow = utcNow;
    }

    /// <summary>False once the last request has been served: no work is left in the thread pool.</summary>
    internal bool IsWorking
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// When the oldest press yet to end was made, asked when a look starts and not when it is requested: a look kept waiting
    /// behind another one may start after a press. Safe to call from any thread. When set, it replaces
    /// <see cref="ClipScanRequest.ClipStartedUtc"/>.
    /// </summary>
    public Func<DateTime?>? ClipStarted { get; set; }

    /// <summary>
    /// Asks for a look. The file of the index gives <see cref="ClipScanRequest.IndexFolders"/>, whatever this one says. Call it
    /// at the opening of the view, and, only while the Library view is the one on screen, at the end of a clip and when the
    /// window comes back to the front; on coming back to the Library view from another one, ask again. A hidden list is not
    /// worth a listing of the folders.
    /// </summary>
    public void Request(ClipScanRequest folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _wanted = folders;
            StartIfIdle();
        }
    }

    /// <summary>
    /// Asks for an action, with a fresh look before it: the answer is a look with <see cref="LibrarySnapshot.Acted"/> set.
    /// Actions keep their order.
    /// </summary>
    public void Act(LibraryAction action, ClipScanRequest folders)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(folders);
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _actions.Enqueue((action, folders));
            StartIfIdle();
        }
    }

    /// <summary>
    /// The view is closed: no look is started or finished, no duration is read and nothing more is delivered. Call it on the
    /// thread of the window. An action already asked for is still written, then the work ends: the user asked for the gesture,
    /// and it must not be lost for having closed the window at once, even though the answer may never reach a window that
    /// is closed (nothing is shown ahead of the answer, so there is no star on screen to take back).
    /// </summary>
    /// <param name="workEnded">
    /// Runs once on the thread of the window, if work was still going on at the close and when it has ended. By then the action
    /// is written: a window opened in the meantime has read the index before it, and can ask again. The thread that held a
    /// list while the window was closed is over too, so this is the time to give back what it held. Never if no work was left.
    /// </param>
    public void Close(Action? workEnded = null)
    {
        lock (_gate)
        {
            _closed = true;
            _wanted = null;
            _workEnded = workEnded;
        }

        _close.Cancel();
    }

    /// <summary>The duration of a line, if read and if its file has not changed. Null shows a line with no duration. For the thread of the window: it touches no disk.</summary>
    public TimeSpan? LengthOf(ClipGroup line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            foreach (var file in line.Files)
            {
                if (_lengths.TryGetValue(file.Path, out var known) && known.IsOf(file))
                {
                    return known.Length;
                }
            }

            return null;
        }
    }

    private void StartIfIdle()
    {
        if (!_running)
        {
            _running = true;
            _start(Work);
        }
    }

    private void Work()
    {
        Action? ended;
        while (TakeNext(out var action, out var folders, out ended))
        {
            try
            {
                Look(folders, action);
            }
            catch (Exception ex)
            {
                // What is on screen stays as it is. The next request, or the next window, looks again.
                _journal?.Invoke($"library read: failed ({ClipFolders.Describe(ex)})");
            }
        }

        if (ended is not null)
        {
            // Not through OnLoop: the view is closed, which is what this is for.
            _toLoop(() =>
            {
                try
                {
                    ended();
                }
                catch (Exception ex)
                {
                    _journal?.Invoke($"library read: an answer failed ({ClipFolders.Describe(ex)})");
                }
            });
        }
    }

    private bool TakeNext(out LibraryAction? action, out ClipScanRequest folders, out Action? ended)
    {
        ended = null;
        lock (_gate)
        {
            if (_actions.TryDequeue(out var next))
            {
                (action, folders) = next;
                return true;
            }

            action = null;
            if (_wanted is { } wanted)
            {
                _wanted = null;
                folders = wanted;
                return true;
            }

            folders = null!;
            _running = false;
            ended = _workEnded;
            _workEnded = null;
            return false;
        }
    }

    private void Look(ClipScanRequest folders, LibraryAction? action)
    {
        // One line for one failure in one look: a folder of locked files must not fill the log.
        var told = new HashSet<string>();
        var sink = _journal;
        Action<string>? journal = sink is null ? null : line =>
        {
            if (told.Add(line))
            {
                sink(line);
            }
        };

        // A look stops at the closing of the view. The look an action needs goes to its end, or the action would write from half of it.
        var cancel = action is null ? _close.Token : CancellationToken.None;
        var library = _store.Read();
        if (cancel.IsCancellationRequested)
        {
            return;
        }

        // Asked here, just before the folders are listed: the request may be older than a press.
        if (ClipStarted is { } clipStarted)
        {
            folders = folders with { ClipStartedUtc = clipStarted() };
        }

        var listing = _scan(folders with { IndexFolders = ClipIndexMatch.FoldersCited(library.Data.Clips) }, journal, cancel);
        if (cancel.IsCancellationRequested)
        {
            return;
        }

        // The index gets every clip found, the screen only those under the cap: a clip left out of the lines is not a clip gone.
        var scan = new ClipScan(listing.Groups, listing.FoldersRead);
        LibraryActed? acted = null;
        if (action is not null)
        {
            var change = action.Apply(_store, listing.Groups, scan, _utcNow());
            acted = new LibraryActed(action, change?.Outcome ?? action.OutcomeWithoutClip(scan), change?.CollectionId);
            library = change?.Library ?? library;
        }

        var labels = ClipIndexMatch.Match(library.Data.Clips, scan);
        var lines = ClipScanner.Cap(listing.Groups, group => labels.LabelsOf(group).Any);
        if (_close.IsCancellationRequested)
        {
            return;
        }

        KeepLengthsOf(lines.Lines);
        var snapshot = new LibrarySnapshot(lines, listing.FoldersRead, library, labels, acted);
        OnLoop(() => _delivered(snapshot));
        ReadLengths(lines.Lines, journal);
    }

    /// <summary>Runs on the loop unless the view has closed by then, and nothing that fails there can reach a log with its message.</summary>
    private void OnLoop(Action answer) => _toLoop(() =>
    {
        if (_close.IsCancellationRequested)
        {
            return;
        }

        try
        {
            answer();
        }
        catch (Exception ex)
        {
            _journal?.Invoke($"library read: an answer failed ({ClipFolders.Describe(ex)})");
        }
    });

    /// <summary>The durations of files that changed or left go away, so the cache is never larger than the lines.</summary>
    private void KeepLengthsOf(IReadOnlyList<ClipGroup> lines)
    {
        lock (_gate)
        {
            // Sized for the lines: left to grow by itself, it would keep room for twice as many.
            var kept = new Dictionary<string, KnownLength>(lines.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                foreach (var file in line.Files)
                {
                    if (_lengths.TryGetValue(file.Path, out var known) && known.IsOf(file))
                    {
                        kept[file.Path] = known;
                    }
                }
            }

            _lengths = kept;
        }
    }

    /// <summary>
    /// Newest first, which is the top of the list. A duration unknown is looked for again at the next look: a file locked or
    /// kept in the cloud today can be read tomorrow, at the same size and date. The names are on screen by now.
    /// </summary>
    private void ReadLengths(IReadOnlyList<ClipGroup> lines, Action<string>? journal)
    {
        var toTell = false;
        var toldAt = Environment.TickCount64;
        foreach (var line in lines)
        {
            // More work waiting: the look it brings starts here again, without what was already read.
            if (ShouldStopReadingLengths())
            {
                break;
            }

            if (LengthOf(line) is not null)
            {
                continue;
            }

            foreach (var file in line.Files)
            {
                if (_readLength(file.Path, journal) is { } length)
                {
                    lock (_gate)
                    {
                        _lengths[file.Path] = new KnownLength(file.Size, file.WrittenUtc, length);
                    }

                    toTell = true;
                    break;
                }
            }

            if (toTell && Environment.TickCount64 - toldAt >= LengthsToldEveryMs)
            {
                OnLoop(_lengthsKnown);
                toTell = false;
                toldAt = Environment.TickCount64;
            }
        }

        if (toTell)
        {
            OnLoop(_lengthsKnown);
        }
    }

    private bool ShouldStopReadingLengths()
    {
        lock (_gate)
        {
            return _closed || _wanted is not null || _actions.Count > 0;
        }
    }

    /// <summary>A duration, and the size and date of the file it was read from.</summary>
    private readonly record struct KnownLength(long Size, DateTime WrittenUtc, TimeSpan Length)
    {
        public bool IsOf(in ClipFile file) => Size == file.Size && WrittenUtc == file.WrittenUtc;
    }
}
