using Hindsound.Library;

namespace Hindsound.Ui.Window;

internal enum NameBoxKind
{
    Create,
    Rename,
    Delete,

    /// <summary>A damaged file of collections is put aside for an empty one.</summary>
    StartOver,
}

/// <summary>What the box does with a key before the screen does.</summary>
internal enum NameBoxKey
{
    /// <summary>Not for the box: the widget with the focus takes it, as anywhere.</summary>
    PassOn,

    /// <summary>The box closes and nothing changes.</summary>
    Cancel,

    /// <summary>The name is asked for.</summary>
    Confirm,

    /// <summary>The key is swallowed: the action has left and cannot be called back, so the box has to stay as it is until it answers.</summary>
    Ignore,
}

/// <summary>
/// The small box of a collection, and all it has to remember, as its widgets are made again at each key: create, rename, or confirm a
/// deletion. Nothing here reaches the file. It says what is asked, and the answer of the action comes back to <see cref="Answered"/>.
/// </summary>
internal sealed class NameBoxState
{
    public const string TakenText = "That name is already used", TooLongText = "40 characters at most";
    public const string StartOverQuestion = "Start over with no collections? Your clips stay.";

    private readonly Func<IReadOnlyList<LibraryCollection>> _collections;

    /// <param name="kind">What the box is for.</param>
    /// <param name="collectionId">The collection to rename or delete.</param>
    /// <param name="name">Its name, in the field of a rename and in the question of a deletion.</param>
    /// <param name="returnId">The stop that opened the box: the focus goes back there.</param>
    /// <param name="collections">The collections as of now: the file may change while the box is open.</param>
    private NameBoxState(NameBoxKind kind, string? collectionId, string name, string returnId, Func<IReadOnlyList<LibraryCollection>> collections)
    {
        Kind = kind;
        CollectionId = collectionId;
        Name = name;
        ReturnId = returnId;
        _collections = collections;
        Field = new FieldState(kind == NameBoxKind.Rename ? name : string.Empty);
    }

    public NameBoxKind Kind { get; }

    public string? CollectionId { get; }

    public string Name { get; }

    public string ReturnId { get; }

    /// <summary>The text as typed, even when it cannot be kept.</summary>
    public FieldState Field { get; }

    /// <summary>The action has left and has not answered: a second press would make it twice.</summary>
    public bool Waiting { get; private set; }

    public string Title => Kind == NameBoxKind.Rename ? "Rename collection" : "New collection";

    public string ConfirmLabel => Kind switch
    {
        NameBoxKind.Create => "Create",
        NameBoxKind.Rename => "Rename",
        NameBoxKind.StartOver => SettingsScreen.StartOverLabel,
        _ => "Delete",
    };

    /// <summary>The first control of the box: the name, or Cancel for a deletion, so that Enter on arrival deletes nothing.</summary>
    public string EntryId => IsQuestion ? NameBox.CancelId : NameBox.NameId;

    /// <summary>A question with no name to type: a deletion or a fresh start.</summary>
    public bool IsQuestion => Kind is NameBoxKind.Delete or NameBoxKind.StartOver;

    public bool CanConfirm => IsQuestion || LibraryStore.NameRefusal(_collections(), Field.Draft, CollectionId) is null;

    public static NameBoxState ForCreate(string returnId, Func<IReadOnlyList<LibraryCollection>> collections) =>
        new(NameBoxKind.Create, null, string.Empty, returnId, collections);

    public static NameBoxState ForRename(string id, string name, string returnId, Func<IReadOnlyList<LibraryCollection>> collections)
    {
        var state = new NameBoxState(NameBoxKind.Rename, id, name, returnId, collections);

        // A name written by hand may break today's rules: the verdict is otherwise only given at a key, and the button would be grey for no reason shown.
        state.Field.Message = state.Check(name).Message;
        return state;
    }

    public static NameBoxState ForDelete(string id, string name, string returnId) =>
        new(NameBoxKind.Delete, id, name, returnId, () => []);

    public static NameBoxState ForStartOver(string returnId) =>
        new(NameBoxKind.StartOver, null, string.Empty, returnId, () => []);

    /// <summary>What the field says of the text, at each key. An empty field says nothing: it has done nothing wrong yet.</summary>
    public FieldCheck Check(string text) => LibraryStore.NameRefusal(_collections(), text, CollectionId) switch
    {
        null => FieldCheck.Fine,
        LibraryOutcome.NameTooLong => FieldCheck.Bad(TooLongText),
        LibraryOutcome.NameTaken => FieldCheck.Bad(TakenText),
        _ => new FieldCheck(false, null),
    };

