using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hindsound.Input;
using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>The editor of a clip: a page in place of the list of the Library, with Cancel and Save in the foot.</summary>
internal sealed partial class SettingsScreen
{
    public const string EditorTitle = "Edit clip", EditorSoundboardTitle = "Send to soundboard", EditorCancelId = "foot.cancel", EditorSaveId = "foot.save";
    public const string EditorStartId = "edit.start", EditorEndId = "edit.end", EditorPlayId = "edit.play", EditorVolumePrefix = "edit.vol";
    public const string EditorNothingChanges = "Nothing changes until you save", EditorCantRead = "Can't read this clip", EditorReading = "Reading the clip...";
    public const string EditorCantRecycle = "The Recycle Bin can't take the original. Nothing changed.";
    public const string EditorTooBig = "Too big for the soundboard: 512 KB max. Nothing was saved.";
    public const string EditorSourceGone = "The clip is no longer there. Nothing was saved.";
    public const string EditorFailed = "Can't save the edit. The original is as it was.";
    public const string EditorSavedAside = "The original is in the Recycle Bin. The edit was saved as a new clip.";
    public const string EditorAsideLost = "The original is in the Recycle Bin. The edit could not be kept.";
    public const string EditorCantPlay = "Can't play the selection.", EditorSaving = "Saving...";

    private const float EditorRowTop = EditorWave.Top + EditorWave.Height + 24, EditorVolumePitch = 30;
    private static readonly long EditorClockBase = Stopwatch.GetTimestamp();

    private IEditorPanel? _editorPanel;
    private (ClipGroup Clip, EditorMode Mode)? _editing;
    private EditorLoad? _editorLoad;
    private EditorSelection? _selection;
    private double[] _editGains = [];
    private EditorSaveDialogState? _saveDialog;
    private string? _editorReturnId;
    private bool _editSaving;
    private EditorMode _savingMode;
    private string? _editorProblem;
    private PreviewState _editPreview;
    private TimeSpan _playStartedAt;
    private long _playFromMs, _playToMs;
    private bool _editStopAsked;
    private readonly StrongBox<float> _editGrab = new();

    /// <summary>Whether the editor is on the Library page instead of the list.</summary>
    public bool EditorOpen => _editing is not null;

    /// <summary>The clip being edited and what for. Null when the editor is closed.</summary>
    public (ClipGroup Clip, EditorMode Mode)? Editing => _editing;

    /// <summary>The part kept, once the clip is read. Null before, and when it can't be read.</summary>
    internal EditorSelection? EditorSelection => _selection;

    /// <summary>The volume set for each file of the clip, in dB.</summary>
    internal IReadOnlyList<double> EditorGains => _editGains;

    /// <summary>The dialog of Save, while it is open.</summary>
    internal EditorSaveDialogState? EditorDialog => _saveDialog;

    /// <summary>The amber sentence of the foot: the last save or playback that went wrong. Null when all is well.</summary>
    internal string? EditorProblem => _editorProblem;

    /// <summary>The path of the clip chosen in the list, which a save chooses.</summary>
    internal string? EditorChosenPath => ChosenClip?.Main.Path;

    /// <summary>What the play head counts from. A test sets its own.</summary>
    internal Func<TimeSpan> EditorClock { get; set; } = () => Stopwatch.GetElapsedTime(EditorClockBase);

    private bool EditorReady => _editorLoad is { Outcome: EditorLoadOutcome.Loaded } && _selection is not null;

    private bool EditorPlaying => _editPreview is PreviewState.Opening or PreviewState.Playing;

    /// <summary>What the editor asks for now: the part kept and the volumes.</summary>
    internal EditRequest? EditorRequest => _editing is { } editing && _selection is { } selection
        ? new EditRequest(editing.Mode, EditorSelection.Time(selection.StartMs), EditorSelection.Time(selection.EndMs), [.. _editGains])
        : null;

    /// <summary>What the editor reads and writes through. Null, the default, leaves the menu without its entries for the editor.</summary>
    public void SetEditor(IEditorPanel? panel)
    {
        if (_editing is not null)
        {
            _editorPanel?.Close();
            _editing = null;
            ResetEditor();
        }

        _editorPanel = panel;
    }

