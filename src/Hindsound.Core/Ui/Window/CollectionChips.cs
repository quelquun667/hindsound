using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>A collection on the line, and whether the selected clip is in it.</summary>
internal readonly record struct ChipEntry(string Id, string Name, bool Ticked);

/// <summary>A chip that fits: its name, cut if it must, and where it goes.</summary>
internal readonly record struct PlacedChip(ChipEntry Entry, string Text, Box Box);

/// <summary>A place the keyboard or the mouse can be: a chip, or the +N when <paramref name="Id"/> is null.</summary>
internal readonly record struct ChipStop(string? Id)
{
    public static ChipStop Plus => default;
}

/// <summary>What fits on the line. <see cref="Hidden"/> is what the +N opens, and <see cref="Plus"/> is null if nothing is hidden.</summary>
internal sealed record ChipFit(IReadOnlyList<PlacedChip> Chips, Box? Plus, string PlusText, IReadOnlyList<ChipEntry> Hidden)
{
    public int StopCount => Chips.Count + (Plus is null ? 0 : 1);

    public ChipStop StopAt(int index) => index < Chips.Count ? new ChipStop(Chips[index].Entry.Id) : ChipStop.Plus;

    /// <summary>The stop under a point, or null.</summary>
    public ChipStop? StopUnder(float x, float y)
    {
        for (var i = 0; i < Chips.Count; i++)
        {
            if (Chips[i].Box.Contains(x, y))
            {
                return StopAt(i);
            }
        }

        return Plus is { } plus && plus.Contains(x, y) ? ChipStop.Plus : null;
    }

    /// <summary>Where a stop is now. One that has left the line lands on the +N, which holds it; without one, on the first chip.</summary>
    public int IndexOf(ChipStop? stop)
    {
        if (stop is { Id: { } id })
        {
            for (var i = 0; i < Chips.Count; i++)
            {
                if (Chips[i].Entry.Id == id)
                {
                    return i;
                }
            }
        }

        return Plus is not null && stop is not null ? Chips.Count : 0;
    }

    public Box BoxOf(int index) => index < Chips.Count ? Chips[index].Box : Plus ?? default;
}

/// <summary>The line of collections, without its widget: what fits at any scale, its order, its keyboard.</summary>
internal static class ChipLine
{
    public const float Top = 370, Height = 28, Gap = 8, MaxName = 96, MidY = Top + Height / 2;

    public const string Label = "Selected clip in:", NewLabel = "New collection...";

    public const string ChipsId = "lib.chips", NewId = "lib.new";

    private const float LabelSize = 12, LabelGap = 10, NameSize = 13, ChipPad = 34, TickInset = 8, TickSize = 12, NameInset = 26, PlusPad = 20, NewPad = 12;

    /// <summary>
    /// Why the file gives no line, in its place: the collections cannot be changed. One sentence for each status that does not allow a
    /// write (see <see cref="LibraryRead.CanWrite"/>), so that a line which shows ticks never answers a click with nothing. Null if
    /// nothing has to be said.
    /// </summary>
    public static string? ReadOnlyMessage(LibraryStatus status) => status switch
    {
        LibraryStatus.TooNew => "A newer version wrote your collections. Update to change them.",
        LibraryStatus.Corrupt => "Your collections file has an error. A copy is kept before anything changes.",
        LibraryStatus.Unreadable => "Collections can't be read right now. Your clips are safe.",
        _ => null,
    };

    /// <summary>
    /// What to say of an answer that closes the box, or ticks nothing, though nothing changed. The status of the file cannot say it: after
    /// a failed write the file is as it was and reads as sound. Null for the answers that need no sentence. A refused name is said in
    /// the box while it is open, and here if the box has gone.
    /// </summary>
    public static string? OutcomeMessage(LibraryOutcome outcome) => outcome switch
    {
        LibraryOutcome.NotWritten => "Couldn't save that. Nothing changed.",
        LibraryOutcome.ReadOnly => "Collections can't be changed right now. Your clips are safe.",
        LibraryOutcome.UnknownCollection => "That collection no longer exists.",
        LibraryOutcome.NameTaken => NameBoxState.TakenText,
        LibraryOutcome.NameTooLong => NameBoxState.TooLongText,
        LibraryOutcome.NoFile or LibraryOutcome.ClipGone => SettingsScreen.ClipGoneMessage,

        // The folder was not read, so the clip may well be there: not called gone.
        LibraryOutcome.FolderNotRead => "Can't check this clip's folder right now. Try again.",
        _ => null,
    };

