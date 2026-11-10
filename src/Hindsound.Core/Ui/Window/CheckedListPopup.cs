using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary>What a key did to the list.</summary>
internal enum CheckedListKey
{
    /// <summary>Not a key of the list.</summary>
    Ignored,

    Handled,

    /// <summary>The list has closed, and the focus goes on to the next stop, as Tab asks.</summary>
    ClosedMoveOn,
}

/// <summary>
/// The list the +N opens: the collections that did not fit on the line, with their boxes. It looks and moves like the list of a
/// dropdown, but a choice does not close it: a clip can go in several in a row. Esc, Tab or a click outside closes it. Who is in the
/// list is fixed when it opens, and the ticks are read as they are now.
/// </summary>
internal sealed class CheckedListPopup
{
    public const int MaxVisible = 8;
    public const float RowHeight = 28, Pad = 4, Width = 220;

    private const float EdgeMargin = 4, AnchorGap = 4, TextInset = 32, TickInset = 11;

    private readonly IReadOnlyList<ChipEntry> _items;
    private readonly Func<string, bool> _ticked;
    private readonly Action<string, bool> _setIn;
    private readonly Action _closed;
    private readonly Box _box;
    private int _top;
    private bool _gone;

    /// <param name="items">Who is in the list.</param>
    /// <param name="anchor">The +N.</param>
    /// <param name="right">How far the list may go to the right.</param>
    /// <param name="ticked">Whether the selected clip is in this collection as of now.</param>
    /// <param name="setIn">Put the selected clip in this collection, or take it out.</param>
    /// <param name="closed">The list has closed: what was ticked here may now move on the line.</param>
    public CheckedListPopup(IReadOnlyList<ChipEntry> items, Box anchor, float right, Func<string, bool> ticked, Action<string, bool> setIn, Action closed)
    {
        _items = items;
        _ticked = ticked;
        _setIn = setIn;
        _closed = closed;
        var height = Math.Min(MaxVisible, items.Count) * RowHeight + 2 * Pad;
        var below = anchor.Bottom + AnchorGap;
        var top = below + height <= SettingsScreen.WindowHeight - EdgeMargin ? below : Math.Max(EdgeMargin, anchor.Y - AnchorGap - height);
        _box = new Box(Math.Min(anchor.X, right - Width), top, Width, height);
    }

    public Box Bounds => _box;

    public int Index { get; private set; }

    public int Count => _items.Count;

    public int Top => _top;

    public void Move(int by)
    {
        Index = Math.Clamp(Index + by, 0, Math.Max(0, _items.Count - 1));
        if (Index < _top)
        {
            _top = Index;
        }
        else if (Index >= _top + MaxVisible)
        {
            _top = Index - MaxVisible + 1;
        }
    }

    public void Wheel(int notches) =>
        _top = Math.Clamp(_top - notches, 0, Math.Max(0, _items.Count - MaxVisible));

    /// <summary>Space or Enter, and a click: the box of the row changes, and the list stays.</summary>
    public void ToggleCurrent()
    {
        if (Index < _items.Count)
        {
            var id = _items[Index].Id;
            _setIn(id, !_ticked(id));
        }
    }

    public void Close()
    {
        if (!_gone)
        {
            _gone = true;
            _closed();
        }
    }

    public CheckedListKey Key(uint vk)
    {
        switch (vk)
        {
            case Vk.Escape:
                Close();
                return CheckedListKey.Handled;
            case Vk.Tab:
                Close();
                return CheckedListKey.ClosedMoveOn;
            case Vk.Enter:
            case Vk.Space:
                ToggleCurrent();
                return CheckedListKey.Handled;
            case Vk.Up:
                Move(-1);
                return CheckedListKey.Handled;
            case Vk.Down:
                Move(1);
                return CheckedListKey.Handled;
            case Vk.PageUp:
                Move(-MaxVisible);
                return CheckedListKey.Handled;
            case Vk.PageDown:
                Move(MaxVisible);
                return CheckedListKey.Handled;
            case Vk.Home:
                Move(-Count);
                return CheckedListKey.Handled;
            case Vk.End:
                Move(Count);
                return CheckedListKey.Handled;
            default:
                return CheckedListKey.Ignored;
        }
    }

    /// <summary>The mouse moved. Returns true if another row is lit.</summary>
    public bool Hover(float x, float y)
    {
        var row = RowAt(x, y);
        if (row < 0 || row == Index)
        {
            return false;
        }

        Index = row;
        return true;
    }

    public void Press(float x, float y)
    {
        var row = RowAt(x, y);
        if (row >= 0)
        {
            Index = row;
            ToggleCurrent();
        }
        else if (!_box.Contains(x, y))
        {
            // The padding at the ends of the list belongs to it: only a press outside closes it.
            Close();
        }
    }

    public void Paint(ICanvas c, uint accent)
    {
        c.Frame(_box, 4, Theme.Raised, Theme.Control);
        var onAccent = AccentColors.TextOn(accent);
        for (var i = _top; i < Math.Min(_items.Count, _top + MaxVisible); i++)
        {
            var row = new Box(_box.X + 1, _box.Y + Pad + (i - _top) * RowHeight, _box.W - 2, RowHeight);
            if (i == Index)
            {
                c.FillRect(row, Theme.Hover);
                c.FillRect(new Box(row.X, row.Y + 5, 3, RowHeight - 10), accent);
            }

            var on = _ticked(_items[i].Id);
            ChipLine.PaintTick(c, row.X + TickInset, row.MidY, on, accent, onAccent);
            c.TextMid(c.Fit(_items[i].Name, Face.Body, 13, row.W - TextInset - 12), Face.Body, 13, row.X + TextInset, row.MidY, on ? Theme.Text : Theme.Muted);
        }

        if (_items.Count > MaxVisible)
        {
            var track = _box.H - 2 * Pad;
            var height = Math.Max(16, track * MaxVisible / _items.Count);
            var y = _box.Y + Pad + (track - height) * _top / (_items.Count - MaxVisible);
            c.FillRound(new Box(_box.Right - 7, y, 4, height), 2, Theme.Control);
        }
    }

    private int RowAt(float x, float y)
    {
        if (!_box.Contains(x, y))
        {
            return -1;
        }

        var offset = y - _box.Y - Pad;
        if (offset < 0)
        {
            return -1;
        }

        var row = _top + (int)(offset / RowHeight);
        return row < _items.Count && row < _top + MaxVisible ? row : -1;
    }
}