    /// <summary>Opens the editor on this clip, on the Library page. Without an editor panel, nothing happens.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="mode">What it is edited for.</param>
    /// <param name="returnId">The stop the focus goes back to when the editor closes; the list when null or when it can't take it.</param>
    public void OpenEditor(ClipGroup clip, EditorMode mode, string? returnId = null)
    {
        if (_editorPanel is not { } panel)
        {
            return;
        }

        _library?.StopPlaying();
        ResetEditor();
        _editing = (clip, mode);
        _editorReturnId = returnId;
        _focusId = EditorCancelId;
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
        ResetEditor();
        _editorPanel?.Close();
        _focusId = _editorReturnId ?? ClipListId;
        Refresh();
    }

    private void ResetEditor()
    {
        _editorLoad = null;
        _selection = null;
        _editGains = [];
        _saveDialog = null;
        _editSaving = false;
        _editorProblem = null;
        _editPreview = PreviewState.Stopped;
        _editStopAsked = false;
    }

    /// <summary>The answer to <see cref="IEditorPanel.Load"/>, on the loop.</summary>
    public void EditorLoaded(EditorLoad load)
    {
        if (_editing is not { } editing)
        {
            return;
        }

        _editorLoad = load;
        if (load.Outcome == EditorLoadOutcome.Loaded)
        {
            _selection = new EditorSelection(load.Duration, editing.Mode);
            _editGains = new double[Math.Max(1, load.Peaks.Count)];
            if (_focusId == EditorCancelId)
            {
                _focusId = EditorStartId;
            }
        }

        Refresh();
    }

    /// <summary>What the playback of the selection is doing, on the loop.</summary>
    public void EditorPreviewChanged(PreviewState state)
    {
        var was = _editPreview;
        _editPreview = state;
        if (state == PreviewState.Playing && was != PreviewState.Playing)
        {
            _playStartedAt = EditorClock();
        }

        if (!EditorPlaying)
        {
            _editStopAsked = false;
        }

        if (state == PreviewState.Error)
        {
            _editorProblem = EditorCantPlay;
        }
        else if (_editorProblem == EditorCantPlay && EditorPlaying)
        {
            _editorProblem = null;
        }

        Refresh();
    }

    /// <summary>
    /// The answer to <see cref="IEditorPanel.Save"/>, on the loop. A save the user started is never lost in silence: when the editor
    /// was closed in the meantime, a failure is said on the line of the Library, and a soundboard copy still goes to Discord.
    /// </summary>
    public void EditorSaved(EditSaveResult result)
    {
        var mode = _savingMode;
        var wasOpen = _editing is not null;
        _editSaving = false;
        if (result.Outcome is not (EditSaveOutcome.Saved or EditSaveOutcome.SavedAside))
        {
            var problem = result.Outcome switch
            {
                EditSaveOutcome.CannotRecycle => EditorCantRecycle,
                EditSaveOutcome.TooBig => EditorTooBig,
                EditSaveOutcome.SourceGone => EditorSourceGone,
                _ => EditorFailed,
            };
            if (wasOpen)
            {
                _editorProblem = problem;
            }
            else
            {
                SayOnLine(ChosenClip?.Main.Path, problem);
            }

            Refresh();
            return;
        }

        var path = result.Paths.Count > 0 ? result.Paths[0] : null;
        var forDiscord = result.Outcome == EditSaveOutcome.Saved && mode == EditorMode.Soundboard && path is not null;
        if (forDiscord)
        {
            _editorPanel?.HandToDiscord(path!);
        }

        if (wasOpen)
        {
            _editing = null;
            ResetEditor();
            _editorPanel?.Close();
            _focusId = _editorReturnId ?? ClipListId;
        }

        if (path is null)
        {
            if (result.Outcome == EditSaveOutcome.SavedAside)
            {
                SayOnLine(ChosenClip?.Main.Path, EditorAsideLost);
            }

            Refresh();
            return;
        }

        // The panel sends a new reading after this answer: the copy is chosen as soon as it brings it.
        SelectClip(path);
        var notice = result.Outcome == EditSaveOutcome.SavedAside ? EditorSavedAside : forDiscord ? Soundboard.UploadHint : null;
        if (notice is not null)
        {
            SayOnLine(path, notice);
            if (string.Equals(ChosenClip?.Main.Path, path, PathRules.Comparison))
            {
                Refresh();
            }
        }
    }

    /// <summary>The play head moves: the timer of the wave runs for the editor too, and calls <see cref="EditorTick"/> in place of the Now page.</summary>
    private bool EditorWantsTicks => _editing is not null && EditorPlaying;