    /// <summary>What a key means here, before any widget has it. Tab stays in the box by way of <see cref="NameBox.NextStop"/>.</summary>
    /// <param name="vk">The virtual key.</param>
    /// <param name="plain">No Ctrl, Alt or Win is held.</param>
    /// <param name="focusId">The stop with the focus.</param>
    public NameBoxKey Key(uint vk, bool plain, string? focusId)
    {
        if (!plain)
        {
            return NameBoxKey.PassOn;
        }

        // The action is already in the queue of the reads and cannot be taken out: closing now would hide a change that is made anyway.
        if (Waiting && (vk == Vk.Escape || (vk == Vk.Enter && focusId == NameBox.NameId)))
        {
            return NameBoxKey.Ignore;
        }

        return vk switch
        {
            Vk.Escape => NameBoxKey.Cancel,
            Vk.Enter when focusId == NameBox.NameId => NameBoxKey.Confirm,
            _ => NameBoxKey.PassOn,
        };
    }

    /// <summary>The name or the deletion is asked for. True if the action must now leave, false if it may not.</summary>
    public bool TryConfirm()
    {
        if (Waiting || !CanConfirm)
        {
            return false;
        }

        Waiting = true;
        return true;
    }

    /// <summary>
    /// What the action answered. True if the box closes: it is done, or it has to be said elsewhere, as there is nothing the user
    /// can change in the box. A refused name keeps the box open, with the reason under the field.
    /// </summary>
    public bool Answered(LibraryOutcome outcome)
    {
        Waiting = false;
        switch (outcome)
        {
            case LibraryOutcome.NameTaken:
                Field.Message = TakenText;
                return false;
            case LibraryOutcome.NameTooLong:
                Field.Message = TooLongText;
                return false;
            case LibraryOutcome.NameEmpty:
                return false;
            default:
                return true;
        }
    }

    /// <summary>Where the focus goes when the box closes: the stop that opened it, unless the collection and its buttons have gone.</summary>
    /// <param name="outcome">What the action answered. Null if the box was cancelled.</param>
    public string FocusAfter(LibraryOutcome? outcome)
    {
        var collectionGone = (IsQuestion && outcome == LibraryOutcome.Done)
            || (Kind != NameBoxKind.Create && outcome == LibraryOutcome.UnknownCollection);
        return collectionGone ? NameBox.AfterDeleteId : ReturnId;
    }
}

/// <summary>
/// The widgets of the box, on the mechanism of the update card: their ids share a prefix, and while the box is open the screen keeps
/// Tab and the mouse among those. The field is the real one with its focus outline, and the box itself has no glow. The box of a name
/// is the same height in every state, so that a message under the field moves nothing.
/// </summary>
internal static class NameBox
{
    public const string Prefix = "box.", NameId = "box.name", ConfirmId = "box.confirm", CancelId = "box.cancel";

    /// <summary>Where a deletion leaves the focus: its button goes with the collection, and the menu is on All clips.</summary>
    public const string AfterDeleteId = "lib.filter";

    public const float Width = 360, NameHeight = 190, DeleteHeight = 122;

    /// <summary>The name in the question of a deletion. So cut, the sentence never passes two lines, whatever the name and the scale.</summary>
    public const float DeleteNameWidth = 180;

    private const float Inset = 24, ButtonHeight = 34, ButtonFromBottom = 16, ButtonGap = 12, FieldTop = 74, FieldHeight = 32, TextSize = 14, DimOpacity = 0.45f;

    public static Box Frame(NameBoxKind kind)
    {
        var height = kind is NameBoxKind.Delete or NameBoxKind.StartOver ? DeleteHeight : NameHeight;
        return new Box((SettingsScreen.WindowWidth - Width) / 2, (SettingsScreen.WindowHeight - height) / 2, Width, height);
    }

