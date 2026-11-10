using Hindsound.Input;
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

    /// <summary>Plays the clip, in place of whatever plays. Each state comes back through <see cref="SettingsScreen.PreviewChanged"/>.</summary>
    void Play(ClipGroup line)
    {
    }

    /// <summary>Stops what plays. The state comes back through <see cref="SettingsScreen.PreviewChanged"/>.</summary>
    void StopPlaying()
    {
    }

    /// <summary>Looks at the folders again, off the loop. The answer comes through <see cref="SettingsScreen.LibraryReadingArrived"/>.</summary>
    void ReadAgain();

    /// <summary>
    /// The collections and labels of the latest reading of the index. Null until one has come back, and on a panel with no index:
    /// the rows then show no star and the menu no collection. Each reading brings a new one; it is never changed in place.
    /// </summary>
    LibraryData? Index => null;

    /// <summary>The star and the collections of a line, as of <see cref="Index"/>. Asked on each reading and at paint time, so it does not touch the disk.</summary>
    ClipLabels LabelsOf(ClipGroup line) => new(false, []);

    /// <summary>
    /// Puts the star on the line or takes it off. Whatever the outcome, the answer is a new reading, through
    /// <see cref="SettingsScreen.LibraryReadingArrived"/>: until then the row shows the star asked for.
    /// </summary>
    void SetFavorite(ClipGroup line, bool favorite)
    {
    }

    /// <summary>What the latest reading of the index found. Anything but Missing or Loaded makes the collections read-only.</summary>
    LibraryStatus IndexStatus => LibraryStatus.Missing;

    /// <summary>
    /// Puts the line in a collection or takes it out. Whatever the outcome, the answer is a new reading, then
    /// <see cref="SettingsScreen.CollectionTickAnswered"/> with the same collection, in that order.
    /// </summary>
    void SetIn(ClipGroup line, string collectionId, bool inCollection)
    {
    }

    /// <summary>The answer is a new reading, then <see cref="SettingsScreen.CollectionBoxAnswered"/>, in that order. The same for the three below.</summary>
    void CreateCollection(string name)
    {
    }

    void RenameCollection(string collectionId, string name)
    {
    }

    void DeleteCollection(string collectionId)
    {
    }

    /// <summary>Puts a damaged file aside and starts with no collections. The clips are not touched.</summary>
    void StartOver()
    {
    }
}

/// <summary>The Library page: the clips on disk, and what can be done with the chosen one.</summary>
internal sealed partial class SettingsScreen
{
    public const string ClipListId = "lib.list", ClipCopyId = "foot.copy", ClipShowId = "foot.show", ClipPlayId = "foot.play";
    public const string PlayLabel = "Play", StopLabel = "Stop";
    public const string NoClipsTitle = "No clips yet", NoClipsBody = "Save a clip with your shortcut, and it shows up here.";
    public const string ClipsCappedNote = "Showing the 2,000 most recent clips.";
    public const string SelectedClipCaption = "Selected clip", NothingSelected = "Nothing selected", ShowInFolderLabel = "Show in folder";
    public const string ClipGoneMessage = "This clip was moved or deleted", ClipFailedMessage = "That didn't work. Your clip is untouched.";
    public const string ClipFilterId = NameBox.AfterDeleteId;
    public const string StartOverId = "lib.startover", StartOverLabel = "Start over";
    public const int ClipRowsShown = 6;

    // The menu sits on the line of the side bar's first entry, and the list starts under it.
    private const float ClipFilterTop = BandHeight + 10, ClipListTop = BandHeight + 46;

    private static readonly LibraryData NoIndex = new();
    private const float FootMargin = 24, FootButtonGap = 12, FootButtonHeight = 34;

    private readonly HashSet<string> _goneClips = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The star asked for each path since the last reading, which tells how the file has it now.</summary>
    private readonly Dictionary<string, bool> _starsAsked = new(StringComparer.OrdinalIgnoreCase);

    private ILibraryPanel? _library;
    private RowListState _clips = new() { ViewRows = ClipRowsShown };
    private ClipLines? _clipLines;
    private IReadOnlyList<ClipGroup> _clipRows = [];
    private IReadOnlyList<DropItem> _filterItems = [];
    private LibraryChoice _clipChoice = LibraryChoice.All;
    private bool _clipsRead;
    private string? _wantedClip;
    private string? _failedClip;

    /// <summary>What the preview of this window said last, as it came back on the loop.</summary>
    private PreviewChange _preview;