    /// <summary>Where the play head is, or null when nothing plays.</summary>
    internal long? EditorPlayheadMs
    {
        get
        {
            if (_editPreview != PreviewState.Playing || _selection is null)
            {
                return null;
            }

            var played = (long)Math.Max(0, (EditorClock() - _playStartedAt).TotalMilliseconds);
            return Math.Min(_playToMs, _playFromMs + played);
        }
    }

    /// <summary>
    /// A tick of the timer of the wave while <see cref="EditorWantsTicks"/>. Off the Library page, the playback stops: nobody sees what
    /// plays. On it, only the wave is drawn again: a full paint takes 34 to 45 ms.
    /// </summary>
    private void EditorTick()
    {
        if (_editorPanel is not { } panel || !EditorWantsTicks)
        {
            return;
        }

        if (Section != Section.Library)
        {
            if (!_editStopAsked)
            {
                _editStopAsked = true;
                panel.StopPlaying();
            }

            return;
        }

        if (PopupOpen || BoxOpen || _saveDialog is not null || !EditorReady)
        {
            return;
        }

        var ui = new UiState(Editor.AccentRgb, Focus, null, null, _mouseX, _mouseY, 0, ShowFocus && !_capture.Listening, null);
        _canvas.SetOrigin(0);
        _canvas.FillRect(EditorWave.Area, Theme.Base);
        EditorWave.Paint(_canvas, ui, CurrentWaveView());
        Find(EditorStartId)?.Paint(_canvas, ui);
        Find(EditorEndId)?.Paint(_canvas, ui);
        _host.InvalidateArea(EditorWave.Area);
    }

    /// <summary>A key while the editor is open, before Tab and the widgets: the arrows with Shift, Escape. Null when this does not want it.</summary>
    private bool? EditorKey(uint vk, uint modifiers)
    {
        if (_editing is null)
        {
            return null;
        }

        const uint shift = HotkeyGesture.ModShift;
        if (_saveDialog is not null)
        {
            // Back from another page, the focus is on the side bar, behind the dialog: the key brings it back into the dialog.
            if (_focusId is null || !_focusId.StartsWith(EditorSaveDialogState.Prefix, StringComparison.Ordinal))
            {
                SetFocus(EditorSaveDialogState.NameId);
                return true;
            }

            if (vk == Vk.Escape && modifiers == 0)
            {
                CloseSaveDialog();
                return true;
            }

            if (vk == Vk.Tab && (modifiers & ~shift) == 0)
            {
                if (EditorSaveDialog.NextStop(_stops, _focusId, (modifiers & shift) == 0) is { } next)
                {
                    SetFocus(next);
                }

                return true;
            }

            if (vk == Vk.Enter && modifiers == 0 && Focus is TextField { Id: EditorSaveDialogState.NameId } field)
            {
                field.Commit();
                SendSaveDialog();
                return true;
            }

            return null;
        }

        if (vk == Vk.Escape && modifiers == 0)
        {
            // A save the user started goes to its end: closing now would hide what it does.
            if (!_editSaving)
            {
                CloseEditor();
            }

            return true;
        }

        if (vk is Vk.Left or Vk.Right && (modifiers & ~shift) == 0 && Focus is EditorHandle handle && _selection is { } selection)
        {
            var step = (modifiers & shift) != 0 ? EditorSelection.BigNudgeMs : EditorSelection.NudgeMs;
            selection.Nudge(handle.End, vk == Vk.Left ? -step : step);
            Refresh();
            return true;
        }

        return null;
    }

    // ---------------------------------------------------------------- what Save does

    private void EditorSaveClicked()
    {
        if (_editing is not { } editing || !EditorReady || _editSaving)
        {
            return;
        }

        if (editing.Mode == EditorMode.Soundboard)
        {
            SendEdit(new EditSave(EditSaveChoice.Copy, ClipNames.EditableName(editing.Clip) + Soundboard.CopySuffix));
            return;
        }

        LeaveFocused();
        _saveDialog = new EditorSaveDialogState(editing.Clip);
        _focusId = EditorSaveDialogState.NameId;
        Refresh();
    }

    private void SendSaveDialog()
    {
        if (_saveDialog is not { } dialog)
        {
            return;
        }

        if (dialog.Copy() is { } save)
        {
            SendEdit(save);
            return;
        }

        dialog.Name.Message = dialog.Refusal(dialog.Name.Draft);
        Refresh();
    }

    private void SendEdit(EditSave save)
    {
        if (_editorPanel is not { } panel || EditorRequest is not { } request)
        {
            return;
        }

        _saveDialog = null;
        _editSaving = true;
        _savingMode = request.Mode;
        _editorProblem = null;
        // Cancel and Save are grey until the answer: the focus waits on Play.
        _focusId = EditorPlayId;
        panel.Save(request, save);
        Refresh();
    }

