using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>What one row of the list shows. Built only for the rows being drawn.</summary>
/// <param name="Name">The file name, without its folder.</param>
/// <param name="Duration">Null until the file has been read: the name comes first, and nothing moves when this arrives.</param>
/// <param name="Rescued">The clip is in the rescue folder.</param>
/// <param name="TwoFiles">The two tracks of a clip saved as separate files.</param>
/// <param name="Missing">An action found the file gone, and the next reading has not removed the row yet.</param>
/// <param name="Star">Whether the clip is a favorite. Null draws no star: the labels of the clips are not known.</param>
internal readonly record struct ListRow(string Name, string? Duration = null, bool Rescued = false, bool TwoFiles = false, bool Missing = false, bool? Star = null)
{
    /// <param name="group">The line of the scan.</param>
    /// <param name="seconds">The length read so far.</param>
    /// <param name="rescueFolderKey">The rescue folder, through <see cref="ClipFolders.Key"/>, the one rule for two folders being the same.</param>
    /// <param name="missing">See <see cref="Missing"/>.</param>
    public static ListRow Of(ClipGroup group, int? seconds, string rescueFolderKey, bool missing = false)
    {
        var folder = ClipFolders.Key(Path.GetDirectoryName(group.Main.Path) ?? string.Empty);
        return Of(group, seconds, string.Equals(folder, rescueFolderKey, StringComparison.OrdinalIgnoreCase), missing);
    }

    /// <param name="group">The line of the scan.</param>
    /// <param name="seconds">The length read so far.</param>
    /// <param name="rescued">See <see cref="Rescued"/>, when the caller already knows.</param>
    /// <param name="missing">See <see cref="Missing"/>.</param>
    /// <param name="star">See <see cref="Star"/>.</param>
    public static ListRow Of(ClipGroup group, int? seconds, bool rescued, bool missing = false, bool? star = null) =>
        new(
            NameOf(group),
            seconds is { } length ? NotificationStates.FormatDuration(length) : null,
            rescued,
            group.Files.Count > 1,
            missing,
            star);

    /// <summary>
    /// Main is the system track of a clip saved as two files, but the line stands for both, as Copy and Show in folder do: it is
    /// named as the clip, with the scanner's own rule for the suffix so that the two can never disagree.
    /// </summary>
    public static string NameOf(ClipGroup group)
    {
        var path = group.Main.Path;
        return group.Files.Count > 1 && ClipScanner.WithoutSuffix(path, ClipScanner.SystemSuffix) is { } clip
            ? Path.GetFileName(clip)
            : Path.GetFileName(path);
    }
}

/// <summary>
/// The rows of a <see cref="RowListState"/>: only the ones on screen, with a thumb of their own to the right. The list scrolls
/// inside itself, so it does not set <see cref="Widget.Scrolls"/>: the page must add it with scrolls off, and not let the bottom
/// of its content make the page scroll, or the page's own thumb takes every press on the line of this one. The widget is told nothing of the window: a new reading
/// of the rows changes the state and repaints, and has no way to ask for the keyboard.
/// </summary>
internal sealed class RowList : Widget
{
    /// <summary>Between the rows and the edge of the window: the thumb, on the line of the page's own, then the margin.</summary>
    private const float GutterWidth = 32;

    private const float NameInset = 10, DurationRight = 44, NoteRight = 100, NoteGap = 12, ThumbFromRight = 12, ThumbWidth = 6, ThumbGrace = 4, TrackInset = 4;

    private const string Prefix = "Hindsound_", RescuedNote = "Rescued", TwoFilesNote = "2 files";

    /// <summary>The star sits in the last pixels of the row, after the duration; a press there is about the star alone.</summary>
    private const float StarFromRight = 18, StarRadius = 8, StarZone = 36;

    private const uint StarOutline = 0x8E95A3;

    /// <summary>F, for favorite: puts the star on the chosen row or takes it off.</summary>
    public const uint StarKey = 0x46;

    private readonly RowListState _state;
    private readonly Func<int, ListRow> _rowAt;
    private readonly Action _selectionChanged;
    private readonly Action _repaint;

    /// <param name="state">What outlives this widget.</param>
    /// <param name="box">The list and its thumb, down to whole rows of height.</param>
    /// <param name="rowAt">What a row shows. Only asked for the rows drawn.</param>
    /// <param name="selectionChanged">The user chose another row: what depends on the choice has to be laid out again.</param>
    /// <param name="repaint">The rows moved, and nothing else changed.</param>
    public RowList(RowListState state, Box box, Func<int, ListRow> rowAt, Action selectionChanged, Action repaint)
    {
        _state = state;
        _rowAt = rowAt;
        _selectionChanged = selectionChanged;
        _repaint = repaint;
        Bounds = box;
        state.ViewRows = (int)(box.H / RowListState.RowHeight);
    }