    /// <summary>The ticked ones first. Within each group, the order they came in.</summary>
    public static IReadOnlyList<ChipEntry> TickedFirst(IReadOnlyList<ChipEntry> entries)
    {
        var sorted = new List<ChipEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Ticked)
            {
                sorted.Add(entry);
            }
        }

        foreach (var entry in entries)
        {
            if (!entry.Ticked)
            {
                sorted.Add(entry);
            }
        }

        return sorted;
    }

    public static float NewButtonWidth(ICanvas c) => Button.WidthFor(c, NewLabel, NameSize, NewPad);

    /// <summary>Where the chips must stop, with the New collection... button lined up on the right.</summary>
    public static float ChipsLimit(ICanvas c, float right) => right - NewButtonWidth(c) - Gap;

    public static Button NewButton(ICanvas c, float right, Action click)
    {
        var width = NewButtonWidth(c);
        return new Button(NewLabel, click)
        {
            Id = NewId,
            FontSize = NameSize,
            Bounds = new Box(right - width, Top, width, Height),
        };
    }

    /// <summary>
    /// The chips that fit from <paramref name="left"/> to <paramref name="limit"/> and the +N that holds the rest. Measured on the canvas
    /// of the window, so a scale that makes the text wider simply shows fewer. Stops measuring at the first chip left out: the cost
    /// does not grow with the number of collections.
    /// </summary>
    public static ChipFit Lay(ICanvas c, IReadOnlyList<ChipEntry> entries, float left, float limit)
    {
        var x = MathF.Ceiling(left + c.Measure(Label, Face.Body, LabelSize) + LabelGap);
        var chips = new List<PlacedChip>();
        for (var i = 0; i < entries.Count; i++)
        {
            var text = c.Fit(entries[i].Name, Face.Medium, NameSize, MaxName);
            var width = MathF.Ceiling(c.Measure(text, Face.Medium, NameSize)) + ChipPad;
            var hiddenAfter = entries.Count - i - 1;
            if (x + width + (hiddenAfter > 0 ? Gap + PlusWidth(c, hiddenAfter) : 0) > limit)
            {
                var plusText = PlusLabel(entries.Count - i);
                var hidden = new List<ChipEntry>(entries.Count - i);
                for (var j = i; j < entries.Count; j++)
                {
                    hidden.Add(entries[j]);
                }

                return new ChipFit(chips, new Box(x, Top, PlusWidth(c, hidden.Count), Height), plusText, hidden);
            }

            chips.Add(new PlacedChip(entries[i], text, new Box(x, Top, width, Height)));
            x += width + Gap;
        }

        return new ChipFit(chips, null, string.Empty, []);
    }

    /// <summary>The arrows move along the stops. Returns the stop to go to, the same at an end, or -1 if the key is not for moving.</summary>
    public static int Step(int current, uint vk, int count) => vk switch
    {
        Vk.Left or Vk.Up => Math.Max(0, current - 1),
        Vk.Right or Vk.Down => Math.Min(count - 1, current + 1),
        Vk.Home => 0,
        Vk.End => count - 1,
        _ => -1,
    };

    /// <summary>The square with its mark: a box to tick, so that the line does not read as a filter.</summary>
    public static void PaintTick(ICanvas c, float x, float midY, bool on, uint accent, uint onAccent)
    {
        var tick = new Box(x, midY - TickSize / 2, TickSize, TickSize);
        if (on)
        {
            c.FillRound(tick, 2, accent);
            c.Stroke(tick.X + 2.5f, midY + 0.2f, tick.X + 5, midY + 2.8f, 1.6f, onAccent);
            c.Stroke(tick.X + 5, midY + 2.8f, tick.X + 9.5f, midY - 2.8f, 1.6f, onAccent);
        }
        else
        {
            c.Frame(tick, 2, Theme.Base, Theme.Control);
        }
    }

    public static void PaintChip(ICanvas c, PlacedChip chip, bool hovered, UiState ui)
    {
        var on = chip.Entry.Ticked;
        c.Frame(chip.Box, 4, hovered ? Theme.Hover : Theme.Raised, on ? ui.Accent : Theme.Control);
        PaintTick(c, chip.Box.X + TickInset, chip.Box.MidY, on, ui.Accent, ui.OnAccent);
        c.TextMid(chip.Text, Face.Medium, NameSize, chip.Box.X + NameInset, chip.Box.MidY, on || hovered ? Theme.Text : Theme.Muted);
    }

    /// <summary>The sentence in the place of the line, in amber: nothing moves when the file comes back.</summary>
    public static Painted Notice(string message, float left, float width) => new((c, _) =>
    {
        c.WarningIcon(left, MidY - 7, 14, Theme.Amber);
        var room = width - 18;
        if (c.Measure(message, Face.Body, NameSize) <= room)
        {
            c.TextMid(message, Face.Body, NameSize, left + 18, MidY, Theme.Amber);
            return;
        }

        // Two lines fit in the height of the line: next to Start over, the sentence of a damaged file is never cut.
        c.Wrapped(message, Face.Body, NameSize, left + 18, MidY - c.LineHeight(Face.Body, NameSize), room, Theme.Amber);
    });

    public static void PaintLabel(ICanvas c, float left) => c.TextMid(Label, Face.Body, LabelSize, left, MidY, Theme.Muted);

    private static string PlusLabel(int count) => "+" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static float PlusWidth(ICanvas c, int count) => MathF.Ceiling(c.Measure(PlusLabel(count), Face.Medium, NameSize)) + PlusPad;
}