    // The line of collections, and what opens over the page from it.
    private readonly ChipLineState _chipLine = new();
    private CheckedListPopup? _moreList;
    private NameBoxState? _box;

    /// <summary>What the last answer said on the line, in amber, and for which chosen clip. Gone at the next reading, action or clip.</summary>
    private (string? Clip, string Text)? _lineNotice;

    /// <summary>The sentence above came while another page was on screen: the readings up to the first one back on Library keep it.</summary>
    private bool _noticeUnseen;

    /// <summary>The sentence above was said for an action whose own reading never came: the look asked for in its place leaves it be.</summary>
    private bool _noticeKeptOnce;

    /// <summary>
    /// The Library page draws from this panel, under a line of the side bar. Null, the default, leaves the side bar without that
    /// line. Unlike Now, the window does not go there: it opens on Library only when a gesture says so.
    /// </summary>
    public void SetLibrary(ILibraryPanel? panel)
    {
        _library = panel;
        _clips = new RowListState { ViewRows = ClipRowsShown };
        _clipChoice = LibraryChoice.All;
        _wantedClip = null;
        _preview = default;
        DropLibraryOverlays();
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
    public void LibraryReadingArrived() => LibraryReadingArrived(false);

    /// <summary>A new reading is in the panel, as above.</summary>
    /// <param name="actionsStillWaiting">An action is still on its way: this reading was made before it, so it can't say how the file has the stars asked for.</param>
    public void LibraryReadingArrived(bool actionsStillWaiting)
    {
        TakeReading(keepStars: actionsStillWaiting);
        if (Section == Section.Library)
        {
            Refresh();
        }
    }

    /// <summary>More lengths are known. Nothing moves: only the figures are new.</summary>
    public void LibraryLengthsKnown()
    {
        // The box dims the whole window in a full paint only: the figures wait for it to close, which repaints all.
        if (Section == Section.Library && !BoxOpen)
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
        var found = _clips.SelectPath(path);
        if (!found && _clipChoice.Kind != LibraryFilterKind.All)
        {
            // A clip just saved has no label yet: only the whole list can show it.
            _clipChoice = LibraryChoice.All;
            ApplyChoice(toTop: true);
            found = _clips.SelectPath(path);
        }

        _wantedClip = found ? null : path;
        Refresh();
    }

    /// <summary>The box of a collection is open: nothing else on the page takes a key, a click or a paint of its own.</summary>
    public bool BoxOpen => _box is not null;

    /// <summary>The box on screen. Null when none is open.</summary>
    internal NameBoxState? OpenBoxState => _box;

    /// <summary>The list the +N opened. Null when it is closed.</summary>
    internal CheckedListPopup? MoreList => _moreList;

    /// <summary>
    /// A tick asked on the line or in the list of +N has answered. Comes after the reading it brought, so that the line shows the file
    /// again, and the sentence of a failure stays until the next reading.
    /// </summary>
    public void CollectionTickAnswered(string collectionId, LibraryOutcome outcome)
    {
        _chipLine.Answered(collectionId);
        Say(outcome);
        if (Section == Section.Library)
        {
            Refresh();
        }
    }

    /// <summary>What the box asked has answered, after the reading it brought. A refused name keeps the box open, with the reason.</summary>
    public void CollectionBoxAnswered(LibraryOutcome outcome)
    {
        if (_box is not { } box)
        {
            // The box went with the page: the answer is still said on the line.
            Say(outcome);
            Refresh();
            return;
        }

        if (!box.Answered(outcome))
        {
            Refresh();
            return;
        }

        if (box.Kind == NameBoxKind.Create && outcome == LibraryOutcome.Done)
        {
            _chipLine.SortNext();
        }

        Say(outcome);
        CloseBox(outcome);
    }

    /// <summary>What the menu above the list shows.</summary>
    internal LibraryChoice ClipChoice => _clipChoice;

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

    /// <summary>
    /// The preview of this window has a new state. It goes on whatever the page: only Stop, the end of the clip or the window
    /// closing ends it.
    /// </summary>
    public void PreviewChanged(PreviewChange change)
    {
        _preview = change;
        if (Section == Section.Library)
        {
            Refresh();
        }
    }

    /// <summary>The state of the preview as the screen was last told it.</summary>
    internal PreviewChange Preview => _preview;

    /// <summary>Something is being played, whichever clip it is: from Play to Stop or to its end.</summary>
    internal bool PreviewPlaying => _preview.State is PreviewState.Opening or PreviewState.Playing;

    /// <summary>The sentence about the next clip is said: only while something plays, and only where it can be heard by the capture.</summary>
    internal bool SaysPreviewCanEndUpInClip => PreviewPlaying && _status is { } status && ClipPreview.CanEndUpInClip(status);

    /// <summary>
    /// A double click on a row plays it. The first press of it has already chosen the row; a press on the star, the scroll bar or
    /// below the rows plays nothing.
    /// </summary>
    public void MouseDoubleClick(float x, float y)
    {
        var overlay = PopupOpen || BoxOpen;
        MouseDown(x, y);
        if (overlay || BoxOpen || Section != Section.Library || _pressedId != ClipListId || ClipList is not { } list)
        {
            return;
        }

        var row = list.RowToPlayAt(x, y);
        if (row >= 0 && row == _clips.Selected && ChosenClip is { } line && !_goneClips.Contains(line.Main.Path))
        {
            StartPreview(line);
        }
    }

    private void PlayOrStop()
    {
        if (_library is not { } panel)
        {
            return;
        }

        if (ChosenClip is not { } line)
        {
            // A filter or a reading took the clip that plays off the list: the button is still the way to stop it.
            if (PreviewPlaying)
            {
                panel.StopPlaying();
            }

            return;
        }

        if (ClipPreview.ShowsStopFor(_preview, line))
        {
            panel.StopPlaying();
        }
        else
        {
            StartPreview(line);
        }
    }

    private void StartPreview(ClipGroup line)
    {
        if (_library is { } panel)
        {
            panel.Play(line);
        }
    }

    /// <summary>The list on screen. Null on every other page.</summary>
    private RowList? ClipList => Section == Section.Library ? Find(ClipListId) as RowList : null;

    private ClipGroup? ChosenClip => _clips.Selected >= 0 && _clips.Selected < _clipRows.Count ? _clipRows[_clips.Selected] : null;

    private LibraryData ClipIndex => _library?.Index ?? NoIndex;

    /// <summary>The file holds labels that could not be read: its data are empty, and no star or empty list should be said from them.</summary>
    private bool IndexUnread => _library is { IndexStatus: LibraryStatus.Unreadable or LibraryStatus.Corrupt };

    private void TakeReading(bool keepStars = false)
    {
        _clipLines = _library?.Lines;
        _clipsRead = _clipLines is not null;
        var index = ClipIndex;

        // A file that could not be read gives empty data, which says nothing about the collections: the menu and the choice stay
        // as the last good reading left them, rather than losing the collections for a moment.
        if (!IndexUnread || _filterItems.Count == 0)
        {
            _filterItems = LibraryFilter.Menu(index).Select(item => new DropItem(item.Value, item.Label)).ToList();
        }

        if (!IndexUnread)
        {
            // A collection deleted, here or by hand in the file, leaves every clip rather than an empty list with no way to name it.
            _clipChoice = LibraryFilter.StillThere(_clipChoice, index);
        }

        _goneClips.Clear();
        _failedClip = null;

        // A star asked for shows until a reading tells how the file has it. One made before the action was written can't tell.
        if (!keepStars)
        {
            _starsAsked.Clear();
        }

        // A sentence said while another page was up has not been read yet: the readings that come before the page is back, and
        // the one that coming back asks for, leave it be.
        if (_noticeUnseen)
        {
            _noticeUnseen = Section != Section.Library;
        }
        else if (_noticeKeptOnce)
        {
            _noticeKeptOnce = false;
        }
        else
        {
            _lineNotice = null;
        }

        ApplyChoice(toTop: false);
        if (_wantedClip is { } path && _clips.SelectPath(path))
        {
            _wantedClip = null;
        }
    }

    /// <summary>The rows of the menu's choice, out of the latest reading. The chosen clip stays chosen while it is still a row.</summary>
    private void ApplyChoice(bool toTop)
    {
        var lines = _clipLines?.Lines ?? [];
        var rows = _clipChoice.Kind == LibraryFilterKind.All || _library is not { } panel ? lines : Kept(lines, panel, _clipChoice);
        _clipRows = rows;

        // A local, not the field: the state holds on to this until the next reading.
        _clips.SetRows(rows.Count, i => rows[i].Main.Path, endRow: _clipChoice.Kind == LibraryFilterKind.All && _clipLines is { LeftOut: > 0 });
        if (toTop)
        {
            _clips.ScrollTo(0);
        }
    }

    private static List<ClipGroup> Kept(IReadOnlyList<ClipGroup> lines, ILibraryPanel panel, LibraryChoice choice)
    {
        var kept = new List<ClipGroup>();
        foreach (var line in lines)
        {
            if (LibraryFilter.Keeps(choice, panel.LabelsOf(line)))
            {
                kept.Add(line);
            }
        }

        return kept;
    }

    private void PickClipFilter(string value)
    {
        var choice = LibraryFilter.StillThere(LibraryChoice.FromValue(value), ClipIndex);
        if (choice == _clipChoice)
        {
            return;
        }

        // Another view starts at its head, as a new list would.
        _clipChoice = choice;
        ApplyChoice(toTop: true);
        Refresh();
    }

    /// <summary>The star on screen: the one asked for, until a reading says how the file has it.</summary>
    private bool? StarOf(ClipGroup line) =>
        _library is { Index: not null } panel && !IndexUnread
            ? _starsAsked.TryGetValue(line.Main.Path, out var asked) ? asked : panel.LabelsOf(line).Favorite
            : null;

    private void StarPressed(int index)
    {
        // A star the file would refuse is not asked for: the reading that follows a refusal would put it out again, and say nothing.
        // A damaged or unreadable file shows no star at all, as it knows none; a newer one shows its stars and keeps them.
        if (_library is not { IndexStatus: LibraryStatus.Missing or LibraryStatus.Loaded } panel
            || index < 0 || index >= _clipRows.Count || StarOf(_clipRows[index]) is not { } star)
        {
            return;
        }

        var line = _clipRows[index];
        _starsAsked[line.Main.Path] = !star;
        _host.Invalidate();
        panel.SetFavorite(line, !star);
    }

    private void BuildLibrary()
    {
        // The list scrolls inside itself. Any real bottom here would give the page a scrolling room, and its thumb would take the
        // presses on the line of the list's own.
        _contentBottom = BandHeight;
        var filter = new Box(Left, ClipFilterTop, FieldWidth, RowHeight);
        Add(new Dropdown(() => _filterItems, () => _clipChoice.Value, PickClipFilter, OpenPopup) { Id = ClipFilterId, Bounds = filter }, scrolls: false);

        // While something plays where the capture can hear it, the sentence takes the place of Rename and Delete, which come back
        // once it ends: both would not fit on the line.
        if (SaysPreviewCanEndUpInClip)
        {
            var x = filter.Right + 16;
            Add(new Painted((c, _) => c.TextMid(c.Fit(ClipPreview.CanEndUpInClipText, Face.Body, 12, Right - x), Face.Body, 12, x, filter.MidY, Theme.Muted)), scrolls: false);
        }

        // Only a collection has a name to change, and an empty one still needs a way to be deleted.
        else if (LibraryFilter.NameOf(_clipChoice, ClipIndex) is { } name && _clipChoice.CollectionId is { } id)
        {
            var rename = () => OpenBox(NameBoxState.ForRename(id, name, CollectionActions.RenameId, () => ClipIndex.Collections));
            var delete = () => OpenBox(NameBoxState.ForDelete(id, name, CollectionActions.DeleteId));
            foreach (var button in CollectionActions.Build(_canvas, filter, rename, delete))
            {
                Add(button, scrolls: false);
            }
        }

        // Nothing is said of a filter over labels that could not be read: the amber line under the list already says why.
        var empty = IndexUnread && _clipChoice.Kind != LibraryFilterKind.All ? new LibraryEmptyText(string.Empty) : LibraryFilter.EmptyText(_clipChoice);
        Add(
            new RowList(_clips, new Box(Left, ClipListTop, WindowWidth - Left, ClipRowsShown * RowListState.RowHeight), ClipRowAt, ClipChosen, _host.Invalidate)
            {
                Id = ClipListId,
                RowPressed = () => _wantedClip = null,
                StarPressed = StarPressed,
                EndNote = _clipChoice.Kind == LibraryFilterKind.All ? ClipsCappedNote : null,
                EmptyTitle = _clipsRead ? empty.Title : string.Empty,
                EmptyBody = _clipsRead ? empty.Body : null,
                EmptyArea = new Box(Left, BandHeight, Width, ViewBottom - BandHeight),
            },
            scrolls: false);
        BuildCollectionLine();
    }

    /// <summary>
    /// Under the list: the collections of the chosen clip and New collection..., or in their place, in amber, why they cannot be
    /// changed or what the last answer said. With no index or no chosen clip there is no line, as with no clip at all.
    /// </summary>
    private void BuildCollectionLine()
    {
        if (_library is not { Index: { } index } panel)
        {
            return;
        }

        // The file is the same whatever the clip: a file in trouble is said even with nothing chosen.
        if (ChipLine.ReadOnlyMessage(panel.IndexStatus) is { } stuck)
        {
            var right = Right;
            if (panel.IndexStatus == LibraryStatus.Corrupt)
            {
                var width = Button.WidthFor(_canvas, StartOverLabel, 13, 12);
                right -= width + ChipLine.Gap;
                Add(new Button(StartOverLabel, () => OpenBox(NameBoxState.ForStartOver(StartOverId))) { Id = StartOverId, FontSize = 13, Bounds = new Box(Right - width, ChipLine.Top, width, ChipLine.Height) }, scrolls: false);
            }

            Add(ChipLine.Notice(stuck, Left, right - Left), scrolls: false);
            return;
        }

        var chosen = ChosenClip;
        if (_lineNotice is { } notice && !string.Equals(notice.Clip, chosen?.Main.Path, StringComparison.OrdinalIgnoreCase))
        {
            _lineNotice = null;
        }

        var limit = ChipLine.ChipsLimit(_canvas, Right);
        if (_lineNotice is { } said)
        {
            Add(ChipLine.Notice(said.Text, Left, limit - Left), scrolls: false);
        }
        else if (chosen is not null)
        {
            var labels = panel.LabelsOf(chosen);
            var entries = new List<ChipEntry>(index.Collections.Count);
            foreach (var collection in index.Collections)
            {
                entries.Add(new ChipEntry(collection.Id, collection.Name, labels.CollectionIds.Contains(collection.Id)));
            }

            Add(new CollectionChips(_canvas, _chipLine, _chipLine.Arrange(chosen.Main.Path, entries), Left, limit, SetChosenIn, OpenMore), scrolls: false);
        }

        if (chosen is not null)
        {
            Add(ChipLine.NewButton(_canvas, Right, () => OpenBox(NameBoxState.ForCreate(ChipLine.NewId, () => ClipIndex.Collections))), scrolls: false);
        }
    }

    /// <summary>The sentence of an answer, kept for the clip chosen now. Nothing for an answer with nothing to say.</summary>
    private void Say(LibraryOutcome outcome)
    {
        _lineNotice = ChipLine.OutcomeMessage(outcome) is { } text ? (ChosenClip?.Main.Path, text) : null;
        _noticeUnseen = _lineNotice is not null && Section != Section.Library;
    }

    /// <summary>
    /// The answer just given came without its reading, and a look was asked for in its place: that look must not wipe the sentence
    /// of the answer. Only the next reading keeps it, and only if there is a sentence to keep.
    /// </summary>
    public void KeepSentenceThroughNextReading() => _noticeKeptOnce = _lineNotice is not null;

    private bool InChosen(string collectionId) =>
        ChosenClip is { } line && _library is { } panel && panel.LabelsOf(line).CollectionIds.Contains(collectionId);

    private void SetChosenIn(string collectionId, bool inCollection)
    {
        if (_library is { } panel && ChosenClip is { } line)
        {
            _lineNotice = null;
            _host.Invalidate();
            panel.SetIn(line, collectionId, inCollection);
        }
    }

    private void OpenMore(CollectionChips chips)
    {
        if (chips.Fit.Plus is not { } plus || ChosenClip is null)
        {
            return;
        }

        _focusId = ChipLine.ChipsId;
        _moreList = new CheckedListPopup(chips.Fit.Hidden, plus, Right, id => _chipLine.IsTicked(id, InChosen(id)), _chipLine.Asking(SetChosenIn), MoreClosed);
        _host.Invalidate();
    }

    private void MoreClosed()
    {
        _moreList = null;

        // What was ticked in the list comes to the front now, with nothing under the mouse to jump.
        _chipLine.SortNext();
        Refresh();
    }

    private void OpenBox(NameBoxState state)
    {
        LeaveFocused();
        _popup = null;
        _moreList = null;
        _box = state;
        _focusId = state.EntryId;
        Refresh();
    }

    /// <summary>The box, last of all, so that it dims what is under it and draws over it.</summary>
    private void BuildBox()
    {
        if (_box is { } box && Section == Section.Library)
        {
            foreach (var widget in NameBox.Build(_canvas, box, SendBox, () => CloseBox(null)))
            {
                Chrome(widget);
            }
        }
    }

    /// <summary>The box has taken the confirmation, once: the action leaves, and the box waits for its answer.</summary>
    private void SendBox()
    {
        if (_box is not { } box)
        {
            return;
        }

        if (_library is not { } panel)
        {
            CloseBox(null);
            return;
        }

        _lineNotice = null;
        switch (box.Kind)
        {
            case NameBoxKind.Create:
                panel.CreateCollection(box.Field.Draft);
                break;
            case NameBoxKind.Rename:
                panel.RenameCollection(box.CollectionId!, box.Field.Draft);
                break;
            case NameBoxKind.Delete:
                panel.DeleteCollection(box.CollectionId!);
                break;
            default:
                panel.StartOver();
                break;
        }

        Refresh();
    }

    /// <summary>The focus goes back to what opened the box, or to the menu if that went with the collection.</summary>
    private void CloseBox(LibraryOutcome? outcome)
    {
        if (_box is not { } box)
        {
            return;
        }

        _box = null;
        _focusId = box.FocusAfter(outcome);
        Rebuild();
        if (Find(_focusId) is not { CanFocus: true })
        {
            _focusId = ClipFilterId;
        }

        Refresh();
    }

    /// <summary>A key while the box or the list of +N is open. Null if the key goes on as anywhere else.</summary>
    private bool? OverlayKey(uint vk, uint modifiers)
    {
        if (_moreList is { } more)
        {
            switch (more.Key(vk))
            {
                case CheckedListKey.Ignored:
                    // Taken by nobody, as the menu's list does: the line behind the open list must not move its cursor.
                    return false;
                case CheckedListKey.ClosedMoveOn:
                    MoveFocus((modifiers & HotkeyGesture.ModShift) == 0);
                    return true;
                default:
                    _host.Invalidate();
                    return true;
            }
        }

        if (_box is not { } box)
        {
            return null;
        }

        var plain = (modifiers & (HotkeyGesture.ModControl | HotkeyGesture.ModAlt | HotkeyGesture.ModWin)) == 0;
        switch (box.Key(vk, plain, _focusId))
        {
            case NameBoxKey.Cancel:
                CloseBox(null);
                return true;
            case NameBoxKey.Confirm:
                if (box.TryConfirm())
                {
                    SendBox();
                }

                return true;
            case NameBoxKey.Ignore:
                return true;
        }

        // Nothing behind the box moves.
        return vk is Vk.PageUp or Vk.PageDown ? true : null;
    }

    /// <summary>Leaving the page, or a new panel: what was open over it goes without a word.</summary>
    private void DropLibraryOverlays()
    {
        _box = null;
        if (_moreList is not null)
        {
            _moreList = null;
            _chipLine.SortNext();
        }
    }

    private ListRow ClipRowAt(int index)
    {
        var line = _clipRows[index];
        int? seconds = _library?.LengthOf(line) is { } length ? NotificationStates.WholeSeconds(length) : null;
        return ListRow.Of(line, seconds, _library?.IsRescued(line) == true, _goneClips.Contains(line.Main.Path), StarOf(line));
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

        // Stop takes the place and the width of Play, so that nothing moves under the mouse.
        var playWidth = MathF.Max(Button.WidthFor(_canvas, PlayLabel, icon: true), Button.WidthFor(_canvas, StopLabel));
        var playLeft = WindowWidth - FootMargin - playWidth;
        var showLeft = playLeft - FootButtonGap - showWidth;
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
        var stop = ClipPreview.ShowsStopFor(_preview, chosen) || chosen is null && PreviewPlaying;
        Chrome(new Button(stop ? StopLabel : PlayLabel, PlayOrStop)
        {
            Id = ClipPlayId,
            Icon = stop ? ButtonIcon.None : ButtonIcon.Play,
            Enabled = stop || enabled,
            Bounds = new Box(playLeft, buttonTop, playWidth, FootButtonHeight),
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
        else if (ClipPreview.ErrorTextFor(_preview, chosen) is { } cantPlay)
        {
            Message.Error(c, cantPlay, FootMargin, second);
        }
        else
        {
            c.TextMid(c.Fit(ListRow.NameOf(chosen), Face.Body, 12, nameWidth), Face.Body, 12, FootMargin, second, Theme.Muted);
        }
    }
}
