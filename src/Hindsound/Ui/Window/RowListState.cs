namespace Hindsound.Ui.Window;

/// <summary>
/// A list of rows of one height, and everything about it that is not drawing: which rows are on screen, which one is chosen,
/// the keyboard, and what stays put when the rows are read again. It outlives the widget, which a page builds anew at every
/// refresh, and it holds nothing per row: a row is only asked for when it is drawn or searched, so 2,000 cost what 6 do.
/// It scrolls by whole rows. The canvas cannot clip, and a row cut in two would have to draw over what lies around the list.
/// </summary>
internal sealed class RowListState
{
    public const float RowHeight = 40;

    /// <summary>No row: nothing chosen, or nothing at that point.</summary>
    public const int None = -1;

    private const int WheelRows = 2;
    private const float SmallestThumb = 32;

    private Func<int, string> _pathAt = static _ => string.Empty;
    private string? _selectedPath;
    private string? _topPath;
    private int _viewRows = 1;
    private int _hoverRow = None;
    private float _thumbGrab;

    public int Count { get; private set; }

    /// <summary>One more slot after the last row, for a sentence: it scrolls like a row, and cannot be chosen.</summary>
    public bool HasEndRow { get; private set; }

    /// <summary>The first row on screen.</summary>
    public int Top { get; private set; }

    public int Selected { get; private set; } = None;

    /// <summary>What the chosen row is known by. Its number changes when a clip arrives above it; this does not.</summary>
    public string? SelectedPath => _selectedPath;

    public int ViewRows
    {
        get => _viewRows;
        set
        {
            _viewRows = Math.Max(1, value);
            Top = Math.Clamp(Top, 0, MaxTop);
            NoteTop();
        }
    }

    public int MaxTop => Math.Max(0, Slots - _viewRows);

    /// <summary>How many rows to draw from <see cref="Top"/>: the only ones anything is asked about at paint time.</summary>
    public int DrawnRows => Math.Clamp(Count - Top, 0, _viewRows);

    public bool EndRowOnScreen => HasEndRow && Count - Top < _viewRows;

    public bool HasThumb => MaxTop > 0;

    /// <summary>The thumb is being dragged, or was when the button last went down. Lives here because a refresh replaces the widget under the mouse.</summary>
    public bool ThumbDragging { get; set; }

    private int Slots => Count + (HasEndRow ? 1 : 0);

    /// <summary>
    /// The rows were read again, in the background of what the user is doing: the chosen row stays the same row, and the list
    /// does not jump. A list at its head stays at its head, so that a new clip shows; further down, the top row stays on top.
    /// The chosen row is only brought back if the news pushed it off the screen it was on, not if the user had scrolled away from it.
    /// </summary>
    /// <remarks>
    /// It believes what it is given. Readings must be handed over in the order they were asked for and only the latest one
    /// (an older one arriving last would drop a clip that has just been added), never after the window is gone, and with a
    /// journal line that names no path. A change of filter starts at the head of the list, before this.
    /// </remarks>
    /// <param name="count">How many rows there are now.</param>
    /// <param name="pathAt">The path of a row. It is kept until the next reading, so its rows must not change before then, and it must not allocate: a search asks for every row. The earlier reader is never asked again, so one that reads a field the caller has just replaced is no trouble.</param>
    /// <param name="endRow">Leave a slot for a sentence after the last row. An empty list has none.</param>
    /// <returns>True if the chosen row is gone, so nothing is chosen any more.</returns>
    public bool SetRows(int count, Func<int, string> pathAt, bool endRow = false)
    {
        var topPath = _topPath;
        var selectedWasOnScreen = IsOnScreen(Selected);
        _pathAt = pathAt;
        Count = Math.Max(0, count);
        HasEndRow = endRow && Count > 0;

        var top = topPath is null ? None : IndexOf(topPath);
        Top = Math.Clamp(top == None ? Top : top, 0, MaxTop);
        NoteTop();

        if (_selectedPath is not { } path)
        {
            return false;
        }

        Selected = IndexOf(path);
        if (Selected == None)
        {
            _selectedPath = null;
            return true;
        }

        _selectedPath = pathAt(Selected);
        if (selectedWasOnScreen && !IsOnScreen(Selected))
        {
            RevealSelected();
        }

        return false;
    }

    /// <summary>Chooses a row and brings it on screen. Returns true if another row is chosen now.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= Count)
        {
            return false;
        }

