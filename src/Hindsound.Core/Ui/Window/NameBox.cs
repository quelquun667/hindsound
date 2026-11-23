using Hindsound.App;
using Hindsound.Library;

namespace Hindsound.Ui.Window;

internal enum NameBoxKind
{
    Create,
    Rename,
    Delete,

    /// <summary>A damaged file of collections is put aside for an empty one.</summary>
    StartOver,

    /// <summary>A clip of the list takes a new name: its files, their suffixes and extensions kept.</summary>
    RenameClip,

    /// <summary>A clip of the list goes to the Recycle Bin, once confirmed.</summary>
    DeleteClip,
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
/// The small box of a collection or of a clip, and all it has to remember, as its widgets are made again at each key: create, rename,
/// or confirm a deletion. Nothing here reaches the file. It says what is asked, and the answer of the action comes back to
/// <see cref="Answered"/>, or to <see cref="AnsweredClip"/> for a clip.
/// </summary>
internal sealed class NameBoxState
{
    // The English of the sentences below stays a constant, which tests of the English read; the *Shown ones are what the screen
    // draws, in the language of the moment.
    public const string TakenText = "That name is already used", TooLongText = "40 characters at most";
    public static string TakenShown => Tr.Of(TakenText, "Ce nom est déjà utilisé");
    public static string TooLongShown => Tr.Of(TooLongText, "40 caractères au maximum");
    public static string StartOverQuestion => Tr.Of("Start over with no collections? Your clips stay.", "Recommencer sans collection\u00A0? Tes clips restent.");

    public const string ClipEmptyText = "The name can't be empty", ClipReservedText = "Windows keeps this name for itself";
    public const string ClipTrailingText = "A name can't end with a dot or a space", ClipTooLongText = "This name is too long";
    public const string ClipTakenText = "Another clip has this name";
    public static string ClipEmptyShown => Tr.Of(ClipEmptyText, "Le nom ne peut pas être vide");
    public static string ClipTrailingShown => Tr.Of(ClipTrailingText, "Un nom ne peut pas finir par un point ou un espace");
    public static string ClipTooLongShown => Tr.Of(ClipTooLongText, "Ce nom est trop long");
    public static string ClipTakenShown => Tr.Of(ClipTakenText, "Un autre clip porte ce nom");

    // The Windows wording, as the others of the box: Features.Words puts the word of the system in. The French word is the same on
    // every system.
    public const string CannotRecycleText = "The Recycle Bin can't take this clip. It stays where it is.";
    public static string CannotRecycleShown => Tr.Of(CannotRecycleText, "La Corbeille ne peut pas prendre ce clip. Il reste où il est.");
    public static string TwoFilesText => TwoFilesTextOf("PC");

    /// <summary>The same sentence for the computer of this system: the French names it ("le son du Mac"), the English does not.</summary>
    public static string TwoFilesTextOf(string machine) => Tr.Format(
        $"This clip is 2 files, the system sound and the microphone. Both files go to the Recycle Bin.",
        $"Ce clip fait 2 fichiers\u00A0: le son du {machine} et le micro. Les deux fichiers vont dans la Corbeille.");

    /// <summary>The words of the system the box is shown on: the sentences above are the Windows ones.</summary>
    public PlatformFeatures Features { get; set; } = PlatformFeatures.Windows;

    private readonly Func<IReadOnlyList<LibraryCollection>> _collections;
    private Func<string, ClipRenameOutcome?> _clipRefusal = _ => null;

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
        Field = new FieldState(kind is NameBoxKind.Rename or NameBoxKind.RenameClip ? name : string.Empty);
    }

    public NameBoxKind Kind { get; }

    public string? CollectionId { get; }

    /// <summary>The clip to rename or delete. Null in the box of a collection.</summary>
    public ClipGroup? Clip { get; private init; }

    /// <summary>The box is about a clip, not a collection.</summary>
    public bool IsClip => Kind is NameBoxKind.RenameClip or NameBoxKind.DeleteClip;

    public string Name { get; }

    public string ReturnId { get; }

    /// <summary>The text as typed, even when it cannot be kept.</summary>
    public FieldState Field { get; }

    /// <summary>The action has left and has not answered: a second press would make it twice.</summary>
    public bool Waiting { get; private set; }

    public string Title => Kind switch
    {
        NameBoxKind.Rename => Tr.Of("Rename collection", "Renommer la collection"),
        NameBoxKind.RenameClip => Tr.Of("Rename clip", "Renommer le clip"),
        _ => Tr.Of("New collection", "Nouvelle collection"),
    };

    /// <summary>The words over the field.</summary>
    public string FieldLabel => Kind == NameBoxKind.RenameClip ? Tr.Of("Clip name", "Nom du clip") : Tr.Of("Collection name", "Nom de la collection");

    public string ConfirmLabel => Kind switch
    {
        NameBoxKind.Create => Tr.Of("Create", "Créer"),
        NameBoxKind.Rename => Tr.Of("Rename", "Renommer"),
        NameBoxKind.StartOver => SettingsScreen.StartOverLabel,
        NameBoxKind.RenameClip => Tr.Of("Rename", "Renommer"),
        _ => Tr.Of("Delete", "Supprimer"),
    };