    private void CloseSaveDialog()
    {
        _saveDialog = null;
        _focusId = EditorSaveId;
        Refresh();
    }

    private void SendReplace()
    {
        if (_saveDialog is not null)
        {
            SendEdit(new EditSave(EditSaveChoice.Replace, null));
        }
    }

    private void EditorPlayOrStop()
    {
        if (_editorPanel is not { } panel)
        {
            return;
        }

        if (EditorPlaying)
        {
            panel.StopPlaying();
            return;
        }

        if (EditorRequest is { } request && _selection is { } selection)
        {
            _playFromMs = selection.StartMs;
            _playToMs = selection.EndMs;
            _editStopAsked = false;
            panel.Play(request);
        }
    }

    private void SetEditGain(int index, double db)
    {
        if (index < _editGains.Length)
        {
            _editGains[index] = Math.Clamp(db, EditLimits.MinGainDb, EditLimits.MaxGainDb);
            Refresh();
        }
    }

    // ---------------------------------------------------------------- texts

    /// <summary>Minutes, seconds and tenths. The tenths are rounded first, so that 59.96 s carries over to 1:00.0.</summary>
    internal static string EditClock(long ms)
    {
        var tenths = (ms + 50) / 100;
        return string.Create(CultureInfo.InvariantCulture, $"{tenths / 600}:{tenths % 600 / 10.0:00.0}");
    }

    internal static string KeptText(long ms) => string.Create(CultureInfo.InvariantCulture, $"{ms / 1000.0:0.0} s");

    internal static string SizeText(long bytes) => bytes < 1_000_000
        ? string.Create(CultureInfo.InvariantCulture, $"about {Math.Max(1, Math.Round(bytes / 1000.0)):0} KB")
        : string.Create(CultureInfo.InvariantCulture, $"about {bytes / 1_000_000.0:0.0} MB");

    internal static string VolumeLabel(int index, int count) => count > 1 ? (index == 0 ? "PC volume" : "Mic volume") : "Volume";

    internal static string HeldText(double ceilingDb) =>
        string.Create(CultureInfo.InvariantCulture, $"Peaks held under {ceilingDb:0.#} dB");

    private string SaveSummary()
    {
        if (_selection is not { } selection)
        {
            return "";
        }

        var summary = $"Kept {EditClock(selection.StartMs)} to {EditClock(selection.EndMs)} ({KeptText(selection.KeptMs)})";
        for (var i = 0; i < _editGains.Length; i++)
        {
            if (Math.Abs(_editGains[i]) >= 0.5)
            {
                var name = _editGains.Length > 1 ? (i == 0 ? "PC" : "mic") : "volume";
                summary += ", " + name + " " + StatusText.GainLabel(_editGains[i]);
            }
        }

        return summary;
    }

    private EditorWaveView CurrentWaveView()
    {
        var selection = _selection!;
        return new EditorWaveView(
            _editorLoad!.Peaks,
            _editGains,
            selection.StartMs,
            selection.EndMs,
            selection.DurationMs,
            EditorPlayheadMs,
            selection.Mode == EditorMode.Normal && selection.TooLongForSoundboard,
            Editor.Settings.Export.PeakCeilingDb);
    }

    private EditorTimeline EditorTimelineNow() =>
        EditorTimeline.Of(EditorWave.Panel, Math.Max(1, _editorLoad?.Peaks.Count ?? 1), _selection?.DurationMs ?? 0, _canvas.Scale);

    // ---------------------------------------------------------------- the page

    /// <summary>A widget of the page, or only its picture while the dialog of Save is over it: nothing under the dialog takes a key or a click.</summary>
    private void EditorWidget(Widget widget)
    {
        if (_saveDialog is null)
        {
            Add(widget, scrolls: false);
        }
        else
        {
            var shown = widget;
            Add(new Painted((c, ui) => shown.Paint(c, ui with { Focus = null, Hover = null, Pressed = null })), scrolls: false);
        }
    }