    /// <summary>The widgets, the first of them drawing all that lies behind the box darker: they go last on the page.</summary>
    /// <param name="c">What the buttons are measured with.</param>
    /// <param name="state">What the box remembers.</param>
    /// <param name="confirm">The action leaves: once, whatever the number of presses, see <see cref="NameBoxState.TryConfirm"/>.</param>
    /// <param name="cancel">Cancel was pressed.</param>
    public static IReadOnlyList<Widget> Build(ICanvas c, NameBoxState state, Action confirm, Action cancel)
    {
        var box = Frame(state.Kind);
        var widgets = new List<Widget>(4);
        var isDelete = state.IsQuestion;
        widgets.Add(new Painted((canvas, _) =>
        {
            canvas.Dim(DimOpacity);
            canvas.Frame(box, 6, Theme.Panel, Theme.Control);
            if (isDelete)
            {
                canvas.Wrapped(state.Kind == NameBoxKind.StartOver ? NameBoxState.StartOverQuestion : DeleteQuestion(canvas, state.Name), Face.Body, TextSize, box.X + Inset, box.Y + 22, Width - 2 * Inset, Theme.Text);
            }
            else
            {
                canvas.TextMid(state.Title, Face.Title, 22, box.X + Inset, box.Y + 30, Theme.Text);
                canvas.TextMid("Collection name", Face.Body, 13, box.X + Inset, box.Y + 58, Theme.Muted);
            }
        }));

        if (!isDelete)
        {
            widgets.Add(new TextField(state.Field, state.Check, null, () => state.Name)
            {
                Id = NameId,
                Face = Face.Body,
                Size = TextSize,
                Enabled = !state.Waiting,
                Bounds = new Box(box.X + Inset, box.Y + FieldTop, Width - 2 * Inset, FieldHeight),
            });
        }

        var ready = state.CanConfirm;
        var idle = !state.Waiting;
        var confirmWidth = Button.WidthFor(c, state.ConfirmLabel);
        var cancelWidth = Button.WidthFor(c, "Cancel");
        var top = box.Bottom - ButtonFromBottom - ButtonHeight;

        // In the order of the eye: Tab from the field goes to Cancel, then to the button that acts.
        widgets.Add(new Button("Cancel", () => { if (!state.Waiting) { cancel(); } })
        {
            Id = CancelId,
            Enabled = idle,
            Bounds = new Box(box.Right - Inset - confirmWidth - ButtonGap - cancelWidth, top, cancelWidth, ButtonHeight),
        });
        widgets.Add(new Button(
            state.ConfirmLabel,
            () =>
            {
                if (state.TryConfirm())
                {
                    confirm();
                }
            })
        {
            Id = ConfirmId,
            Accented = ready && !isDelete,
            Enabled = ready,
            Bounds = new Box(box.Right - Inset - confirmWidth, top, confirmWidth, ButtonHeight),
        });
        return widgets;
    }

    /// <summary>The next stop inside the box, whatever else is on the page. Null if none can take the focus.</summary>
    public static string? NextStop(IReadOnlyList<Widget> widgets, string? focusId, bool forward)
    {
        var stops = new List<Widget>(widgets.Count);
        foreach (var widget in widgets)
        {
            if (widget.Id.StartsWith(Prefix, StringComparison.Ordinal))
            {
                stops.Add(widget);
            }
        }

        var next = FocusRing.Next(stops.Count, stops.FindIndex(w => w.Id == focusId), forward, i => stops[i].CanFocus);
        return next < 0 ? null : stops[next].Id;
    }

    public static string DeleteQuestion(ICanvas c, string name) =>
        "Delete collection \"" + c.Fit(name, Face.Body, TextSize, DeleteNameWidth) + "\"? Your clips stay.";
}

/// <summary>
/// Rename and Delete, to the right of the menu, on its line. Only when the menu shows a collection: the other two choices have nothing
/// to rename, and a line of collections that showed them on every chip would have no room left.
/// </summary>
internal static class CollectionActions
{
    public const string RenameId = "lib.rename", DeleteId = "lib.delete";

    private const float Gap = 8, FilterGap = 12, Size = 13, Pad = 12;

    /// <param name="c">What the labels are measured with.</param>
    /// <param name="filter">The menu.</param>
    /// <param name="rename">Open the box for the name.</param>
    /// <param name="delete">Open the box that confirms.</param>
    public static IReadOnlyList<Button> Build(ICanvas c, Box filter, Action rename, Action delete)
    {
        var renameWidth = Button.WidthFor(c, "Rename", Size, Pad);
        var deleteWidth = Button.WidthFor(c, "Delete", Size, Pad);
        var x = filter.Right + FilterGap;
        return
        [
            new Button("Rename", rename) { Id = RenameId, FontSize = Size, Bounds = new Box(x, filter.Y, renameWidth, filter.H) },
            new Button("Delete", delete) { Id = DeleteId, FontSize = Size, Bounds = new Box(x + renameWidth + Gap, filter.Y, deleteWidth, filter.H) },
        ];
    }
}