    /// <summary>The first control of the box: the name, or Cancel for a deletion, so that Enter on arrival deletes nothing.</summary>
    public string EntryId => IsQuestion ? NameBox.CancelId : NameBox.NameId;

    /// <summary>A question with no name to type: a deletion or a fresh start.</summary>
    public bool IsQuestion => Kind is NameBoxKind.Delete or NameBoxKind.StartOver or NameBoxKind.DeleteClip;

    public bool CanConfirm => IsQuestion
        || (Kind == NameBoxKind.RenameClip ? _clipRefusal(Field.Draft) is null : LibraryStore.NameRefusal(_collections(), Field.Draft, CollectionId) is null);

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

    /// <summary>The box that renames a clip, its field holding the name as the list shows it, without its extension.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="returnId">The stop that opened the box: the focus goes back there.</param>
    /// <param name="refusal">Why a name can't be given, asked again at each key: see <see cref="ClipNames.Refusal"/>.</param>
    public static NameBoxState ForClipRename(ClipGroup clip, string returnId, Func<string, ClipRenameOutcome?> refusal)
    {
        var state = new NameBoxState(NameBoxKind.RenameClip, null, ClipNames.EditableName(clip), returnId, () => [])
        {
            Clip = clip,
        };
        state._clipRefusal = refusal;

        // A name given by hand outside the app may break the rules: the reason is shown at once, as the button is grey.
        state.Field.Message = state.Check(state.Name).Message;
        return state;
    }

    /// <summary>The box that asks before a clip goes to the Recycle Bin. Its name is the one the list shows, with its extension.</summary>
    public static NameBoxState ForClipDelete(ClipGroup clip, string returnId) =>
        new(NameBoxKind.DeleteClip, null, ListRow.NameOf(clip), returnId, () => []) { Clip = clip };

    /// <summary>The sentence of a name a clip can't take. Null for an outcome that is no reason of the name.</summary>
    public static string? ClipRefusalText(ClipRenameOutcome outcome) => ClipRefusalText(outcome, PlatformFeatures.Windows);

    /// <summary>The sentence of a name a clip can't take, in the words of <paramref name="features"/>.</summary>
    public static string? ClipRefusalText(ClipRenameOutcome outcome, PlatformFeatures features) => outcome switch
    {
        ClipRenameOutcome.Empty => ClipEmptyShown,
        ClipRenameOutcome.BadCharacters => SettingsEditor.TemplateCharsText,
        ClipRenameOutcome.Reserved => features.ReservedNameText,
        ClipRenameOutcome.TrailingDotOrSpace => ClipTrailingShown,
        ClipRenameOutcome.TooLong => ClipTooLongShown,
        ClipRenameOutcome.Taken => ClipTakenShown,
        ClipRenameOutcome.TrackSuffix => ClipNames.TrackSuffixText,
        _ => null,
    };

    /// <summary>What the field says of the text, at each key. An empty collection name says nothing: it has done nothing wrong yet.</summary>
    public FieldCheck Check(string text) => Kind == NameBoxKind.RenameClip ? CheckClip(text) : LibraryStore.NameRefusal(_collections(), text, CollectionId) switch
    {
        null => FieldCheck.Fine,
        LibraryOutcome.NameTooLong => FieldCheck.Bad(TooLongShown),
        LibraryOutcome.NameTaken => FieldCheck.Bad(TakenShown),
        _ => new FieldCheck(false, null),
    };