    private void BuildEditor()
    {
        _contentBottom = 0;
        var name = _editing is { } editing ? ClipNames.EditableName(editing.Clip) : "";
        var title = _editing?.Mode == EditorMode.Soundboard ? EditorSoundboardTitle : EditorTitle;
        Add(new Painted((c, _) =>
        {
            c.TextMid(title, Face.Title, 22, Left, BandHeight + 20, Theme.Text);
            var x = Left + c.Measure(title, Face.Title, 22) + 20;
            c.TextRight(c.Fit(name, Face.Mono, 13, Right - x), Face.Mono, 13, Right, BandHeight + 22, Theme.Muted);
        }), scrolls: false);

        if (_editorLoad is not { } load || load.Outcome != EditorLoadOutcome.Loaded || _selection is not { } selection)
        {
            var text = _editorLoad is null ? EditorReading : EditorCantRead;
            var unreadable = _editorLoad is not null;
            Add(new Painted((c, _) =>
            {
                var panel = EditorWave.Panel;
                c.Frame(panel, 4, Theme.Base, Theme.Line);
                if (unreadable)
                {
                    Message.Error(c, text, panel.X + 16, panel.MidY);
                }
                else
                {
                    c.TextMid(text, Face.Body, 14, panel.X + 16, panel.MidY, Theme.Muted);
                }
            }), scrolls: false);
            return;
        }

        var mode = _editing!.Value.Mode;
        Add(new Painted((c, ui) => EditorWave.Paint(c, ui, CurrentWaveView())), scrolls: false);
        foreach (var end in new[] { EditorEnd.Start, EditorEnd.End })
        {
            var at = end;
            EditorWidget(new EditorHandle(at, EditorTimelineNow, () => selection.At(at), ms => MoveHandle(at, ms), _editGrab)
            {
                Id = at == EditorEnd.Start ? EditorStartId : EditorEndId,
                Bounds = EditorHandle.HitBox(at, EditorTimelineNow().XOf(selection.At(at))),
            });
        }

        // The times, and the button that plays the part kept.
        var playing = EditorPlaying;
        var playWidth = MathF.Max(Button.WidthFor(_canvas, "Play", icon: true), Button.WidthFor(_canvas, "Stop"));
        EditorWidget(new Button(playing ? "Stop" : "Play", EditorPlayOrStop)
        {
            Id = EditorPlayId,
            Icon = playing ? ButtonIcon.None : ButtonIcon.Play,
            Bounds = new Box(Left, EditorRowTop + 2, playWidth, 34),
        });
        var after = Left + playWidth + 32;
        Add(new Painted((c, ui) =>
        {
            var size = _editorPanel is { } panel && EditorRequest is { } request ? SizeText(panel.EstimateBytes(request)) : "";
            var tooLong = mode == EditorMode.Normal && selection.TooLongForSoundboard;
            var cells = new (string Caption, string Value, float Width, uint Color)[]
            {
                ("Start", EditClock(selection.StartMs), 92, Theme.Text),
                ("End", EditClock(selection.EndMs), 92, Theme.Text),
                ("Kept", KeptText(selection.KeptMs), 92, tooLong ? Theme.Amber : ui.Accent),
                ("Size", size, 140, Theme.Muted),
            };
            var x = after;
            foreach (var cell in cells)
            {
                c.TextMid(cell.Caption, Face.Body, 12, x, EditorRowTop + 7, Theme.Muted);
                c.TextMid(cell.Value, Face.Mono, 15, x, EditorRowTop + 27, cell.Color);
                x += cell.Width;
            }
        }), scrolls: false);

        var y = EditorRowTop + 44;
        for (var i = 0; i < _editGains.Length; i++)
        {
            EditorVolumeRow(i, y);
            y += EditorVolumePitch;
        }

        var lineTop = y + 8;
        Add(new Painted((c, ui) => PaintSoundboardLine(c, ui, lineTop, mode == EditorMode.Soundboard || !selection.TooLongForSoundboard)), scrolls: false);
    }

    private void MoveHandle(EditorEnd end, long ms)
    {
        _selection?.Move(end, ms);
        _host.Invalidate();
    }