    /// <summary>A press landed on a row, whether or not it changes the choice.</summary>
    public Action? RowPressed { get; init; }

    /// <summary>
    /// A press on the star of a row, or <see cref="StarKey"/> on the chosen one, with the index of the row. Null: the stars cannot
    /// be changed, and a press there chooses the row like anywhere else on it.
    /// </summary>
    public Action<int>? StarPressed { get; init; }

    /// <summary>The sentence of the end slot.</summary>
    public string? EndNote { get; init; }

    public string EmptyTitle { get; init; } = string.Empty;

    public string? EmptyBody { get; init; }

    /// <summary>What the empty message centres in, if not the list: the page around it, where nothing else is shown.</summary>
    public Box? EmptyArea { get; init; }

    /// <summary>The keyboard has nothing to do in an empty list.</summary>
    public override bool CanFocus => Enabled && _state.Count > 0;

    private float RowsRight => Bounds.Right - GutterWidth;

    private float ThumbX => Bounds.Right - ThumbFromRight;

    private float TrackTop => Bounds.Y + TrackInset;

    private float TrackHeight => Bounds.H - 2 * TrackInset;

    public override bool OnKey(uint vk)
    {
        if (vk == StarKey)
        {
            return PressStar(_state.Selected);
        }

        var selected = _state.Selected;
        var top = _state.Top;
        if (!_state.Key(vk))
        {
            return false;
        }

        Told(selected, top);
        return true;
    }

    /// <summary>The wheel, for the screen to hand over: it would scroll the page otherwise. A selection does not follow.</summary>
    public void OnWheel(int notches)
    {
        if (_state.Wheel(notches))
        {
            _repaint();
        }
    }

    /// <summary>Page Up or Page Down while the focus is elsewhere on the page: the rows move, and the choice stays with its row.</summary>
    public void OnPage(bool down)
    {
        if (_state.Page(down))
        {
            _repaint();
        }
    }

    /// <summary>The mouse moved over the list. Returns true if it is over another row: the only moves that are worth a repaint.</summary>
    public bool HoverMoved(float x, float y) => _state.TrackHover(RowUnder(x, y));

    public override void OnPress(float x, float y)
    {
        _state.ThumbDragging = false;
        if (x >= RowsRight)
        {
            if (_state.HasThumb && x >= ThumbX - ThumbGrace)
            {
                _state.GrabThumb(y - TrackTop, TrackHeight);
                _repaint();
            }

            return;
        }

        var row = _state.RowAt(y - Bounds.Y);

        // The star is a switch of its own: choosing the row as well would move the foot under a user who only marked a clip.
        if (x >= RowsRight - StarZone && PressStar(row))
        {
            return;
        }

        var selected = _state.Selected;
        var top = _state.Top;
        if (row != RowListState.None)
        {
            // Before the choice is told, and even when it is the row already chosen: the user has just said which clip they want.
            RowPressed?.Invoke();
        }

        _state.Select(row);
        Told(selected, top);
    }

    public override void OnDrag(float x, float y)
    {
        if (_state.ThumbDragging && _state.DragThumb(y - TrackTop, TrackHeight))
        {
            _repaint();
        }
    }