    /// <summary>The name of a clip, at each key. Unlike a collection, an empty name says why: the field was full when the box opened.</summary>
    private FieldCheck CheckClip(string text) =>
        _clipRefusal(text) is { } refusal ? FieldCheck.Bad(ClipRefusalText(refusal, Features) ?? ClipEmptyShown) : FieldCheck.Fine;

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
                Field.Message = TakenShown;
                return false;
            case LibraryOutcome.NameTooLong:
                Field.Message = TooLongShown;
                return false;
            case LibraryOutcome.NameEmpty:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// What the renaming of a clip answered. True if the box closes; a name the disk refused keeps it open, with the reason under
    /// the field.
    /// </summary>
    public bool AnsweredClip(ClipRenameOutcome outcome)
    {
        Waiting = false;
        if (ClipRefusalText(outcome, Features) is not { } text)
        {
            return true;
        }

        Field.Message = text;
        return false;
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

    private const float ExtensionGap = 10, Inset = 24, ButtonHeight = 34, ButtonFromBottom = 16, ButtonGap = 12, FieldTop = 74, FieldHeight = 32, TextSize = 14, DimOpacity = 0.45f;

    /// <summary>The box of a clip: wider, for a file name.</summary>
    public const float ClipWidth = 440, ClipDeleteHeight = 138, ClipDeleteTwoFilesHeight = 168;

    /// <summary>The name of a clip in the question of a deletion: never more than a line.</summary>
    public const float ClipNameWidth = 340;

    /// <summary>Where the box of this state lies.</summary>
    public static Box Frame(NameBoxState state)
    {
        if (!state.IsClip)
        {
            return Frame(state.Kind);
        }

        var height = state.Kind == NameBoxKind.RenameClip ? NameHeight : state.Clip!.Files.Count > 1 ? ClipDeleteTwoFilesHeight : ClipDeleteHeight;
        return new Box((SettingsScreen.WindowWidth - ClipWidth) / 2, (SettingsScreen.WindowHeight - height) / 2, ClipWidth, height);
    }

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
        var box = Frame(state);
        var widgets = new List<Widget>(4);
        var isDelete = state.IsQuestion;
        var inner = box.W - 2 * Inset;

        // The extension of a clip is not part of the name typed: it stands in grey at the right of the field, out of the native box's way.
        var extension = state.Kind == NameBoxKind.RenameClip ? Path.GetExtension(state.Clip!.Main.Path) : string.Empty;
        var extensionWidth = extension.Length > 0 ? MathF.Ceiling(c.Measure(extension, Face.Body, TextSize)) + ExtensionGap : 0;
        var field = new Box(box.X + Inset, box.Y + FieldTop, inner, FieldHeight);
        widgets.Add(new Painted((canvas, _) =>
        {
            canvas.Dim(DimOpacity);
            canvas.Frame(box, 6, Theme.Panel, Theme.Control);
            if (state.Kind == NameBoxKind.DeleteClip)
            {
                var used = canvas.Wrapped(state.Features.Words(ClipDeleteQuestion(canvas, state.Name)), Face.Body, TextSize, box.X + Inset, box.Y + 22, inner, Theme.Text);
                if (state.Clip!.Files.Count > 1)
                {
                    canvas.Wrapped(state.Features.Words(NameBoxState.TwoFilesTextOf(state.Features.MachineName)), Face.Body, 13, box.X + Inset, box.Y + 22 + used + 6, inner, Theme.Muted);
                }
            }
            else if (isDelete)
            {
                canvas.Wrapped(state.Kind == NameBoxKind.StartOver ? NameBoxState.StartOverQuestion : DeleteQuestion(canvas, state.Name), Face.Body, TextSize, box.X + Inset, box.Y + 22, inner, Theme.Text);
            }
            else
            {
                canvas.TextMid(state.Title, Face.Title, 22, box.X + Inset, box.Y + 30, Theme.Text);
                canvas.TextMid(state.FieldLabel, Face.Body, 13, box.X + Inset, box.Y + 58, Theme.Muted);
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
                Suffix = extension.Length > 0 ? extension : null,
                SuffixWidth = extensionWidth,
                Bounds = field,
            });
        }

        var ready = state.CanConfirm;
        var idle = !state.Waiting;
        var confirmWidth = Button.WidthFor(c, state.ConfirmLabel);
        var cancelWidth = Button.WidthFor(c, Tr.Of("Cancel", "Annuler"));
        var top = box.Bottom - ButtonFromBottom - ButtonHeight;

        // In the order of the eye: Tab from the field goes to Cancel, then to the button that acts.
        widgets.Add(new Button(Tr.Of("Cancel", "Annuler"), () => { if (!state.Waiting) { cancel(); } })
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
        FitQuestion(c.Fit(name, Face.Body, TextSize, DeleteNameWidth));

    private static string FitQuestion(string name) => Tr.Format(
        $"Delete collection \"{name}\"? Your clips stay.",
        $"Supprimer la collection «\u00A0{name}\u00A0»\u00A0? Tes clips restent.");

    /// <summary>The question before a clip goes to the Recycle Bin, its name with its extension.</summary>
    public static string ClipDeleteQuestion(ICanvas c, string name) =>
        Tr.Format($"Move \"{c.Fit(name, Face.Body, TextSize, ClipNameWidth)}\" to the Recycle Bin?", $"Déplacer «\u00A0{c.Fit(name, Face.Body, TextSize, ClipNameWidth)}\u00A0» vers la Corbeille\u00A0?");
}

/// <summary>
/// The "..." button of the line of the menu, with the room the search field leaves for it. It stands only when the menu shows a
/// collection: the other two choices have nothing to rename, and an empty collection still needs a way to be deleted. It opens
/// <see cref="CollectionMenu"/>.
/// </summary>
internal static class CollectionActions
{
    public const string MoreId = "lib.collection.more", MoreLabel = "...";

    public const float MoreWidth = 44, Gap = 12;

    /// <summary>The button, to the right of the menu, the height of it.</summary>
    public static Box MoreBox(Box filter) => new(filter.Right + Gap, filter.Y, MoreWidth, filter.H);

    /// <summary>The left edge of the search field: after the menu and the room of the button, whether the button is shown or not.</summary>
    public static float SearchLeft(Box filter) => MoreBox(filter).Right + Gap;

    /// <param name="filter">The menu.</param>
    /// <param name="open">Open the menu of the collection.</param>
    public static Button Build(Box filter, Action open) => new(MoreLabel, open) { Id = MoreId, Bounds = MoreBox(filter) };
}