    private void EditorVolumeRow(int index, float y)
    {
        var label = VolumeLabel(index, _editGains.Length);
        var db = _editGains[index];
        Add(new Painted((c, _) => c.TextMid(label, Face.Body, 14, Left, y + RowHeight / 2, Theme.Text)), scrolls: false);
        var bounds = new Box(Right - 300, y + 5, 300, 20);
        EditorWidget(new Slider(EditLimits.MinGainDb, EditLimits.MaxGainDb, EditLimits.GainStepDb, () => _editGains[index], v => SetEditGain(index, v), () => { }, StatusText.GainLabel(db))
        {
            Id = EditorVolumePrefix + index,
            Bounds = bounds,
        });
        var ceiling = Editor.Settings.Export.PeakCeilingDb;
        Add(new Painted((c, _) =>
        {
            // The tick of 0 dB, under the track. The thumb runs over 16 units less than the track, 8 at each side.
            var track = bounds.W - 60 - 16;
            var x = bounds.X + 8 + (float)(-EditLimits.MinGainDb / (EditLimits.MaxGainDb - EditLimits.MinGainDb)) * track;
            c.FillRect(new Box(x - 0.5f, y + 25, 1, 5), Theme.Control);
            if (db > 0)
            {
                c.TextMid(HeldText(ceiling), Face.Body, 12, Left + c.Measure(label, Face.Body, 14) + 18, y + 15, Theme.Muted);
            }
        }), scrolls: false);
    }

    /// <summary>Whether the part kept fits the Discord soundboard: a statement with a mark drawn, not a box to tick.</summary>
    private static void PaintSoundboardLine(ICanvas c, UiState ui, float y, bool fits)
    {
        var text = fits ? Soundboard.FitsText : Soundboard.TooLongText;
        var width = MathF.Ceiling(c.Measure(text, Face.Body, 13)) + 44;
        var box = new Box(Left, y, width, 26);
        c.Frame(box, 4, Theme.Panel, fits ? Theme.Mix(ui.Accent, Theme.Base, 0.55f) : Theme.Mix(Theme.Amber, Theme.Base, 0.6f));
        if (fits)
        {
            c.Stroke(box.X + 11, box.MidY, box.X + 15, box.MidY + 4, 2, ui.Accent);
            c.Stroke(box.X + 15, box.MidY + 4, box.X + 22, box.MidY - 5, 2, ui.Accent);
        }
        else
        {
            c.WarningIcon(box.X + 10, box.MidY - 7, 14, Theme.Amber);
        }

        c.TextMid(text, Face.Body, 13, box.X + 32, box.MidY, fits ? Theme.Text : Theme.Amber);
    }

    private void BuildEditorFoot()
    {
        var top = WindowHeight - FootHeight;
        var problem = _editorProblem;
        var saving = _editSaving;
        Chrome(new Painted((c, _) =>
        {
            c.FillRect(new Box(0, top, WindowWidth, FootHeight), Theme.Panel);
            c.FillRect(new Box(0, top, WindowWidth, 1), Theme.Line);
            if (problem is not null)
            {
                Message.Error(c, problem, FootMargin, top + FootHeight / 2);
            }
            else
            {
                c.TextMid(saving ? EditorSaving : EditorNothingChanges, Face.Body, 13, FootMargin, top + FootHeight / 2, Theme.Muted);
            }
        }));

        var saveWidth = Button.WidthFor(_canvas, "Save");
        var cancelWidth = Button.WidthFor(_canvas, "Cancel");
        var buttonTop = top + (FootHeight - FootButtonHeight) / 2;
        var cancel = new Button("Cancel", CloseEditor)
        {
            Id = EditorCancelId,
            Enabled = !_editSaving,
            Bounds = new Box(WindowWidth - FootMargin - saveWidth - FootButtonGap - cancelWidth, buttonTop, cancelWidth, FootButtonHeight),
        };
        var save = new Button("Save", EditorSaveClicked)
        {
            Id = EditorSaveId,
            Accented = EditorReady,
            Enabled = EditorReady && !_editSaving,
            Bounds = new Box(WindowWidth - FootMargin - saveWidth, buttonTop, saveWidth, FootButtonHeight),
        };
        if (_saveDialog is not { } dialog)
        {
            Chrome(cancel);
            Chrome(save);
            return;
        }

        Chrome(new Painted((c, ui) =>
        {
            var still = ui with { Focus = null, Hover = null, Pressed = null };
            cancel.Paint(c, still);
            save.Paint(c, still);
        }));

        // The dialog, last of all so that it dims what is under it; the band and the side bar only to be seen.
        foreach (var widget in EditorSaveDialog.Build(_canvas, dialog, SaveSummary(), SendSaveDialog, SendReplace, CloseSaveDialog))
        {
            Chrome(widget);
        }

        var box = EditorSaveDialog.BoxOf;
        _chrome.Insert(0, new EditorShield { Id = "edit.shield.band", Bounds = new Box(0, 0, WindowWidth, BandHeight) });
        _chrome.Insert(1, new EditorShield { Id = "edit.shield.side", Bounds = new Box(0, BandHeight, box.X, WindowHeight - BandHeight) });
    }
}
