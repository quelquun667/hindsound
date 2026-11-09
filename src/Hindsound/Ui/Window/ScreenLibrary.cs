using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>The window's side of the Library page. Kept out of ISettingsBackend, which test doubles implement.</summary>
internal interface ILibraryPanel
{
    /// <summary>
    /// The lines of the latest reading, newest first and capped, with how many the cap left out. Null until a first reading has come
    /// back: an empty list is not "no clips yet" before anyone has looked. Each reading brings new lists; these are never changed in place.
    /// </summary>
    ClipLines? Lines { get; }

    /// <summary>The length read so far. Asked at paint time for the rows drawn, so it does not touch the disk.</summary>
    TimeSpan? LengthOf(ClipGroup line);

    /// <summary>The line is in the rescue folder. Asked at paint time for the rows drawn, so it does not touch the disk.</summary>
    bool IsRescued(ClipGroup line);

    /// <summary>Copies the clip. The answer comes through <see cref="SettingsScreen.ClipGestureEnded"/>.</summary>
    void Copy(ClipGroup line);

    /// <summary>Shows the clip in the Explorer. The answer comes through <see cref="SettingsScreen.ClipGestureEnded"/>.</summary>
    void ShowInFolder(ClipGroup line);

    /// <summary>Looks at the folders again, off the loop. The answer comes through <see cref="SettingsScreen.LibraryReadingArrived"/>.</summary>
    void ReadAgain();
}

/// <summary>The Library page: the clips on disk, and what can be done with the chosen one.</summary>
internal sealed partial class SettingsScreen
{
    public const string ClipListId = "lib.list", ClipCopyId = "foot.copy", ClipShowId = "foot.show";
    public const string NoClipsTitle = "No clips yet", NoClipsBody = "Save a clip with your shortcut, and it shows up here.";
    public const string ClipsCappedNote = "Showing the 2,000 most recent clips.";
    public const string SelectedClipCaption = "Selected clip", NothingSelected = "Nothing selected", ShowInFolderLabel = "Show in folder";
    public const string ClipGoneMessage = "This clip was moved or deleted", ClipFailedMessage = "That didn't work. Your clip is untouched.";
    public const int ClipRowsShown = 7;

    // The first row lines up with the first line of the side bar.
    private const float ClipListTop = BandHeight + 14;
    private const float FootMargin = 24, FootButtonGap = 12, FootButtonHeight = 34;

    private readonly HashSet<string> _goneClips = new(StringComparer.OrdinalIgnoreCase);

    private ILibraryPanel? _library;
    private RowListState _clips = new() { ViewRows = ClipRowsShown };
    private IReadOnlyList<ClipGroup> _clipRows = [];
    private bool _clipsRead;
    private string? _wantedClip;
    private string? _failedClip;

    /// <summary>
    /// The Library page draws from this panel, under a line of the side bar. Null, the default, leaves the side bar without that
    /// line. Unlike Now, the window does not go there: it opens on Library only when a gesture says so.
    /// </summary>
    public void SetLibrary(ILibraryPanel? panel)
    {
        _library = panel;
        _clips = new RowListState { ViewRows = ClipRowsShown };
        _wantedClip = null;
        TakeReading();
        if (panel is null && Section == Section.Library)
        {
            ShowSection(FirstSection);
            return;
        }

        Refresh();
        ReadLibraryAgain();
    }

    /// <summary>A new reading is in the panel. The chosen row and the top row stay the same clips, and the keyboard stays where it is.</summary>
    public void LibraryReadingArrived()
    {
        TakeReading();
        if (Section == Section.Library)
        {
            Refresh();
        }
    }

    /// <summary>More lengths are known. Nothing moves: only the figures are new.</summary>
    public void LibraryLengthsKnown()
    {
        if (Section == Section.Library)
        {
            _host.Invalidate();
        }
    }

    /// <summary>A clip was finished, or the window came back to the front. Only the view on screen is worth a look at the disk.</summary>
    public void ReadLibraryAgain()
    {
        if (Section == Section.Library)
        {
            _library?.ReadAgain();
        }
    }

    /// <summary>Chooses the clip of this path. One not listed yet, a clip just saved, is chosen as soon as a reading brings it.</summary>
    public void SelectClip(string path)
    {
        _wantedClip = _clips.SelectPath(path) ? null : path;
        Refresh();
    }

    /// <summary>
    /// Copy or Show in folder has answered. A clip found gone stays dimmed, with its message, until the next reading: asking for one
    /// here would remove the row before the message can be read.
    /// </summary>
    /// <param name="path">The path of <see cref="ClipGroup.Main"/>.</param>
    /// <param name="outcome">How it ended.</param>
    public void ClipGestureEnded(string path, ShellOutcome outcome)
    {
        switch (outcome)
        {
            case ShellOutcome.Gone:
                _goneClips.Add(path);
                break;
            case ShellOutcome.Failed:
                _failedClip = path;
                break;
            default:
                return;
        }

        Refresh();
    }

    /// <summary>The list on screen. Null on every other page.</summary>
    private RowList? ClipList => Section == Section.Library ? Find(ClipListId) as RowList : null;

    private ClipGroup? ChosenClip => _clips.Selected >= 0 && _clips.Selected < _clipRows.Count ? _clipRows[_clips.Selected] : null;

