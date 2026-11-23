using Hindsound.App;
using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>What the dialog of Save holds while it is open: the name of the copy as typed.</summary>
/// <param name="clip">The clip being edited: the name of the copy is checked by the rule of its renaming.</param>
internal sealed class EditorSaveDialogState(ClipGroup clip)
{
    public const string Prefix = "edit.box.";
    public const string NameId = Prefix + "name", CancelId = Prefix + "cancel", ReplaceId = Prefix + "replace", CopyId = Prefix + "copy";
    public static string Title => Tr.Of("Save your edit", "Enregistrer ta modification");
    public static string NameCaption => Tr.Of("Name of the copy", "Nom de la copie");
    public static string CopyLabel => Tr.Of("Save as a copy", "Enregistrer une copie");
    public static string CopyHelp => Tr.Of("The original stays as it is.", "L'original reste tel quel.");
    public static string ReplaceLabel => Tr.Of("Replace the original", "Remplacer l'original");

    public ClipGroup Clip => clip;

    /// <summary>The words of the system the dialog is shown on.</summary>
    public PlatformFeatures Features { get; init; } = PlatformFeatures.Windows;

    public FieldState Name { get; } = new(ClipNames.EditableName(clip) + EditLimits.CutSuffix);

    /// <summary>Why the copy can't take this name, or null. Another file of that name never counts: the save makes the name unique.</summary>
    public string? Refusal(string typed) => ClipNames.Refusal(clip, typed, _ => false) is { } outcome ? MessageOf(outcome, Features) : null;

    /// <summary>What Save as a copy sends, or null while the name of the copy is refused.</summary>
    public EditSave? Copy() => Refusal(Name.Draft) is null ? new EditSave(EditSaveChoice.Copy, Name.Draft) : null;

    public static string MessageOf(ClipRenameOutcome outcome) => MessageOf(outcome, PlatformFeatures.Windows);

    public static string MessageOf(ClipRenameOutcome outcome, PlatformFeatures features) => outcome switch
    {
        ClipRenameOutcome.Empty => Tr.Of("Type a name.", "Tape un nom."),
        ClipRenameOutcome.BadCharacters => Tr.Of("A name can't hold \\ / : * ? \" < > |", "Un nom ne peut pas contenir \\ / : * ? \" < > |"),
        ClipRenameOutcome.Reserved => features.ReservedNameText + ".",
        ClipRenameOutcome.TrailingDotOrSpace => Tr.Of("A name can't end with a dot or a space.", "Un nom ne peut pas finir par un point ou un espace."),
        ClipRenameOutcome.TooLong => Tr.Of("This name is too long.", "Ce nom est trop long."),
        ClipRenameOutcome.TrackSuffix => ClipNames.TrackSuffixText,
        _ => Tr.Of("Can't use this name.", "Impossible d'utiliser ce nom."),
    };
}

/// <summary>The dialog of Save, drawn over the editor with the window dimmed under it: the name of the copy, then Cancel, Replace the original and Save as a copy.</summary>
internal static class EditorSaveDialog
{
    public const float BoxWidth = 460, BoxHeight = 230, Inset = 24, ButtonHeight = 34, Gap = 12;

    private static string CancelLabel => Tr.Of("Cancel", "Annuler");

    /// <summary>
    /// The box is <see cref="BoxWidth"/> wide, and wider when its three buttons with their gaps and the usual padding need more,
    /// as the French words do on a large scale.
    /// </summary>
    public static Box BoxOf(ICanvas c)
    {
        var buttons = Button.WidthFor(c, EditorSaveDialogState.CopyLabel) + Button.WidthFor(c, EditorSaveDialogState.ReplaceLabel) + Button.WidthFor(c, CancelLabel) + 2 * Gap;
        var width = MathF.Max(BoxWidth, buttons + 2 * Inset);
        return new Box((SettingsScreen.WindowWidth - width) / 2, (SettingsScreen.WindowHeight - BoxHeight) / 2, width, BoxHeight);
    }

    /// <param name="c">What the labels are measured with.</param>
    /// <param name="state">The dialog.</param>
    /// <param name="summary">What is kept, and the volumes.</param>
    /// <param name="copy">Save as a copy, when the name is fine. Enter in the field does it too.</param>
    /// <param name="replace">Replace the original.</param>
    /// <param name="cancel">Back to the editor.</param>
    public static List<Widget> Build(ICanvas c, EditorSaveDialogState state, string summary, Action copy, Action replace, Action cancel)
    {
        var box = BoxOf(c);
        var inner = box.W - 2 * Inset;
        var widgets = new List<Widget>
        {
            new Painted((c, _) =>
            {
                c.Dim(0.45f);
                c.Frame(box, 6, Theme.Panel, Theme.Control);
                c.TextMid(EditorSaveDialogState.Title, Face.Title, 22, box.X + Inset, box.Y + 30, Theme.Text);
                c.TextMid(c.Fit(summary, Face.Body, 13, inner), Face.Body, 13, box.X + Inset, box.Y + 60, Theme.Muted);
                c.TextMid(EditorSaveDialogState.NameCaption, Face.Body, 13, box.X + Inset, box.Y + 90, Theme.Muted);
            }),
            new TextField(state.Name, typed => state.Refusal(typed) is { } refused ? FieldCheck.Bad(refused) : FieldCheck.Fine, null, () => state.Name.Draft)
            {
                Id = EditorSaveDialogState.NameId,
                Face = Face.Body,
                Size = 14,
                Help = EditorSaveDialogState.CopyHelp,
                Bounds = new Box(box.X + Inset, box.Y + 102, inner, 32),
            },
        };

        var copyWidth = Button.WidthFor(c, EditorSaveDialogState.CopyLabel);
        var replaceWidth = Button.WidthFor(c, EditorSaveDialogState.ReplaceLabel);
        var cancelLabel = CancelLabel;
        var cancelWidth = Button.WidthFor(c, cancelLabel);
        var top = box.Bottom - 20 - ButtonHeight;
        var copyLeft = box.Right - Inset - copyWidth;
        var replaceLeft = copyLeft - Gap - replaceWidth;
        widgets.Add(new Button(cancelLabel, cancel) { Id = EditorSaveDialogState.CancelId, Bounds = new Box(replaceLeft - Gap - cancelWidth, top, cancelWidth, ButtonHeight) });
        widgets.Add(new Button(EditorSaveDialogState.ReplaceLabel, replace) { Id = EditorSaveDialogState.ReplaceId, Bounds = new Box(replaceLeft, top, replaceWidth, ButtonHeight) });
        widgets.Add(new Button(EditorSaveDialogState.CopyLabel, copy)
        {
            Id = EditorSaveDialogState.CopyId,
            Accented = true,
            Enabled = state.Copy() is not null,
            Bounds = new Box(copyLeft, top, copyWidth, ButtonHeight),
        });
        return widgets;
    }

    /// <summary>The next stop inside the dialog, whatever else is on the window. Null if none can take the focus.</summary>
    public static string? NextStop(IReadOnlyList<Widget> widgets, string? focusId, bool forward)
    {
        var stops = widgets.Where(w => w.Id.StartsWith(EditorSaveDialogState.Prefix, StringComparison.Ordinal)).ToList();
        var next = FocusRing.Next(stops.Count, stops.FindIndex(w => w.Id == focusId), forward, i => stops[i].CanFocus);
        return next < 0 ? null : stops[next].Id;
    }
}