/// <summary>
/// What outlives the widgets of the line, which are made again at every change on the page: the keyboard's place, the press under
/// way, and the order. The order only changes when the clip does or when the +N list closes: a chip that jumped to the front as
/// it is ticked would leave the mouse over another one, and a second click would tick that one instead of undoing the first.
/// </summary>
internal sealed class ChipLineState
{
    private readonly List<string> _order = [];

    // What each click asked for, by collection, and how many of its actions have not answered yet.
    private readonly Dictionary<string, (bool On, int Waiting)> _pending = [];
    private string? _clip;
    private bool _sortNext = true;

    public ChipStop? Cursor { get; set; }

    /// <summary>
    /// Whether the clip is in the collection, counting what was asked and has not answered. Actions wait in a queue and the screen
    /// only changes with the answer, so a second click before it must start from the first one's request, not from the screen.
    /// </summary>
    /// <param name="id">The collection.</param>
    /// <param name="fromFile">What the last read says.</param>
    public bool IsTicked(string id, bool fromFile) => _pending.TryGetValue(id, out var pending) ? pending.On : fromFile;

    /// <summary>A click asked for this state of the collection and the action has left.</summary>
    public void Asked(string id, bool on) =>
        _pending[id] = (on, _pending.TryGetValue(id, out var pending) ? pending.Waiting + 1 : 1);

    /// <summary>
    /// An action on this collection has answered, whatever it said: once the last one has, the screen goes back to what the file says.
    /// No effect on a collection nothing is waiting on.
    /// </summary>
    public void Answered(string id)
    {
        if (_pending.TryGetValue(id, out var pending))
        {
            if (pending.Waiting <= 1)
            {
                _pending.Remove(id);
            }
            else
            {
                _pending[id] = (pending.On, pending.Waiting - 1);
            }
        }
    }

    /// <summary>The action that puts the clip in a collection or takes it out, noting what it asks for. For the list of the +N.</summary>
    public Action<string, bool> Asking(Action<string, bool> setIn) => (id, on) =>
    {
        Asked(id, on);
        setIn(id, on);
    };

    public ChipStop? Pressed { get; set; }

    /// <summary>The mouse is still over the stop it went down on.</summary>
    public bool PressStaysOver { get; set; }

    public ChipStop? Hover { get; set; }

    /// <summary>The next arrangement puts the ticked ones first: the list of +N has closed, or a collection was made.</summary>
    public void SortNext() => _sortNext = true;

    /// <summary>The entries of a clip, in the order of the line.</summary>
    /// <param name="clipKey">The selected clip, by the path of its line.</param>
    /// <param name="entries">The collections as of now, in the order of the file.</param>
    public IReadOnlyList<ChipEntry> Arrange(string? clipKey, IReadOnlyList<ChipEntry> entries)
    {
        if (!string.Equals(clipKey, _clip, Library.PathRules.Comparison))
        {
            _clip = clipKey;
            _sortNext = true;

            // What was asked of the other clip says nothing of this one.
            _pending.Clear();
        }

        if (_sortNext)
        {
            _sortNext = false;
            var sorted = ChipLine.TickedFirst(entries);
            _order.Clear();
            foreach (var entry in sorted)
            {
                _order.Add(entry.Id);
            }

            return sorted;
        }

        var byId = new Dictionary<string, ChipEntry>(entries.Count);
        foreach (var entry in entries)
        {
            byId[entry.Id] = entry;
        }

        var arranged = new List<ChipEntry>(entries.Count);
        var kept = new List<string>(entries.Count);
        foreach (var id in _order)
        {
            if (byId.Remove(id, out var entry))
            {
                arranged.Add(entry);
                kept.Add(id);
            }
        }

        // A collection that came from elsewhere goes last, in the order of the file: what is on the line does not move.
        foreach (var entry in entries)
        {
            if (byId.ContainsKey(entry.Id))
            {
                arranged.Add(entry);
                kept.Add(entry.Id);
            }
        }

        _order.Clear();
        _order.AddRange(kept);
        return arranged;
    }
}

/// <summary>
/// The line under the list: a box to tick for each collection that fits, then +N. One stop for Tab; the arrows move inside, Space or
/// Enter ticks the chip or opens the +N. New collection... is a button of its own: see <see cref="ChipLine.NewButton"/>. A tick is only
/// asked for here: what is on screen comes from the file, once the action is done.
/// </summary>
internal sealed class CollectionChips : Widget
{
    private readonly ChipLineState _state;
    private readonly Action<string, bool> _setIn;
    private readonly Action<CollectionChips> _openMore;
    private readonly float _left;