    public override void OnRelease(bool inside)
    {
        if (_state.ThumbDragging)
        {
            _state.ThumbDragging = false;
            _repaint();
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        if (_state.Count == 0)
        {
            PaintEmpty(c);
            return;
        }

        var hover = ui.Hovered(this) ? RowUnder(ui.MouseX, ui.LocalY(this)) : RowListState.None;

        // What the next move is compared with is what is lit on screen, which a scroll, a key or a new reading may have changed
        // under a mouse that did not move.
        _state.TrackHover(hover);
        var top = _state.Top;
        var drawn = _state.DrawnRows;
        for (var i = 0; i < drawn; i++)
        {
            PaintRow(c, ui, top + i, new Box(Bounds.X, Bounds.Y + i * RowListState.RowHeight, RowsRight - Bounds.X, RowListState.RowHeight), hover);
        }

        if (_state.EndRowOnScreen && EndNote is { } note)
        {
            var slot = new Box(Bounds.X, Bounds.Y + drawn * RowListState.RowHeight, RowsRight - Bounds.X, RowListState.RowHeight);
            c.TextCentered(note, Face.Body, 13, slot, Theme.Muted);
        }

        if (_state.HasThumb)
        {
            var lit = _state.ThumbDragging && ReferenceEquals(ui.Pressed, this);
            var thumb = new Box(ThumbX, TrackTop + _state.ThumbY(TrackHeight), ThumbWidth, _state.ThumbHeight(TrackHeight));
            c.FillRound(thumb, 3, lit ? Theme.Muted : Theme.Control);
        }

        if (ui.Focused(this))
        {
            ui.FocusOutline(c, FocusBox(), 4);
        }
    }

    private void Told(int selected, int top)
    {
        if (_state.Selected != selected)
        {
            _selectionChanged();
        }
        else if (_state.Top != top)
        {
            _repaint();
        }
    }

    private bool PressStar(int row)
    {
        if (StarPressed is not { } press || row < 0 || row >= _state.Count || _rowAt(row).Star is null)
        {
            return false;
        }

        press(row);
        return true;
    }

    private int RowUnder(float x, float y) =>
        x >= Bounds.X && x < RowsRight ? _state.RowAt(y - Bounds.Y) : RowListState.None;

    /// <summary>The chosen row, or the whole list when it has scrolled away: the outline must not point at what is not there.</summary>
    private Box FocusBox()
    {
        var offset = _state.Selected - _state.Top;
        return offset >= 0 && offset < _state.DrawnRows
            ? new Box(Bounds.X, Bounds.Y + offset * RowListState.RowHeight, RowsRight - Bounds.X, RowListState.RowHeight)
            : new Box(Bounds.X, Bounds.Y, RowsRight - Bounds.X, Bounds.H);
    }

    private void PaintEmpty(ICanvas c)
    {
        var box = EmptyArea ?? Bounds;
        c.TextCentered(EmptyTitle, Face.Title, 22, box with { Y = box.Y + 6, H = box.H - 40 }, Theme.Text);
        if (EmptyBody is { } body)
        {
            c.TextCentered(body, Face.Body, 14, box with { Y = box.Y + 42, H = box.H - 40 }, Theme.Muted);
        }
    }

    private void PaintRow(ICanvas c, UiState ui, int index, Box row, int hover)
    {
        var selected = _state.Selected;
        var chosen = index == selected;
        if (chosen)
        {
            c.FillRound(row, 4, Theme.Raised);
            c.FillRound(new Box(row.X, row.Y + 10, 3, RowListState.RowHeight - 20), 1.5f, ui.Accent);
        }
        else
        {
            if (index == hover)
            {
                c.FillRound(row, 4, Theme.Panel);
            }

            // The rule under a row, except where the chosen row's own shape already ends it, and under the last one.
            if (index != selected - 1 && (index < _state.Count - 1 || _state.HasEndRow))
            {
                c.FillRect(new Box(row.X, row.Bottom - 1, row.W, 1), Theme.Raised);
            }
        }

        var line = _rowAt(index);
        var mid = row.MidY;

        // The star has the last 44 pixels of the row. The name's room never depends on the duration, which arrives later: it
        // would move every name of the list.
        var limit = row.Right - NoteRight;
        if (line.TwoFiles)
        {
            limit = PaintNote(c, TwoFilesNote, limit, mid);
        }

        if (line.Rescued)
        {
            limit = PaintNote(c, RescuedNote, limit, mid);
        }

        PaintName(c, line, row.X + NameInset, limit, mid, chosen);
        if (line.Duration is { } duration)
        {
            var figures = line.Missing ? Theme.Faded : chosen ? Theme.Text : Theme.Muted;
            c.TextRight(duration, Face.Mono, 13, row.Right - DurationRight, mid, figures);
        }

        if (line.Star is { } star)
        {
            c.Star(row.Right - StarFromRight, mid, StarRadius, star, line.Missing ? Theme.Faded : star ? ui.Accent : StarOutline);
        }
    }

    /// <summary>A note, ending at this x. Returns where what comes before it must end.</summary>
    private static float PaintNote(ICanvas c, string note, float right, float mid)
    {
        c.TextRight(note, Face.Body, 12, right, mid, Theme.Faded);
        return right - c.Measure(note, Face.Body, 12) - NoteGap;
    }

    /// <summary>The name, cut if it must. The part that is the same on every line steps back, so the eye lands on what differs.</summary>
    private static void PaintName(ICanvas c, ListRow line, float x, float limit, float mid, bool chosen)
    {
        var quiet = line.Missing || !chosen ? Theme.Faded : Theme.Muted;
        var strong = line.Missing ? Theme.Faded : chosen ? Theme.Text : Theme.Muted;
        var name = c.Fit(line.Name, Face.Body, 14, limit - x);

        // A name cut before its dot has no extension left: only a dot at the very end is left, and that is not one.
        var extension = Path.GetExtension(name.AsSpan()).Length;
        var head = name.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix.Length : 0;

        x = PaintPart(c, name[..head], x, mid, quiet);
        x = PaintPart(c, name[head..^extension], x, mid, strong);
        PaintPart(c, name[^extension..], x, mid, quiet);
    }

    private static float PaintPart(ICanvas c, string text, float x, float mid, uint color)
    {
        if (text.Length == 0)
        {
            return x;
        }

        c.TextMid(text, Face.Body, 14, x, mid, color);
        return x + c.Measure(text, Face.Body, 14);
    }
}
