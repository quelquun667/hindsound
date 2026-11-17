using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>The editor of a clip: a page in place of the list of the Library, with Cancel and Save in the foot.</summary>
internal sealed partial class SettingsScreen
{
    public const string EditorTitle = "Edit clip", EditorCancelId = "foot.cancel", EditorSaveId = "foot.save";

    private IEditorPanel? _editorPanel;
    private (ClipGroup Clip, EditorMode Mode)? _editing;

    /// <summary>Whether the editor is on the Library page instead of the list.</summary>
    public bool EditorOpen => _editing is not null;

    /// <summary>The clip being edited and what for. Null when the editor is closed.</summary>
    public (ClipGroup Clip, EditorMode Mode)? Editing => _editing;

    /// <summary>What the editor reads and writes through. Null, the default, leaves the menu without its entries for the editor.</summary>
    public void SetEditor(IEditorPanel? panel)
    {
        if (_editing is not null)
        {
            _editorPanel?.Close();
            _editing = null;
        }

        _editorPanel = panel;
    }

    /// <summary>Opens the editor on this clip, on the Library page. Without an editor panel, nothing happens.</summary>
    public void OpenEditor(ClipGroup clip, EditorMode mode)
    {
        if (_editorPanel is not { } panel)
        {
            return;
        }

        _library?.StopPlaying();
        _editing = (clip, mode);
        panel.Load(clip);
        if (Section == Section.Library)
        {
            Refresh();
        }
        else
        {
            ShowSection(Section.Library);
        }
    }

    /// <summary>Leaves the editor for the list, with nothing saved.</summary>
    public void CloseEditor()
    {
        if (_editing is null)
        {
            return;
        }

        _editing = null;
        _editorPanel?.Close();
        Refresh();
    }

    /// <summary>The answer to <see cref="IEditorPanel.Load"/>, on the loop.</summary>
    public void EditorLoaded(EditorLoad load)
    {
    }

    /// <summary>What the playback of the selection is doing, on the loop.</summary>
    public void EditorPreviewChanged(PreviewState state)
    {
    }

    /// <summary>The answer to <see cref="IEditorPanel.Save"/>, on the loop.</summary>
    public void EditorSaved(EditSaveResult result)
    {
    }

    /// <summary>A key while the editor is open, before Tab and the widgets: the arrows with Shift, Escape. Null when this does not want it.</summary>
    private bool? EditorKey(uint vk, uint modifiers) => null;

    private void BuildEditor()
    {
        var name = _editing is { } editing ? ClipNames.EditableName(editing.Clip) : "";
        Add(new Painted((c, _) =>
        {
            c.TextMid(EditorTitle, Face.Title, 18, NavWidth + 32, BandHeight + 24, Theme.Text);
            c.TextMid(name, Face.Mono, 12, NavWidth + 200, BandHeight + 24, Theme.Muted);
        }), scrolls: false);
    }

    private void BuildEditorFoot()
    {
        var top = WindowHeight - FootHeight;
        Chrome(new Painted((c, _) =>
        {
            c.FillRect(new Box(0, top, WindowWidth, FootHeight), Theme.Panel);
            c.FillRect(new Box(0, top, WindowWidth, 1), Theme.Line);
        }));

        var saveWidth = Button.WidthFor(_canvas, "Save");
        var cancelWidth = Button.WidthFor(_canvas, "Cancel");
        var buttonTop = top + (FootHeight - FootButtonHeight) / 2;
        Chrome(new Button("Cancel", CloseEditor)
        {
            Id = EditorCancelId,
            Bounds = new Box(WindowWidth - FootMargin - saveWidth - FootButtonGap - cancelWidth, buttonTop, cancelWidth, FootButtonHeight),
        });
        Chrome(new Button("Save", () => { })
        {
            Id = EditorSaveId,
            Enabled = false,
            Bounds = new Box(WindowWidth - FootMargin - saveWidth, buttonTop, saveWidth, FootButtonHeight),
        });
    }
}