    /// <param name="c">What the names are measured with.</param>
    /// <param name="state">What outlives this widget.</param>
    /// <param name="entries">The collections, through <see cref="ChipLineState.Arrange"/>.</param>
    /// <param name="left">Where the label starts.</param>
    /// <param name="limit">Where the chips must stop, see <see cref="ChipLine.ChipsLimit"/>.</param>
    /// <param name="setIn">Put the selected clip in this collection, or take it out.</param>
    /// <param name="openMore">Open the list of what the +N holds.</param>
    public CollectionChips(ICanvas c, ChipLineState state, IReadOnlyList<ChipEntry> entries, float left, float limit, Action<string, bool> setIn, Action<CollectionChips> openMore)
    {
        _state = state;
        _setIn = setIn;
        _openMore = openMore;
        _left = left;
        Id = ChipLine.ChipsId;
        Fit = ChipLine.Lay(c, entries, left, limit);

        // Only the stops: a press on the label is not a press on the line, and an empty line can never be hit.
        var from = Fit.StopCount > 0 ? Fit.BoxOf(0).X : left;
        var to = Fit.StopCount > 0 ? Fit.BoxOf(Fit.StopCount - 1).Right : left;
        Bounds = new Box(from, ChipLine.Top, to - from, ChipLine.Height);
    }

    public ChipFit Fit { get; }

    /// <summary>With no collection there is nothing here to move along.</summary>
    public override bool CanFocus => Enabled && Fit.StopCount > 0;

    /// <summary>The place of the keyboard on the line.</summary>
    public int CursorIndex => Fit.IndexOf(_state.Cursor);

    public override bool OnKey(uint vk)
    {
        if (Fit.StopCount == 0)
        {
            return false;
        }

        if (vk is Vk.Space or Vk.Enter)
        {
            Act(Fit.StopAt(CursorIndex));
            return true;
        }

        var next = ChipLine.Step(CursorIndex, vk, Fit.StopCount);
        if (next < 0)
        {
            return false;
        }

        _state.Cursor = Fit.StopAt(next);
        return true;
    }

    public override void OnPress(float x, float y)
    {
        _state.Pressed = Fit.StopUnder(x, y);
        _state.PressStaysOver = _state.Pressed is not null;
        if (_state.Pressed is { } stop)
        {
            _state.Cursor = stop;
        }
    }

    public override void OnDrag(float x, float y) => _state.PressStaysOver = _state.Pressed is { } stop && Fit.StopUnder(x, y) == stop;

    public override void OnRelease(bool inside)
    {
        var stop = _state.Pressed;
        var over = _state.PressStaysOver;
        _state.Pressed = null;
        _state.PressStaysOver = false;
        if (inside && over && stop is { } pressed)
        {
            Act(pressed);
        }
    }

    /// <summary>The mouse moved over the line. Returns true if it is over another stop: the only moves that are worth a repaint.</summary>
    public bool HoverMoved(float x, float y)
    {
        var under = Fit.StopUnder(x, y);
        if (under == _state.Hover)
        {
            return false;
        }

        _state.Hover = under;
        return true;
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        ChipLine.PaintLabel(c, _left);
        var hover = ui.Hovered(this) ? Fit.StopUnder(ui.MouseX, ui.LocalY(this)) : null;

        // What the next move is compared with is what is lit on screen.
        _state.Hover = hover;
        foreach (var chip in Fit.Chips)
        {
            var shown = chip with { Entry = chip.Entry with { Ticked = _state.IsTicked(chip.Entry.Id, chip.Entry.Ticked) } };
            ChipLine.PaintChip(c, shown, hover is { } h && h.Id == chip.Entry.Id, ui);
        }

        if (Fit.Plus is { } plus)
        {
            c.Frame(plus, 4, hover is { Id: null } ? Theme.Hover : Theme.Raised, Theme.Control);
            c.TextCentered(Fit.PlusText, Face.Medium, 13, plus, Theme.Text);
        }

        if (ui.Focused(this) && Fit.StopCount > 0)
        {
            ui.FocusOutline(c, Fit.BoxOf(CursorIndex), 4);
        }
    }

    private void Act(ChipStop stop)
    {
        if (stop.Id is not { } id)
        {
            if (Fit.Plus is not null)
            {
                _openMore(this);
            }

            return;
        }

        for (var i = 0; i < Fit.Chips.Count; i++)
        {
            var entry = Fit.Chips[i].Entry;
            if (entry.Id == id)
            {
                var on = !_state.IsTicked(id, entry.Ticked);
                _state.Asked(id, on);
                _setIn(id, on);
                return;
            }
        }
    }
}