        var changed = index != Selected;
        Selected = index;
        _selectedPath = _pathAt(index);
        RevealSelected();
        return changed;
    }

    /// <summary>Chooses the row of this path. A path that is not listed leaves the choice as it is, and returns false.</summary>
    public bool SelectPath(string path)
    {
        var index = IndexOf(path);
        if (index == None)
        {
            return false;
        }

        Select(index);
        return true;
    }

    public void Deselect()
    {
        Selected = None;
        _selectedPath = null;
    }

    /// <summary>Brings the chosen row on screen, by the shortest move. Returns true if the list moved.</summary>
    public bool RevealSelected()
    {
        if (Selected == None)
        {
            return false;
        }

        var top = Selected < Top ? Selected : Selected >= Top + _viewRows ? Selected - _viewRows + 1 : Top;
        return ScrollTo(top);
    }

    /// <summary>Up, Down, Home, End, Page Up and Page Down. Returns false for any other key, and on an empty list.</summary>
    public bool Key(uint vk)
    {
        if (Count == 0 || vk is not (Vk.Up or Vk.Down or Vk.Home or Vk.End or Vk.PageUp or Vk.PageDown))
        {
            return false;
        }

        // With nothing chosen, the first arrow lands on what is in front of the user, not at the far end of 2,000 rows.
        var from = Selected == None ? Math.Min(Top, Count - 1) : Selected;
        var page = Math.Max(1, _viewRows - 1);
        var target = vk switch
        {
            Vk.Home => 0,
            Vk.End => Count - 1,
            Vk.Up => Selected == None ? from : from - 1,
            Vk.Down => Selected == None ? from : from + 1,
            Vk.PageUp => from - page,
            _ => from + page,
        };
        target = Math.Clamp(target, 0, Count - 1);

        // A page moves the rows and the choice together: the chosen row stays where the eye is.
        if (vk is Vk.PageUp or Vk.PageDown)
        {
            ScrollTo(Top + target - from);
        }

        Select(target);

        // Past the last row there is only the sentence: this is the one moment it can be read.
        if (target == Count - 1)
        {
            ScrollTo(MaxTop);
        }

        return true;
    }

    /// <summary>Returns true if the list moved. The chosen row may scroll away: that is what scrolling is for.</summary>
    public bool ScrollTo(int top)
    {
        var next = Math.Clamp(top, 0, MaxTop);
        var moved = next != Top;
        Top = next;
        NoteTop();
        return moved;
    }

    /// <summary>A page of rows, leaving one in sight, as a key on the list does. Returns true if the list moved.</summary>
    public bool Page(bool down) => ScrollTo(Top + (down ? 1 : -1) * Math.Max(1, _viewRows - 1));

    /// <param name="notches">Positive for the wheel going away from the user, as the screen hands them.</param>
    public bool Wheel(int notches) => ScrollTo(Top - notches * WheelRows);

    /// <summary>The row at this height, measured from the top of the list. <see cref="None"/> for the end slot and outside the list.</summary>
    public int RowAt(float y)
    {
        if (y < 0 || y >= _viewRows * RowHeight)
        {
            return None;
        }

        var row = Top + (int)(y / RowHeight);
        return row < Count ? row : None;
    }

    /// <summary>Notes the row under the mouse. Returns true if it is another one: only then has the hover anything to repaint.</summary>
    public bool TrackHover(int row)
    {
        var moved = row != _hoverRow;
        _hoverRow = row;
        return moved;
    }

    public float ThumbHeight(float track) => Math.Min(track, Math.Max(SmallestThumb, track * _viewRows / Math.Max(1, Slots)));

    /// <summary>Where the thumb starts, in a track of this height.</summary>
    public float ThumbY(float track) => MaxTop == 0 ? 0 : (track - ThumbHeight(track)) * Top / MaxTop;

    /// <summary>The button went down on the track, at this height in it. Off the thumb, the thumb's middle comes under the mouse.</summary>
    public void GrabThumb(float y, float track)
    {
        var start = ThumbY(track);
        var height = ThumbHeight(track);
        _thumbGrab = y >= start && y < start + height ? y - start : height / 2;
        ThumbDragging = true;
        DragThumb(y, track);
    }

    /// <summary>Returns true if the list moved.</summary>
    public bool DragThumb(float y, float track)
    {
        var share = (y - _thumbGrab) / Math.Max(1, track - ThumbHeight(track));
        return ScrollTo((int)MathF.Round(Math.Clamp(share, 0, 1) * MaxTop));
    }

    /// <summary>
    /// Remembers what the top row is known by, while the reader that can tell is still the right one: a reading must not have to
    /// ask the earlier reader once the rows it looks at may already be the new ones.
    /// </summary>
    private void NoteTop() => _topPath = Top > 0 && Top < Count ? _pathAt(Top) : null;

    private bool IsOnScreen(int row) => row != None && row >= Top && row < Top + _viewRows;

    private int IndexOf(string path)
    {
        for (var i = 0; i < Count; i++)
        {
            // The same comparison as the file system: a rename that only changes the case is the same clip.
            if (string.Equals(_pathAt(i), path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return None;
    }
}