    private void TakeReading()
    {
        var reading = _library?.Lines;
        var rows = reading?.Lines ?? [];
        _clipsRead = reading is not null;
        _clipRows = rows;

        // A local, not the field: the state holds on to this until the next reading.
        _clips.SetRows(rows.Count, i => rows[i].Main.Path, endRow: reading is { LeftOut: > 0 });
        _goneClips.Clear();
        _failedClip = null;
        if (_wantedClip is { } path && _clips.SelectPath(path))
        {
            _wantedClip = null;
        }
    }

    private void BuildLibrary()
    {
        // The list scrolls inside itself. Any real bottom here would give the page a scrolling room, and its thumb would take the
        // presses on the line of the list's own.
        _contentBottom = BandHeight;
        Add(
            new RowList(_clips, new Box(Left, ClipListTop, WindowWidth - Left, ClipRowsShown * RowListState.RowHeight), ClipRowAt, ClipChosen, _host.Invalidate)
            {
                Id = ClipListId,
                RowPressed = () => _wantedClip = null,
                EndNote = ClipsCappedNote,
                EmptyTitle = _clipsRead ? NoClipsTitle : string.Empty,
                EmptyBody = _clipsRead ? NoClipsBody : null,
                EmptyArea = new Box(Left, BandHeight, Width, ViewBottom - BandHeight),
            },
            scrolls: false);
    }

    private ListRow ClipRowAt(int index)
    {
        var line = _clipRows[index];
        int? seconds = _library?.LengthOf(line) is { } length ? NotificationStates.WholeSeconds(length) : null;
        return ListRow.Of(line, seconds, _library?.IsRescued(line) == true, _goneClips.Contains(line.Main.Path));
    }

    private void ClipChosen()
    {
        _wantedClip = null;
        Refresh();
    }

    private void BuildLibraryFoot()
    {
        var chosen = ChosenClip;
        var showWidth = Button.WidthFor(_canvas, ShowInFolderLabel);
        var copyWidth = Button.WidthFor(_canvas, "Copy");
        var showLeft = WindowWidth - FootMargin - showWidth;
        var copyLeft = showLeft - FootButtonGap - copyWidth;
        var buttonTop = WindowHeight - FootHeight + (FootHeight - FootButtonHeight) / 2;
        var enabled = chosen is not null && !_goneClips.Contains(chosen.Main.Path);
        Chrome(new Painted((c, _) => PaintLibraryFoot(c, chosen, copyLeft - FootButtonGap - FootMargin)));
        Chrome(new Button("Copy", () => ClipGesture(p => p.Copy))
        {
            Id = ClipCopyId,
            Enabled = enabled,
            Bounds = new Box(copyLeft, buttonTop, copyWidth, FootButtonHeight),
        });
        Chrome(new Button(ShowInFolderLabel, () => ClipGesture(p => p.ShowInFolder))
        {
            Id = ClipShowId,
            Enabled = enabled,
            Bounds = new Box(showLeft, buttonTop, showWidth, FootButtonHeight),
        });
    }

    private void ClipGesture(Func<ILibraryPanel, Action<ClipGroup>> gesture)
    {
        if (_library is { } panel && ChosenClip is { } line)
        {
            // What went wrong last time is not true of this attempt.
            _failedClip = null;
            _host.Invalidate();
            gesture(panel)(line);
        }
    }

    private void PaintLibraryFoot(ICanvas c, ClipGroup? chosen, float nameWidth)
    {
        var top = WindowHeight - FootHeight;
        c.FillRect(new Box(0, top, WindowWidth, FootHeight), Theme.Panel);
        c.FillRect(new Box(0, top, WindowWidth, 1), Theme.Line);
        var mid = top + FootHeight / 2;
        var x = FootMargin + c.Measure(SelectedClipCaption, Face.Title, 18) + 16;
        if (chosen is null)
        {
            c.TextMid(SelectedClipCaption, Face.Title, 18, FootMargin, mid, Theme.Muted);
            c.TextMid(NothingSelected, Face.Body, 13, x, mid, Theme.Muted);
            return;
        }

        // Two lines, so the name stays on screen when the list has scrolled away from the clip.
        var first = mid - 12;
        var second = mid + 13;
        c.TextMid(SelectedClipCaption, Face.Title, 18, FootMargin, first, Theme.Muted);
        if (_library?.LengthOf(chosen) is { } length)
        {
            var duration = NotificationStates.FormatDuration(NotificationStates.WholeSeconds(length));
            c.TextMid(duration, Face.Mono, 13, x, first, Theme.Text);
            x += c.Measure(duration, Face.Mono, 13) + 16;
        }

        c.TextMid(StatusText.FileSize(chosen.Files.Sum(file => file.Size)), Face.Mono, 13, x, first, Theme.Muted);
        if (_goneClips.Contains(chosen.Main.Path))
        {
            Message.Error(c, ClipGoneMessage, FootMargin, second);
        }
        else if (string.Equals(_failedClip, chosen.Main.Path, StringComparison.OrdinalIgnoreCase))
        {
            Message.Error(c, ClipFailedMessage, FootMargin, second);
        }
        else
        {
            c.TextMid(c.Fit(ListRow.NameOf(chosen), Face.Body, 12, nameWidth), Face.Body, 12, FootMargin, second, Theme.Muted);
        }
    }
}
