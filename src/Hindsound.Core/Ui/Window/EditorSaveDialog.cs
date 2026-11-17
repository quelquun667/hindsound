using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>What the dialog of Save holds while it is open: the choice and the name of the copy as typed.</summary>
/// <param name="clip">The clip being edited: the name of the copy is checked by the rule of its renaming.</param>
internal sealed class EditorSaveDialogState(ClipGroup clip)
{
    public const string Prefix = "edit.box.";
    public const string CopyId = Prefix + "copy", NameId = Prefix + "name", ReplaceId = Prefix + "replace", CancelId = Prefix + "cancel", SaveId = Prefix + "save";
    public const string Title = "Save your edit", NameCaption = "Name of the copy";
    public const string CopyLabel = "Save as a copy", CopyHelp = "The original stays as it is.";
    public const string ReplaceLabel = "Replace the original", ReplaceHelp = "The original goes to the Recycle Bin.";

    public ClipGroup Clip => clip;

    public EditSaveChoice Choice { get; set; } = EditSaveChoice.Copy;

    public FieldState Name { get; } = new(ClipNames.EditableName(clip) + EditLimits.CutSuffix);

    /// <summary>Why the copy can't take this name, or null. Another file of that name never counts: the save makes the name unique.</summary>
    public string? Refusal(string typed) => ClipNames.Refusal(clip, typed, _ => false) is { } outcome ? MessageOf(outcome) : null;

    /// <summary>What the dialog sends, or null while the name of the copy is refused.</summary>
    public EditSave? Save() =>
        Choice == EditSaveChoice.Replace ? new EditSave(EditSaveChoice.Replace, null)
        : Refusal(Name.Draft) is null ? new EditSave(EditSaveChoice.Copy, Name.Draft)
        : null;

    public static string MessageOf(ClipRenameOutcome outcome) => outcome switch
    {
        ClipRenameOutcome.Empty => "Type a name.",
        ClipRenameOutcome.BadCharacters => "A name can't hold \\ / : * ? \" < > |",
        ClipRenameOutcome.Reserved => "Windows keeps this name for itself.",
        ClipRenameOutcome.TrailingDotOrSpace => "A name can't end with a dot or a space.",
        ClipRenameOutcome.TooLong => "This name is too long.",
        _ => "Can't use this name.",
    };
}

/// <summary>One of the two ways of the dialog: a round mark, a name and the sentence under it. Space, Enter or a click picks it.</summary>
internal sealed class EditorChoice(string label, string help, Func<bool> chosen, Action pick) : Widget
{
    public override bool CanFocus => Enabled;

    public bool Chosen => chosen();

    public override bool OnKey(uint vk)
    {
        if (vk is Vk.Space or Vk.Enter)
        {
            pick();
            return true;
        }

        return false;
    }

    public override void OnRelease(bool inside)
    {
        if (inside)
        {
            pick();
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        var mark = new Box(box.X + 2, box.Y + 5, 16, 16);
        var on = chosen();
        c.Ring(mark, 8, 2, on ? ui.Accent : Theme.Control);
        if (on)
        {
            c.FillRound(new Box(mark.X + 4, mark.Y + 4, 8, 8), 4, ui.Accent);
        }

        c.TextMid(label, Face.Body, 14, box.X + 28, mark.MidY, Theme.Text);
        c.TextMid(help, Face.Body, 12, box.X + 28, mark.MidY + 19, Theme.Muted);
        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 4);
        }
    }
}

/// <summary>The dialog of Save, drawn over the editor with the window dimmed under it.</summary>
internal static class EditorSaveDialog
{
    public const float BoxWidth = 460, BoxHeight = 300, Inset = 24, ButtonHeight = 34, Gap = 12, Indent = 28;

    public static Box BoxOf => new((SettingsScreen.WindowWidth - BoxWidth) / 2, (SettingsScreen.WindowHeight - BoxHeight) / 2, BoxWidth, BoxHeight);

    /// <param name="c">What the labels are measured with.</param>
    /// <param name="state">The dialog.</param>
    /// <param name="summary">What is kept, and the volumes.</param>
    /// <param name="pick">A choice clicked or taken by the keyboard.</param>
    /// <param name="save">Save, when the name is fine.</param>
    /// <param name="cancel">Back to the editor.</param>
    public static List<Widget> Build(ICanvas c, EditorSaveDialogState state, string summary, Action<EditSaveChoice> pick, Action save, Action cancel)
    {
        var box = BoxOf;
        var inner = box.W - 2 * Inset;
        var copy = state.Choice == EditSaveChoice.Copy;
        var widgets = new List<Widget>
        {
            new Painted((c, _) =>
            {
                c.Dim(0.45f);
                c.Frame(box, 6, Theme.Panel, Theme.Control);
                c.TextMid(EditorSaveDialogState.Title, Face.Title, 22, box.X + Inset, box.Y + 30, Theme.Text);
                c.TextMid(c.Fit(summary, Face.Body, 13, inner), Face.Body, 13, box.X + Inset, box.Y + 56, Theme.Muted);
                c.TextMid(EditorSaveDialogState.NameCaption, Face.Body, 13, box.X + Inset + Indent, box.Y + 122, copy ? Theme.Muted : Theme.Faded);
            }),
            new EditorChoice(EditorSaveDialogState.CopyLabel, EditorSaveDialogState.CopyHelp, () => state.Choice == EditSaveChoice.Copy, () => pick(EditSaveChoice.Copy))
            {
                Id = EditorSaveDialogState.CopyId,
                Bounds = new Box(box.X + Inset, box.Y + 72, inner, 42),
            },
            new TextField(state.Name, typed => state.Refusal(typed) is { } refused ? FieldCheck.Bad(refused) : FieldCheck.Fine, null, () => state.Name.Draft)
            {
                Id = EditorSaveDialogState.NameId,
                Face = Face.Body,
                Size = 14,
                Enabled = copy,
                Bounds = new Box(box.X + Inset + Indent, box.Y + 134, inner - Indent, 32),
            },
            new EditorChoice(EditorSaveDialogState.ReplaceLabel, EditorSaveDialogState.ReplaceHelp, () => state.Choice == EditSaveChoice.Replace, () => pick(EditSaveChoice.Replace))
            {
                Id = EditorSaveDialogState.ReplaceId,
                Bounds = new Box(box.X + Inset, box.Y + 190, inner, 42),
            },
        };

        var saveWidth = Button.WidthFor(c, "Save");
        var cancelWidth = Button.WidthFor(c, "Cancel");
        var top = box.Bottom - 16 - ButtonHeight;
        var saveLeft = box.Right - Inset - saveWidth;
        widgets.Add(new Button("Cancel", cancel) { Id = EditorSaveDialogState.CancelId, Bounds = new Box(saveLeft - Gap - cancelWidth, top, cancelWidth, ButtonHeight) });
        widgets.Add(new Button("Save", save)
        {
            Id = EditorSaveDialogState.SaveId,
            Accented = true,
            Enabled = state.Save() is not null,
            Bounds = new Box(saveLeft, top, saveWidth, ButtonHeight),
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
