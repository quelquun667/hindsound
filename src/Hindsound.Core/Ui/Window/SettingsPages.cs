using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Updates;

namespace Hindsound.Ui.Window;

/// <summary>What is on each page, top to bottom. The sizes are those of the design mockup.</summary>
internal sealed partial class SettingsScreen
{
    public static string LegalNote => Tr.Of(
        "Recording a conversation without telling the people in it can be illegal. Let them know before you keep it.",
        "Enregistrer une conversation sans prévenir les personnes présentes peut être illégal. Préviens-les avant de la garder.");
    public static string ShorterBufferNote => Tr.Of("A shorter buffer drops the oldest audio", "Une mémoire plus courte supprime l'audio le plus ancien");
    public static string SoundHelp => Tr.Of(
        "The beep can be heard in your next clip if you capture a specific output.",
        "Le bip peut s'entendre dans ton prochain clip si tu captures une sortie précise.");

    public static string GitHubNote => Tr.Of(
        "Once a day, Hindsound asks GitHub for the latest version. GitHub sees your IP address and the time, nothing else.",
        "Une fois par jour, Hindsound demande la dernière version à GitHub. GitHub voit ton adresse IP et l'heure, rien d'autre.");
    public static string FirstRunUpdatesNote => Tr.Of(
        "Hindsound checks GitHub for updates once a day. GitHub sees your IP address. Change it in Settings > General.",
        "Hindsound vérifie les mises à jour sur GitHub une fois par jour. GitHub voit ton adresse IP. Change ça dans Réglages > Général.");
    public const string ClipBusyNote = "A clip is still being saved";

    public static string ClipBusyNoteText => Tr.Of(ClipBusyNote, "Un clip s'enregistre encore");
    public static string DesktopKeysNote => Tr.Of(
        "Your desktop sets these keys. To change one, use the shortcut settings of your desktop.",
        "Ton bureau définit ces raccourcis. Pour en modifier un, utilise ses réglages de raccourcis clavier.");
    public static string CommandIntro => Tr.Of(
        "This desktop has no global shortcut for apps. In its keyboard settings, bind this command to a key:",
        "Ce bureau n'a pas de raccourci global pour les applications. Dans ses réglages du clavier, associe cette commande à une touche\u00A0:");
    public static string CommandNote => Tr.Of("The key then saves a clip, as the Save button does.", "La touche enregistre alors un clip, comme le bouton Enregistrer.");
    public static string CommandGenericIntro => Tr.Of(
        "This desktop has no global shortcut for apps. In its keyboard settings, bind the full path of the Hindsound program followed by --save to a key.",
        "Ce bureau n'a pas de raccourci global pour les applications. Dans ses réglages du clavier, associe à une touche le chemin complet du programme Hindsound suivi de --save.");
    private static string CopyLabel => Tr.Of("Copy", "Copier");
    private static string CopiedLabel => Tr.Of("Copied", "Copié");
    public const string FirstRunUpdatesId = "run.updates";
    private const string CardPrefix = "card.";

    internal const float Left = 200, Right = 728, Width = Right - Left, RowHeight = 30, FieldWidth = 220;
    private const string HexFieldId = "general.hex", TemplateFieldId = "export.template", ClipFieldId = "durations.custom", SwatchId = "general.accent";

    private readonly FieldState _clipField;
    private readonly FieldState _hexField;
    private readonly FieldState _templateField;
    private bool _customClipOpen;
    private bool _customAccentOpen;
    private bool _commandCopied;
    private string? _folderMessage;
    private int _firstRunStep;
    private bool _firstRunClipDone;
    private IUpdatesPanel? _updates;
    private float? _updatesTop;
    private string? _cardReturnFocus;

    /// <summary>
    /// The language the system gives, which the first item of the Language list names. A parameter so a picture or a test does
    /// not depend on the languages of the PC it runs on.
    /// </summary>
    public Func<UiLanguage> SystemLanguage { get; set; } = static () => UiLanguages.FromSystem(PlatformDefaults.SystemLanguages?.Invoke() ?? []) ?? UiLanguage.English;

    /// <summary>When the name preview is taken. A parameter so a picture or a test does not depend on the hour.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    private string ClipFieldText() => NotificationStates.FormatDuration(Editor.Settings.ClipSeconds);

    // ---------------------------------------------------------------- what surrounds every page

    private T Chrome<T>(T widget)
        where T : Widget
    {
        _chrome.Add(widget);
        if (widget is not Painted)
        {
            _stops.Add(widget);
        }

        return widget;
    }

    private T Add<T>(T widget, bool scrolls = true)
        where T : Widget
    {
        widget.Scrolls = scrolls;
        _page.Add(widget);
        if (widget is not Painted)
        {
            _stops.Add(widget);
        }

        return widget;
    }

    private void BuildBand()
    {
        var band = StatusText.From(_status, Editor.Settings.BufferSeconds);
        var saveLabel = StatusText.SaveLabel(Editor.Settings.ClipSeconds);
        var capture = Features.Shortcuts() == ShortcutMode.Capture;

        // Where the desktop binds the keys, the button names the key it chose, and none at all when it did not.
        var saveGesture = (capture ? Editor.Settings.Hotkeys.SaveDefault : ShownKey(HotkeyAction.SaveDefault)) ?? string.Empty;
        var saveBox = SaveBox(_canvas, saveLabel, saveGesture);
        var pauseWidth = Button.WidthFor(_canvas, StatusText.ResumeLabel, icon: true);
        var pauseLeft = BandPauseLeft(_canvas, saveLabel, saveGesture);
        Chrome(new Painted((c, ui) => PaintBand(c, ui, band, pauseLeft)) { Bounds = new Box(0, 0, WindowWidth, BandHeight) });
        Chrome(new Button(band.PauseLabel, _backend.TogglePause)
        {
            Id = "band.pause",
            Icon = band.PauseLabel == StatusText.ResumeLabel ? ButtonIcon.Play : ButtonIcon.Pause,
            Enabled = band.PauseEnabled,
            Bounds = new Box(pauseLeft, 18, pauseWidth, 36),
        });
        Chrome(new Button(saveLabel, _backend.SaveNow)
        {
            Id = "band.save",
            Accented = true,
            SubLabel = capture || saveGesture.Length > 0 ? saveGesture : null,
            FontSize = 16,
            Enabled = band.SaveEnabled,
            Bounds = saveBox,
        });
    }

    private static void PaintBand(ICanvas c, UiState ui, StatusBand band, float pauseLeft)
    {
        c.FillRect(new Box(0, 0, WindowWidth, BandHeight), Theme.Panel);
        c.FillRect(new Box(0, 0, WindowWidth, 1), Theme.Line);
        c.FillRect(new Box(0, BandHeight - 1, WindowWidth, 1), Theme.Line);
        c.Glow(new Box(0, 0, 4, BandHeight), 4, 0.5f, ui.Accent);
        c.FillRect(new Box(0, 0, 4, BandHeight), ui.Accent);

        const float mid = BandHeight / 2;
        var x = 28f;
        switch (band.Mark)
        {
            case StatusMark.Dot:
                c.FillRound(new Box(x, mid - 5, 10, 10), 5, Theme.Hot);
                x += 20;
                break;
            case StatusMark.Pause:
                c.FillRound(new Box(x, mid - 6, 3.5f, 12), 0.8f, Theme.Muted);
                c.FillRound(new Box(x + 6.5f, mid - 6, 3.5f, 12), 0.8f, Theme.Muted);
                x += 20;
                break;
            case StatusMark.Warning:
                c.WarningIcon(x, mid - 8, 16, Theme.Hot);
                x += 22;
                break;
            case StatusMark.Caution:
                c.WarningIcon(x, mid - 8, 16, Theme.Amber);
                x += 22;
                break;
        }

        var (title, gap) = BandTitle(c, band, pauseLeft, x);
        c.TextMid(title, Face.Title, 22, x, mid, Theme.Text);
        x += c.Measure(title, Face.Title, 22) + gap;
        if (band.Mic.Length > 0)
        {
            c.TextMid(band.Mic, Face.Body, 14, x, mid, Theme.Muted);
            x += c.Measure(band.Mic, Face.Body, 14) + gap;
            c.TextMid(band.Duration, Face.Mono, 13, x, mid, Theme.Text);
            x += c.Measure(band.Duration, Face.Mono, 13) + gap;
            c.TextMid(band.Ram, Face.Mono, 13, x, mid, Theme.Muted);
        }
    }

    /// <summary>Where the Pause button starts: the width of the save button decides it.</summary>
    internal static float BandPauseLeft(ICanvas c, string saveLabel, string saveGesture) =>
        SaveBox(c, saveLabel, saveGesture).X - 20 - Button.WidthFor(c, StatusText.ResumeLabel, icon: true);

    private static Box SaveBox(ICanvas c, string saveLabel, string saveGesture)
    {
        var width = MathF.Ceiling(Math.Max(c.Measure(saveLabel, Face.Medium, 16), c.Measure(saveGesture, Face.Mono, 11)) + 36);
        return new Box(WindowWidth - 24 - width, 10, width, 52);
    }

    /// <summary>
    /// The title as it fits, and the space between the figures. An exe name can be of any length, and the figures and the
    /// buttons come after it. They close up before the title gets cut: a long memory and the longest word for the microphone
    /// would otherwise cut even "Capturing".
    /// </summary>
    /// <param name="c">What measures.</param>
    /// <param name="band">The words of the band.</param>
    /// <param name="pauseLeft">Where the Pause button starts.</param>
    /// <param name="x">Where the title starts.</param>
    internal static (string Title, float Gap) BandTitle(ICanvas c, StatusBand band, float pauseLeft, float x)
    {
        const float wide = 20, tight = 12;
        float Room(float gap) => pauseLeft - gap - x - (band.Mic.Length == 0 ? 0
            : c.Measure(band.Mic, Face.Body, 14) + gap + c.Measure(band.Duration, Face.Mono, 13) + gap + c.Measure(band.Ram, Face.Mono, 13) + gap);

        var chosen = c.Measure(band.Title, Face.Title, 22) <= Room(wide) ? wide : tight;
        return (c.Fit(band.Title, Face.Title, 22, Room(chosen)), chosen);
    }

    private void BuildNav()
    {
        Chrome(new Painted((c, _) =>
        {
            c.FillRect(new Box(0, BandHeight, NavWidth, ViewBottom - BandHeight), Theme.Panel);
            c.FillRect(new Box(NavWidth - 1, BandHeight, 1, ViewBottom - BandHeight), Theme.Line);
        }));
        var rows = Rows;
        Chrome(new NavList(rows, () => rows.IndexOf(Section), i => ShowSection(rows.Rows[i].Section))
        {
            Id = NavId,
            Bounds = new Box(12, BandHeight + 16, NavWidth - 25, rows.Height),
        });
    }

    private void BuildFoot()
    {
        if (Section == Section.Library)
        {
            if (EditorOpen)
            {
                BuildEditorFoot();
                return;
            }

            BuildLibraryFoot();
            return;
        }

        var top = WindowHeight - FootHeight;
        var clip = _lastClip;
        Chrome(new Painted((c, _) =>
        {
            c.FillRect(new Box(0, top, WindowWidth, FootHeight), Theme.Panel);
            c.FillRect(new Box(0, top, WindowWidth, 1), Theme.Line);
            var mid = top + FootHeight / 2;
            var lastClip = Tr.Of("Last clip", "Dernier clip");
            c.TextMid(lastClip, Face.Title, 18, 24, mid, Theme.Muted);
            var x = 24 + c.Measure(lastClip, Face.Title, 18) + 16;
            if (clip is null)
            {
                c.TextMid(Tr.Of("None yet", "Aucun pour l'instant"), Face.Body, 13, x, mid, Theme.Muted);
                return;
            }

            c.TextMid(clip.DurationText, Face.Mono, 13, x, mid, Theme.Text);
            x += c.Measure(clip.DurationText, Face.Mono, 13) + 16;
            c.TextMid(clip.TimeText, Face.Mono, 13, x, mid, Theme.Muted);
            x += c.Measure(clip.TimeText, Face.Mono, 13) + 16;
            c.TextMid(clip.SizeText, Face.Mono, 13, x, mid, Theme.Muted);
        }));

        var open = Tr.Of("Open folder", "Ouvrir le dossier");
        var copy = Tr.Of("Copy", "Copier");
        var openWidth = Button.WidthFor(_canvas, open);
        var copyWidth = Button.WidthFor(_canvas, copy);
        var buttonTop = top + (FootHeight - 34) / 2;
        Chrome(new Button(copy, _backend.CopyLast)
        {
            Id = "foot.copy",
            Enabled = clip is not null,
            Bounds = new Box(WindowWidth - 24 - openWidth - 16 - copyWidth, buttonTop, copyWidth, 34),
        });
        Chrome(new Button(open, _backend.OpenFolder)
        {
            Id = "foot.open",
            Bounds = new Box(WindowWidth - 24 - openWidth, buttonTop, openWidth, 34),
        });
    }

    private void BuildPage()
    {
        switch (Section)
        {
            case Section.Hotkeys:
                BuildHotkeys();
                break;
            case Section.Durations:
                BuildDurations();
                break;
            case Section.Audio:
                BuildAudio();
                break;
            case Section.Export:
                BuildExport();
                break;
            case Section.General:
                BuildGeneral();
                break;
            case Section.UpdateCard:
                BuildUpdateCard();
                break;
            case Section.Now:
                BuildNow();
                break;
            case Section.Library:
                if (EditorOpen)
                {
                    BuildEditor();
                    break;
                }

                BuildLibrary();
                break;
            default:
                BuildFirstRun();
                break;
        }
    }

    // ---------------------------------------------------------------- pieces of a page

    private void Heading(string text, float y, float x = Left) =>
        Add(new Painted((c, _) => c.Text(text, Face.Title, 22, x, y, Theme.Text)));

    private void RowLabel(string text, float y, float height = RowHeight, float x = Left) =>
        Add(new Painted((c, _) => c.TextMid(text, Face.Body, 14, x, y + height / 2, Theme.Text)));

    private void Note(string text, float y, float x = Left, uint color = Theme.Muted, Face face = Face.Body, float size = 13) =>
        Add(new Painted((c, _) => c.TextMid(text, face, size, x, y, color)));

    private Segmented Group(string id, IReadOnlyList<string> labels, Box box, Func<int> selected, Action<int> pick, bool enabled = true) =>
        Add(new Segmented(labels, selected, pick) { Id = id, Bounds = box, Enabled = enabled });

    private Dropdown List(string id, Box box, Func<IReadOnlyList<DropItem>> items, Func<string> selected, Action<string> pick, bool enabled = true) =>
        Add(new Dropdown(items, selected, pick, OpenPopup) { Id = id, Bounds = box, Enabled = enabled });

    private void Row(string label, string id, float y, Func<IReadOnlyList<DropItem>> items, Func<string> selected, Action<string> pick, bool enabled = true)
    {
        RowLabel(label, y);
        List(id, new Box(Right - FieldWidth, y, FieldWidth, RowHeight), items, selected, pick, enabled);
    }

    private void ToggleRow(string label, string id, float y, Func<bool> on, Action<bool> set)
    {
        RowLabel(label, y);
        Add(new Toggle(on, set) { Id = id, Bounds = new Box(Right - 40, y + 4, 40, 22) });
    }

    private void SegmentRow(string label, string id, float y, IReadOnlyList<string> labels, Func<int> selected, Action<int> pick, bool enabled = true)
    {
        RowLabel(label, y);
        Group(id, labels, new Box(Right - FieldWidth, y, FieldWidth, RowHeight), selected, pick, enabled);
    }

    private Slider SliderRow(string id, Box box, double min, double max, double step, double stored, Func<double, double> clamp, Action<double> keep, Func<double, string> text, bool enabled = true)
    {
        var value = SliderValue(id, stored);
        return Add(new Slider(min, max, step, () => SliderValue(id, stored), v => SliderPreview(id, clamp(v), keep), KeepSlider, text(value))
        {
            Id = id,
            Bounds = box,
            Enabled = enabled,
        });
    }

    // ---------------------------------------------------------------- hotkeys

    /// <summary>The ids of what only the desktop's own shortcuts show, so that a test reads them by name.</summary>
    internal const string HotkeyCommandId = "hotkey.command", HotkeyCopyId = "hotkey.copycommand", HotkeyNoteId = "hotkey.note", HotkeyShownPrefix = "hotkey.shown.";

    /// <summary>The key the desktop bound for an action, in its words. Null while it has not answered, or when it bound none.</summary>
    private string? ShownKey(HotkeyAction action) =>
        _hotkeyResults.FirstOrDefault(r => r.Action == action) is { Status: HotkeyStatus.Registered, Gesture: { Length: > 0 } text } ? text : null;

    /// <summary>What a desktop that binds the keys itself shows in place of the field of an action.</summary>
    private string ShownKeyText(HotkeyAction action)
    {
        if (ShownKey(action) is { } text)
        {
            return text;
        }

        return _hotkeyResults.FirstOrDefault(r => r.Action == action)?.Status switch
        {
            null or HotkeyStatus.Suspended => Tr.Of("Waiting for your desktop", "En attente du bureau"),
            HotkeyStatus.NotSet => Tr.Of("Not set", "Aucun"),
            _ => Tr.Of("Not bound by your desktop", "Non associé par le bureau"),
        };
    }

    /// <summary>
    /// The rows of the Hotkeys page. "Save and edit" has one only where there is an editor to open, as the menu of a clip shows
    /// Edit only then.
    /// </summary>
    private IEnumerable<(HotkeyAction Action, string Label)> HotkeyRowsShown =>
        _editorPanel is null ? SettingsEditor.HotkeyRows.Where(row => row.Action != HotkeyAction.SaveAndEdit) : SettingsEditor.HotkeyRows;

    private void BuildHotkeys()
    {
        switch (Features.Shortcuts())
        {
            case ShortcutMode.Portal:
                BuildDesktopHotkeys();
                return;
            case ShortcutMode.Command:
                BuildCommandHotkeys();
                return;
        }

        const float pitch = 37, fieldWidth = 280, fieldHeight = 32, resetHeight = 30;
        var y = BandHeight + 18;
        Heading(Tr.Of("Hotkeys", "Raccourcis"), y);

        // On the line of the title, at its right end, as the name of the color is on the General page: the seven rows then fit
        // under it, and the button is there with no scrolling. Added after the fields, it comes last in the order of Tab.
        var titleMid = y + 13;
        var resetTop = titleMid - resetHeight / 2;
        y += 36;
        foreach (var (action, label) in HotkeyRowsShown)
        {
            RowLabel(label, y, fieldHeight);
            var view = ViewOf(action);
            Add(new HotkeyField(action, () => ViewOf(action), BeginCapture)
            {
                Id = "hotkey." + action,
                Bounds = new Box(Right - fieldWidth, y, fieldWidth, fieldHeight),
            });
            y += pitch + (view.Message is not null || view.Listening ? HotkeyField.MessageHeight : 0);
        }

        var reset = Tr.Of("Reset to defaults", "Réinitialiser");
        var width = Button.WidthFor(_canvas, reset);
        Add(new Button(reset, ResetHotkeys) { Id = "hotkey.reset", Bounds = new Box(Right - width, resetTop, width, resetHeight) });
        _contentBottom = y - (pitch - fieldHeight);
    }

    /// <summary>
    /// The Hotkeys page where the desktop binds the keys: each action shows the key the desktop gave it, in the desktop's words,
    /// and there is nothing to type in. The desktop is where the keys change.
    /// </summary>
    private void BuildDesktopHotkeys()
    {
        const float pitch = 37, fieldWidth = 280, fieldHeight = 32;
        var y = BandHeight + 18;
        Heading(Tr.Of("Hotkeys", "Raccourcis"), y);
        y += 36;
        foreach (var (action, label) in HotkeyRowsShown)
        {
            RowLabel(label, y, fieldHeight);
            var box = new Box(Right - fieldWidth, y, fieldWidth, fieldHeight);
            var bound = ShownKey(action);
            var text = ShownKeyText(action);
            var asKeys = bound is not null && HotkeyGesture.TryParse(bound, out _);
            Add(new Painted((c, _) =>
            {
                c.Frame(box, 4, Theme.Raised, Theme.Control);
                if (asKeys)
                {
                    c.Keycaps(Features.KeyText(bound!), box.X + 8, box.MidY);
                }
                else
                {
                    c.TextMid(c.Fit(text, Face.Body, 13, box.W - 16), Face.Body, 13, box.X + 8, box.MidY, bound is null ? Theme.Muted : Theme.Text);
                }
            })
            {
                Id = HotkeyShownPrefix + action,
                Bounds = box,
            });
            y += pitch;
        }

        y += 6;
        var noteTop = y;
        Add(new Painted((c, _) => c.Wrapped(DesktopKeysNote, Face.Body, 13, Left, noteTop, Width, Theme.Muted)) { Id = HotkeyNoteId });
        _contentBottom = y + WrapLines(_canvas, DesktopKeysNote, Face.Body, 13, Width).Count * _canvas.LineHeight(Face.Body, 13);
    }

    /// <summary>
    /// The Hotkeys page where nothing can grab a key: the command a key of the desktop's own keyboard settings must run, and the
    /// button that copies it.
    /// </summary>
    private void BuildCommandHotkeys()
    {
        var command = Features.SaveCommand();
        var y = BandHeight + 18;
        Heading(Tr.Of("Hotkeys", "Raccourcis"), y);
        y += 36;
        var introTop = y;
        // A program whose path cannot be written as a command (the notice falls back to words too): the words alone, nothing
        // to copy. A bare "hindsound --save" would be wrong where nothing put the program on the PATH, as with an AppImage.
        var intro = command is null ? CommandGenericIntro : CommandIntro;
        Add(new Painted((c, _) => c.Wrapped(intro, Face.Body, 14, Left, introTop, Width, Theme.Text)) { Id = HotkeyNoteId });
        y += WrapLines(_canvas, intro, Face.Body, 14, Width).Count * _canvas.LineHeight(Face.Body, 14) + 14;
        if (command is null)
        {
            var genericNoteTop = y;
            Add(new Painted((c, _) => c.Wrapped(CommandNote, Face.Body, 13, Left, genericNoteTop, Width, Theme.Muted)));
            _contentBottom = y + WrapLines(_canvas, CommandNote, Face.Body, 13, Width).Count * _canvas.LineHeight(Face.Body, 13);
            return;
        }

        var copyLabel = _commandCopied ? CopiedLabel : CopyLabel;
        var copyWidth = CopyButtonWidth();
        var box = new Box(Left, y, Width - copyWidth - 12, 34);
        Add(new Painted((c, _) =>
        {
            c.Frame(box, 4, Theme.Raised, Theme.Control);
            c.TextMid(c.Fit(command, Face.Mono, 13, box.W - 20), Face.Mono, 13, box.X + 10, box.MidY, Theme.Text);
        })
        {
            Id = HotkeyCommandId,
            Bounds = box,
        });
        Add(new Button(copyLabel, CopyCommand) { Id = HotkeyCopyId, Bounds = new Box(Right - copyWidth, y, copyWidth, 34) });
        y += 34 + 14;
        var noteTop = y;
        Add(new Painted((c, _) => c.Wrapped(CommandNote, Face.Body, 13, Left, noteTop, Width, Theme.Muted)));
        _contentBottom = y + WrapLines(_canvas, CommandNote, Face.Body, 13, Width).Count * _canvas.LineHeight(Face.Body, 13);
    }

    /// <summary>The room of the Copy button, whichever of its two words it shows, so that it does not move when it is pressed.</summary>
    private float CopyButtonWidth() => Math.Max(Button.WidthFor(_canvas, CopiedLabel), Button.WidthFor(_canvas, CopyLabel));

    private void CopyCommand()
    {
        if (Features.SaveCommand() is not { } command)
        {
            return;
        }

        _host.CopyText(command);
        _commandCopied = true;
        Refresh();
    }

    // ---------------------------------------------------------------- durations

    private void BuildDurations()
    {
        var settings = Editor.Settings;
        var y = BandHeight + 22;
        Heading(Tr.Of("Clip length", "Durée du clip"), y);
        y += 38;
        Group(
            "durations.clip",
            SettingsEditor.ClipLabels,
            new Box(Left, y, Width, 36),
            () => _customClipOpen ? SettingsEditor.CustomClip : Editor.ClipSegment,
            PickClip);
        y += 36;
        if (_customClipOpen)
        {
            y += 10;
            RowLabel(Tr.Of("Custom length", "Autre durée"), y);
            Add(new TextField(_clipField, SettingsEditor.CheckCustomClip, Editor.CommitCustomClip, ClipFieldText)
            {
                Id = ClipFieldId,
                Bounds = new Box(Left + 120, y, 100, RowHeight),
                Help = SettingsEditor.CustomClipHelp,
            });
            y += RowHeight + TextField.MessageHeight;
        }

        y += 28;
        Heading(Tr.Of("Keep in memory", "Garder en mémoire"), y);
        y += 38;
        var max = Editor.BufferSliderMax;
        var shown = (int)SliderValue("durations.keep", settings.BufferSeconds);
        Add(new Slider(
            SettingsEditor.BufferSliderMin,
            max,
            SettingsEditor.BufferSliderStep,
            () => SliderValue("durations.keep", settings.BufferSeconds),
            v => SliderPreview("durations.keep", Editor.ClampBuffer((int)v), keep => Editor.SetBufferSeconds((int)keep)),
            KeepSlider,
            NotificationStates.FormatDuration(shown))
        {
            Id = "durations.keep",
            Bounds = new Box(Left, y, Width, 20),
        });
        y += 30;
        var ram = Editor.RamText(shown);
        Note(ram, y + 8, face: Face.Mono, color: Theme.Text);
        y += 20;
        Note(ShorterBufferNote, y + 8);
        y += 20;
        if (shown <= Editor.BufferFloor && Editor.BufferFloor > SettingsEditor.BufferSliderMin)
        {
            var longest = NotificationStates.FormatDuration(Editor.BufferFloor);
            var floor = Tr.Format($"At least {longest}: your longest clip", $"Au moins {longest}\u00A0: ton clip le plus long");
            Note(floor, y + 8, color: Theme.Amber);
            y += 20;
        }

        _contentBottom = y;
    }

    private void PickClip(int index)
    {
        if (index == SettingsEditor.CustomClip)
        {
            _customClipOpen = true;
            _clipField.Draft = ClipFieldText();
            _clipField.Message = null;
            Refresh();
            FocusFieldIfClicked(ClipFieldId);
            return;
        }

        _customClipOpen = false;
        _clipField.Message = null;
        Editor.SetClipSeconds(SettingsEditor.ClipPresets[index]);
        Refresh();
    }

    // ---------------------------------------------------------------- audio

    private void BuildAudio()
    {
        const float pitch = 38;
        var audio = Editor.Settings.Audio;
        var y = BandHeight + 18;
        Heading(Tr.Of("Audio", "Audio"), y);
        y += 36;
        if (Features.SingleApp)
        {
            Row(Tr.Of("Record", "Capturer"), "audio.record", y, () => SettingsEditor.RecordChoices(_apps, Editor.Settings.Audio.SourceApp, _status is { SourceApp: not null, SourceAppMissing: false }, Features.MachineName), () => Editor.Settings.Audio.SourceApp ?? string.Empty, Editor.SetSourceApp);
            y += pitch;
        }

        // One application is captured wherever it plays, so the output has no say. Where one application cannot be chosen, a name
        // left in the file by hand changes nothing, and the output stays the choice.
        Row(Tr.Of("Output", "Sortie"), "audio.output", y, () => SettingsEditor.OutputChoices(_outputs, Editor.Settings.Audio.OutputDeviceId), () => Editor.Settings.Audio.OutputDeviceId, Editor.SetOutput, audio.SourceApp is null || !Features.SingleApp);
        y += pitch;
        Row(Tr.Of("Microphone", "Micro"), "audio.mic", y, () => SettingsEditor.MicChoices(_mics, Editor.Settings.Audio.MicDeviceId), () => Editor.Settings.Audio.MicDeviceId, Editor.SetMic);
        y += pitch;
        RowLabel(Tr.Of("Mic level", "Niveau du micro"), y);
        Add(new MeterBar(() => _meter.Level, MeterCaption) { Bounds = new Box(Right - FieldWidth, y + 9, FieldWidth, 12) });
        y += pitch;
        SegmentRow(Tr.Of("Tracks", "Pistes"), "audio.mode", y, [Tr.Of("Mix", "Mixées"), Tr.Of("Separate", "Séparées")], () => Editor.Settings.Audio.Mode == "mix" ? 0 : 1, i => Editor.SetMode(i == 0 ? "mix" : "multitrack"));
        y += pitch;
        GainRow(Tr.Format($"{Features.MachineName} sound gain", $"Gain du son du {Features.MachineName}"), "audio.systemGain", y, audio.SystemGainDb, Editor.SetSystemGain, true);
        y += pitch;
        GainRow(Tr.Of("Mic gain", "Gain du micro"), "audio.micGain", y, audio.MicGainDb, Editor.SetMicGain, audio.MicCaptured);
        _contentBottom = y + RowHeight;
    }

    private void GainRow(string label, string id, float y, double stored, Action<double> keep, bool enabled)
    {
        const double range = 24;
        RowLabel(label, y);
        SliderRow(id, new Box(Right - 300, y + 5, 300, 20), -range, range, 1, stored, v => Math.Clamp(v, AudioSettings.MinGainDb, AudioSettings.MaxGainDb), keep, StatusText.GainLabel, enabled);
    }

    /// <summary>The bar only moves on what the capture already holds: a mic that is off stays closed.</summary>
    private string? MeterCaption()
    {
        if (!Editor.Settings.Audio.MicCaptured)
        {
            return Tr.Of("The microphone is off", "Le micro est coupé");
        }

        if (_status is null)
        {
            return Tr.Of("Starting", "Démarrage");
        }

        return _status.MicCapturing ? null : _status.Activity == Audio.CaptureActivity.Paused ? Tr.Of("Capture is paused", "Capture en pause") : Tr.Of("No microphone found", "Aucun micro trouvé");
    }

    // ---------------------------------------------------------------- export

    private void BuildExport()
    {
        const float pitch = 38;
        var export = Editor.Settings.Export;
        var y = BandHeight + 18;
        Heading(Tr.Of("Export", "Export"), y);
        y += 36;
        SegmentRow(Tr.Of("Format", "Format"), "export.format", y, ["WAV", "MP3"], () => Editor.Settings.Export.Format == "wav" ? 0 : 1, i => Editor.SetFormat(i == 0 ? "wav" : "mp3"));
        y += pitch;
        SegmentRow(
            Tr.Of("MP3 quality", "Qualité MP3"),
            "export.bitrate",
            y,
            ["128", "192", "320"],
            () => Math.Max(0, Array.IndexOf(SettingsEditor.Bitrates, Editor.Settings.Export.Mp3Bitrate)),
            i => Editor.SetBitrate(SettingsEditor.Bitrates[i]),
            export.Format != "wav");
        y += pitch;
        SegmentRow(
            Tr.Of("Volume", "Volume"),
            "export.normalization",
            y,
            [Tr.Of("Off", "Inchangé"), Tr.Of("Peak", "Pic"), Tr.Of("Loudness", "Loudness")],
            () => Editor.Settings.Export.Normalization switch { "off" => 0, "peak" => 1, _ => 2 },
            i => Editor.SetNormalization(i switch { 0 => "off", 1 => "peak", _ => "loudness" }));
        y += pitch;

        RowLabel(Tr.Of("Folder", "Dossier"), y);
        var browseLabel = Tr.Of("Browse", "Parcourir");
        var browse = Button.WidthFor(_canvas, browseLabel, 13, 12);
        Add(new Button(browseLabel, PickFolder) { Id = "export.browse", FontSize = 13, Bounds = new Box(Right - browse, y, browse, RowHeight) });
        var folder = Editor.FolderShown;
        var folderBox = new Box(Left + 60, y, Width - 60 - browse - 8, RowHeight);
        var folderError = _folderMessage;
        Add(new Painted((c, _) =>
        {
            c.Frame(folderBox, 4, Theme.Panel, Theme.Line);
            c.TextMid(c.Fit(folder, Face.Body, 13, folderBox.W - 16), Face.Body, 13, folderBox.X + 8, folderBox.MidY, Theme.Muted);
            if (folderError is not null)
            {
                Message.Error(c, folderError, folderBox.X, folderBox.Bottom + 10);
            }
        }));
        y += pitch + (folderError is null ? 0 : TextField.MessageHeight);

        RowLabel(Tr.Of("File name", "Nom du fichier"), y);
        var nameBox = new Box(Left + 80, y, Width - 80, RowHeight);
        Add(new TextField(_templateField, SettingsEditor.CheckTemplate, t => Editor.SetTemplate(t), () => Editor.Settings.Export.FileNameTemplate)
        {
            Id = TemplateFieldId,
            Bounds = nameBox,
        });
        var preview = _templateField.Message is null ? Editor.NamePreview(_templateField.Draft, Clock()) : null;
        if (preview is not null)
        {
            Add(new Painted((c, _) => c.TextMid(c.Fit(preview, Face.Mono, 12, nameBox.W), Face.Mono, 12, nameBox.X, nameBox.Bottom + 11, Theme.Muted)));
        }

        y += RowHeight + TextField.MessageHeight + 8;
        ToggleRow(Tr.Of("Copy clip to clipboard", "Copier le clip dans le presse-papiers"), "export.copy", y, () => Editor.Settings.Export.CopyToClipboard, Editor.SetCopyToClipboard);
        _contentBottom = y + RowHeight;
    }

    // ---------------------------------------------------------------- general

    private void BuildGeneral()
    {
        const float pitch = 36;
        _updatesTop = null;
        var general = Editor.Settings.General;
        var y = BandHeight + 18;
        Heading(Tr.Of("Accent color", "Couleur d'accent"), y);
        var top = y;
        var name = Editor.AccentName;
        var hex = Editor.AccentHex;
        Add(new Painted((c, _) =>
        {
            var hexWidth = c.Measure(hex, Face.Mono, 13);
            c.TextRight(hex, Face.Mono, 13, Right, top + 15, Theme.Text);
            c.TextRight(name, Face.Body, 13, Right - hexWidth - 8, top + 15, Theme.Muted);
        }));
        y += 38;

        var order = SettingsEditor.AccentOrder;
        var colors = order.Select(p => { AccentColors.TryParseHex(AccentColors.Presets[p], out var r, out var g, out var b); return ((uint)r << 16) | ((uint)g << 8) | b; }).ToArray();
        var names = order.Select(SettingsEditor.AccentWord).Append(SettingsEditor.AccentWord(AccentPreset.Custom)).ToArray();
        Add(new SwatchGroup(colors, names, SwatchSelected, PickSwatch)
        {
            Id = SwatchId,
            Bounds = new Box(Left + 4, y, Width - 8, 30),
        });
        y += 44;

        if (_customAccentOpen)
        {
            RowLabel(Tr.Of("Color code", "Code couleur"), y);
            Add(new TextField(_hexField, SettingsEditor.CheckAccent, t => Editor.SetAccentHex(t), () => Editor.AccentHex)
            {
                Id = HexFieldId,
                Bounds = new Box(Left + 100, y, 110, RowHeight),
                Help = Tr.Of("Any bright color works.", "Toute couleur vive convient."),
            });
            y += RowHeight + TextField.MessageHeight + 6;
        }

        // The notification of the desktop has no look of ours to preview.
        if (Features.NotificationLook)
        {
            var accent = Editor.AccentRgb;
            var previewTop = y;
            var leftEdge = general.NotificationPosition == "left";
            Add(new Painted((c, _) => c.Notification(SampleClip.Notification(accent), Left, previewTop, accent, leftEdge)));
            y += 64 + 14;
        }

        var divider = y - 7;
        Add(new Painted((c, _) => c.FillRect(new Box(Left, divider, Width, 1), Theme.Line)));
        y += 4;

        ToggleRow(Features.StartupLabel, "general.startup", y, () => Editor.Settings.General.StartWithWindows, Editor.SetStartWithWindows);
        y += pitch;
        var languages = LanguageChoices();
        Row(Tr.Of("Language", "Langue"), "general.language", y, () => languages, () => Editor.Settings.General.Language, Editor.SetLanguage);
        y += pitch;
        SegmentRow(Tr.Of("Window size", "Taille de la fenêtre"), "general.size", y, GeneralSettings.WindowZooms.Select(zoom => Tr.Format($"{zoom}%", $"{zoom}\u00A0%")).ToList(), () => Math.Max(0, Array.IndexOf(GeneralSettings.WindowZooms, Editor.Settings.General.WindowZoom)), i => Editor.SetWindowZoom(GeneralSettings.WindowZooms[i]));
        y += pitch;
        // The choice stays lit when the screen cannot hold it; this says why the window is smaller.
        if (_host.AppliedZoom is > 0 and var applied && applied < Editor.Settings.General.WindowZoom)
        {
            Note(Tr.Format($"This screen fits up to {applied}%", $"Cet écran permet jusqu'à {applied}\u00A0%"), y - pitch + RowHeight + 10);
            y += 18;
        }

        ToggleRow(Tr.Of("Save notifications", "Notifications d'enregistrement"), "general.notifications", y, () => Editor.Settings.General.Notifications, Editor.SetNotifications);
        y += pitch;
        if (Features.NotificationLook)
        {
            SegmentRow(Tr.Of("Notification position", "Position des notifications"), "general.position", y, [Tr.Of("Right edge", "Bord droit"), Tr.Of("Left edge", "Bord gauche")], () => Editor.Settings.General.NotificationPosition == "left" ? 1 : 0, i => Editor.SetNotificationPosition(i == 1 ? "left" : "right"));
            y += pitch;
        }

        Row(Tr.Of("In fullscreen games", "Jeux en plein écran"), "general.fullscreen", y, () => FullscreenChoices, () => Editor.Settings.General.FullscreenGames, Editor.SetFullscreenGames);
        if (Features.ConfirmSound)
        {
            y += pitch;
            Row(Tr.Of("Confirmation sound", "Son de confirmation"), "general.sound", y, () => SoundChoices, () => Editor.Settings.General.ConfirmSound, Editor.SetConfirmSound);
            y += RowHeight + 12;
            Note(SoundHelp, y);
            y += 20;
        }
        else
        {
            y += RowHeight + 12;
        }

        if (Features.TestNotification)
        {
            var test = Tr.Of("Show test notification", "Afficher une notification de test");
            var testWidth = Button.WidthFor(_canvas, test);
            Add(new Button(test, _backend.ShowTestNotification) { Id = "general.test", Bounds = new Box(Left, y, testWidth, 34) });
            y += 34 + 14;
        }
        if (_updates is { } updates)
        {
            y = BuildUpdates(updates.View, y);
        }

        var noteTop = y;
        Add(new Painted((c, _) => c.Wrapped(LegalNote, Face.Body, 13, Left, noteTop, Width, Theme.Muted)));
        y += 2 * _canvas.LineHeight(Face.Body, 13);
        _contentBottom = y;
    }

    /// <summary>
    /// The items of the Language list: the system's language first, named in the words of the moment with the language it
    /// stands for in its own, then each language in its own words.
    /// </summary>
    private DropItem[] LanguageChoices() =>
    [
        new(GeneralSettings.LanguageSystem, Tr.Of("System", "Système") + " (" + UiLanguages.OwnName(SystemLanguage()) + ")"),
        new(GeneralSettings.LanguageEnglish, UiLanguages.OwnName(UiLanguage.English)),
        new(GeneralSettings.LanguageFrench, UiLanguages.OwnName(UiLanguage.French)),
    ];

    private static DropItem[] UpdateChoices => [new("auto", Tr.Of("Install automatically", "Installer automatiquement")), new("ask", AskMe), new("off", Off)];

    // A copy that only tells cannot install: Install automatically tells, as Ask me does. A saved "auto" is not in this list,
    // and a list shows a value it does not hold as its first item, Ask me: it stays first.
    private static DropItem[] AnnounceChoices => [new("ask", AskMe), new("off", Off)];

    private static string AskMe => Tr.Of("Ask me", "Me demander");

    private static string Off => Tr.Of("Off", "Désactivé");

    /// <summary>The Updates section, under the test button. Returns where the next line starts.</summary>
    private float BuildUpdates(UpdatePanelView view, float y)
    {
        var divider = y - 7;
        Add(new Painted((c, _) => c.FillRect(new Box(Left, divider, Width, 1), Theme.Line)));
        y += 8;
        _updatesTop = y;
        Heading(Tr.Of("Updates", "Mises à jour"), y);
        var top = y;
        var version = view.VersionLine;
        Add(new Painted((c, _) => c.TextRight(version, Face.Body, 13, Right, top + 15, Theme.Muted)));
        y += 38;

        // A copy that does not update itself only says so: no list, no buttons, no word about GitHub.
        if (view.Active)
        {
            if (view.AnnouncesOnly)
            {
                Row(Tr.Of("When a version is out", "Quand une version sort"), "general.updates", y, () => AnnounceChoices, () => Editor.Settings.General.Updates.Mode, Editor.SetUpdateMode);
            }
            else
            {
                Row(Tr.Of("When a version is out", "Quand une version sort"), "general.updates", y, () => UpdateChoices, () => Editor.Settings.General.Updates.Mode, Editor.SetUpdateMode);
            }

            y += RowHeight + 12;
        }

        var status = view.StatusLine;
        var statusMid = y;
        Add(new Painted((c, _) => c.TextMid(c.Fit(status, Face.Body, 13, Width), Face.Body, 13, Left, statusMid, Theme.Muted)) { Id = "updates.status" });
        if (!view.Active)
        {
            return y + 20 + 14;
        }

        y += 20;
        var buttons = new List<(string Id, string Label, Action Click, bool Enabled)> { ("updates.check", Tr.Of("Check now", "Vérifier maintenant"), CheckNow, view.CheckEnabled) };
        // Where nothing installs, the button opens the page of the release, which the panel opens for What's new.
        if (view.ShowUpdate && view.AnnouncesOnly)
        {
            buttons.Add(("updates.download", Tr.Of("Download", "Télécharger"), () => _updates?.WhatsNew(), view.UpdateEnabled));
        }
        else if (view.ShowUpdate)
        {
            buttons.Add(("updates.now", Tr.Of("Update now", "Mettre à jour"), ShowUpdateCard, view.UpdateEnabled));
        }

        if (view.ShowWhatsNew)
        {
            buttons.Add(("updates.whatsnew", Tr.Of("What's new", "Nouveautés"), () => _updates?.WhatsNew(), true));
        }

        if (view.ShowSkip)
        {
            buttons.Add(("updates.skip", Tr.Of("Skip this version", "Ignorer cette version"), SkipThisVersion, view.SkipEnabled));
        }

        // One line while they fit, which they do at the design's sizes; a wider font puts the last ones under.
        var x = Left;
        foreach (var (id, label, click, enabled) in buttons)
        {
            var width = Button.WidthFor(_canvas, label);
            if (x > Left && x + width > Right)
            {
                x = Left;
                y += 34 + 10;
            }

            Add(new Button(label, click) { Id = id, Enabled = enabled, Bounds = new Box(x, y, width, 34) });
            x += width + 12;
        }

        y += 34 + 14;
        var noteTop = y;
        Add(new Painted((c, _) => c.Wrapped(GitHubNote, Face.Body, 13, Left, noteTop, Width, Theme.Muted)) { Id = "updates.github" });
        y += WrapLines(_canvas, GitHubNote, Face.Body, 13, Width).Count * _canvas.LineHeight(Face.Body, 13) + 14;
        return y;
    }

    private void CheckNow()
    {
        _updates?.CheckNow();
        Refresh();
    }

    private void SkipThisVersion()
    {
        _updates?.SkipThisVersion();
        Refresh();
    }

    /// <summary>The lines that Wrapped draws, cut the same way: what a page must leave room for.</summary>
    private static List<string> WrapLines(ICanvas c, string text, Face face, float size, float width)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var attempt = line.Length == 0 ? word : line + " " + word;
            if (c.Measure(attempt, face, size) > width && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = attempt;
            }
        }

        lines.Add(line);
        return lines;
    }

    // ---------------------------------------------------------------- the update card

    private static string UpdateLabel => Tr.Of("Update", "Mettre à jour");

    private static string SaveThenUpdateLabel => Tr.Of("Save it, then update", "Enregistrer d'abord");

    private static string UpdateCancelLabel => Tr.Of("Cancel", "Annuler");

    private void BuildUpdateCard()
    {
        const float cardLeft = 130, cardRight = WindowWidth - 130, cardTop = BandHeight + 60, inner = cardLeft + 28, textWidth = cardRight - cardLeft - 56;
        var panel = _updates;
        var view = panel?.View;
        var busy = view?.CardBusy == true;
        var clipBusy = panel?.ClipBusy == true;
        var title = view?.Offered is { } offered ? Tr.Format($"Update to {offered}", $"Mettre à jour vers {offered}") : UpdateLabel;
        var body = StatusText.UpdateCardBody(_status, panel?.BufferSeconds ?? 0);

        // A download under way speaks first; then a clip still being written, which greys the two buttons.
        var line = busy ? view!.CardLine : clipBusy ? ClipBusyNoteText : view?.CardLine;
        var bodyTop = cardTop + 66;
        var bodyHeight = WrapLines(_canvas, body, Face.Body, 14, textWidth).Count * _canvas.LineHeight(Face.Body, 14);
        var lineMid = bodyTop + bodyHeight + 18;
        var buttonY = lineMid + 26;
        var cardHeight = buttonY + 34 + 24 - cardTop;
        Add(new Painted((c, _) =>
        {
            c.Frame(new Box(cardLeft, cardTop, cardRight - cardLeft, cardHeight), 6, Theme.Panel, Theme.Line);
            c.Text(title, Face.Title, 22, inner, cardTop + 22, Theme.Text);
            c.Wrapped(body, Face.Body, 14, inner, bodyTop, textWidth, Theme.Muted);
            if (line is not null)
            {
                c.TextMid(c.Fit(line, Face.Medium, 14, textWidth), Face.Medium, 14, inner, lineMid, Theme.Text);
            }
        }) { Id = "card.text" }, scrolls: false);

        var canUpdate = view is { UpdateEnabled: true } && !busy && !clipBusy;
        var cancelWidth = Button.WidthFor(_canvas, UpdateCancelLabel, padding: 22);
        var updateWidth = Button.WidthFor(_canvas, UpdateLabel, padding: 22);
        var saveWidth = Button.WidthFor(_canvas, SaveThenUpdateLabel, padding: 22);
        var cancelBox = new Box(cardRight - 28 - cancelWidth, buttonY, cancelWidth, 34);
        var updateBox = new Box(cancelBox.X - 12 - updateWidth, buttonY, updateWidth, 34);
        var saveBox = new Box(updateBox.X - 12 - saveWidth, buttonY, saveWidth, 34);
        Add(new Button(SaveThenUpdateLabel, () => Confirm(saveFirst: true)) { Id = "card.save", Enabled = canUpdate, Bounds = saveBox }, scrolls: false);

        // Greyed, the accent would still say "press me": it only goes on a button that can be pressed.
        Add(new Button(UpdateLabel, () => Confirm(saveFirst: false)) { Id = "card.update", Accented = canUpdate, Enabled = canUpdate, Bounds = updateBox }, scrolls: false);
        Add(new Button(UpdateCancelLabel, CloseUpdateCard) { Id = "card.cancel", Bounds = cancelBox }, scrolls: false);
        _contentBottom = WindowHeight;
    }

    private void Confirm(bool saveFirst)
    {
        _updates?.ConfirmUpdate(saveFirst);
        Refresh();
    }

    private static DropItem[] FullscreenChoices => [new("auto", Tr.Of("Auto", "Auto")), new("soundOnly", Tr.Of("Sound only", "Son seul"))];

    private static DropItem[] SoundChoices =>
        [new("whenNoOverlay", Tr.Of("When no overlay can show", "Sans notification visible")), new("always", Tr.Of("Always", "Toujours")), new("never", Tr.Of("Never", "Jamais"))];

    private int SwatchSelected()
    {
        if (_customAccentOpen)
        {
            return SettingsEditor.AccentOrder.Length;
        }

        var preset = AccentColors.PresetOf(Editor.AccentHex);
        var index = Array.IndexOf(SettingsEditor.AccentOrder, preset);
        return index < 0 ? SettingsEditor.AccentOrder.Length : index;
    }

    private void PickSwatch(int index)
    {
        if (index >= SettingsEditor.AccentOrder.Length)
        {
            _customAccentOpen = true;
            _hexField.Draft = Editor.AccentHex;
            _hexField.Message = null;
            _forceEditText = true;
            Refresh();
            FocusFieldIfClicked(HexFieldId);
            return;
        }

        _customAccentOpen = false;
        _hexField.Message = null;
        Editor.SetAccentPreset(SettingsEditor.AccentOrder[index]);
        _hexField.Draft = Editor.AccentHex;
        Refresh();
    }

    // ---------------------------------------------------------------- first launch

    private static string[] FirstRunSteps => [Tr.Of("Pick your hotkey", "Choisis ton raccourci"), Tr.Of("Check your mic", "Vérifie ton micro"), Tr.Of("Try it", "Essaie")];

    // The app has only just started: a clip made at once would hold next to nothing.
    internal static string FirstRunWait => Tr.Of("Wait a few seconds.", "Attends un peu.");
    internal static string FirstRunSay => Tr.Of("Say something, then press your hotkey.", "Dis un mot, puis appuie sur ton raccourci.");
    internal static string FirstRunSayCommand => Tr.Of("Say something, then press the Save button.", "Dis un mot, puis appuie sur Enregistrer.");
    internal const float FirstRunNoteGap = 4;

    /// <summary>The room of a line in the panel of the card: nothing cuts a note that goes past it.</summary>
    internal const float FirstRunTextWidth = 420;

    public int FirstRunStep => _firstRunStep;

    public bool FirstRunClipDone => _firstRunClipDone;

    private void BuildFirstRun()
    {
        const float x = 40, panelLeft = 260, panelRight = panelLeft + FirstRunTextWidth + 40;
        var step = _firstRunStep;
        var done = _firstRunClipDone;
        var mode = Features.Shortcuts();
        Add(new Painted((c, ui) =>
        {
            c.Text(Tr.Of("Welcome to Hindsound", "Bienvenue dans Hindsound"), Face.Title, 22, x, BandHeight + 14, Theme.Text);
            for (var i = 0; i < FirstRunSteps.Length; i++)
            {
                var mid = BandHeight + 72 + i * 44;
                var circle = new Box(x, mid - 12, 24, 24);
                var finished = i < step || (i == step && done);
                if (i == step || finished)
                {
                    c.FillRound(circle, 12, ui.Accent);
                }
                else
                {
                    c.Frame(circle, 12, Theme.Panel, Theme.Control);
                }

                if (finished)
                {
                    c.Stroke(circle.X + 7, mid, circle.X + 10.5f, mid + 4, 2, ui.OnAccent);
                    c.Stroke(circle.X + 10.5f, mid + 4, circle.X + 17, mid - 4, 2, ui.OnAccent);
                }
                else
                {
                    c.TextCentered((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Face.Medium, 13, circle, i == step ? ui.OnAccent : Theme.Muted);
                }

                c.TextMid(FirstRunSteps[i], i == step ? Face.Medium : Face.Body, 14, x + 34, mid, i == step ? Theme.Text : Theme.Muted);
            }

            c.Frame(new Box(panelLeft, BandHeight + 52, panelRight - panelLeft, 196), 6, Theme.Panel, Theme.Line);
            c.Text(FirstRunSteps[step], Face.Title, 20, panelLeft + 20, BandHeight + 64, Theme.Text);
        }), scrolls: false);

        var inner = panelLeft + 20;
        var top = BandHeight + 100;
        var width = panelRight - panelLeft - 40;
        switch (step)
        {
            case 0:
                FirstRunNote(StatusText.FirstRunHotkeyNote(Editor.Settings.ClipSeconds), inner, top);
                switch (mode)
                {
                    case ShortcutMode.Portal:
                        FirstRunDesktopKey(inner, top + 26, width);
                        break;
                    case ShortcutMode.Command:
                        FirstRunCommand(inner, top + 26, width);
                        break;
                    default:
                        Add(
                            new HotkeyField(HotkeyAction.SaveDefault, () => ViewOf(HotkeyAction.SaveDefault), BeginCapture)
                            {
                                Id = "hotkey." + HotkeyAction.SaveDefault,
                                Bounds = new Box(inner, top + 26, 280, 34),
                            },
                            scrolls: false);
                        break;
                }

                break;
            case 1:
                FirstRunNote(Tr.Format($"Speak: the bar should move. Off if you only want the {Features.MachineName} sound.", $"Parle\u00A0: la barre doit bouger. Choisis Coupé pour le son du {Features.MachineName} seul."), inner, top);
                Add(
                    new Dropdown(() => SettingsEditor.MicChoices(_mics, Editor.Settings.Audio.MicDeviceId), () => Editor.Settings.Audio.MicDeviceId, Editor.SetMic, OpenPopup)
                    {
                        Id = "audio.mic",
                        Bounds = new Box(inner, top + 26, width, RowHeight),
                    },
                    scrolls: false);
                Add(new MeterBar(() => _meter.Level, MeterCaption) { Bounds = new Box(inner, top + 74, width, 12) }, scrolls: false);
                break;
            default:
                var gesture = (mode == ShortcutMode.Capture ? Editor.Settings.Hotkeys.SaveDefault : ShownKey(HotkeyAction.SaveDefault)) ?? string.Empty;
                var say = mode == ShortcutMode.Command ? FirstRunSayCommand : FirstRunSay;
                FirstRunNote(FirstRunWait, inner, top);
                Add(new Painted((c, _) => c.TextMid(say, Face.Body, 14, inner + c.Measure(FirstRunWait, Face.Body, 14) + FirstRunNoteGap, top + 8, Theme.Muted)), scrolls: false);
                if (mode == ShortcutMode.Capture)
                {
                    Add(new Painted((c, _) => c.Keycaps(Features.KeyText(gesture), inner, top + 42)), scrolls: false);
                }
                else if (gesture.Length > 0)
                {
                    // A desktop words its key as it likes: drawn as keys only when the app can read it back as some.
                    var asKeys = HotkeyGesture.TryParse(gesture, out _);
                    Add(new Painted((c, _) =>
                    {
                        if (asKeys)
                        {
                            c.Keycaps(Features.KeyText(gesture), inner, top + 42);
                        }
                        else
                        {
                            c.TextMid(c.Fit(gesture, Face.Body, 14, width), Face.Body, 14, inner, top + 42, Theme.Text);
                        }
                    }), scrolls: false);
                }

                if (done && _lastClip is { } clip)
                {
                    if (clip.Notice is { } notice && !NotificationStates.SaysPaste(notice))
                    {
                        Add(new Painted((c, _) => c.TextMid(c.Fit(notice.Body, Face.Medium, 14, width), Face.Medium, 14, inner, top + 84, Theme.Text)), scrolls: false);
                    }
                    else
                    {
                        Add(new Painted((c, _) =>
                        {
                            var paste = Tr.Of("Now paste it anywhere.", "Colle-le maintenant n'importe où.");
                            c.TextMid(paste, Face.Medium, 14, inner, top + 84, Theme.Text);
                            c.Keycaps(Features.KeyText(Features.PasteGesture), inner + 8 + c.Measure(paste, Face.Medium, 14), top + 84);
                        }), scrolls: false);
                    }
                }

                break;
        }

        var lineY = BandHeight + 258;
        Add(new Painted((c, _) => c.FillRect(new Box(x, lineY, panelRight - x, 1), Theme.Line)), scrolls: false);
        var rowY = lineY + 8;
        Add(new Painted((c, _) =>
        {
            c.TextMid(Features.StartupLabel, Face.Body, 14, x, rowY + 15, Theme.Text);
            c.Wrapped(LegalNote, Face.Body, 13, x, rowY + 38, 420, Theme.Muted);
        }), scrolls: false);
        Add(new Toggle(() => Editor.Settings.General.StartWithWindows, Editor.SetStartWithWindows) { Id = "run.startup", Bounds = new Box(x + 160, rowY + 4, 40, 22) }, scrolls: false);

        var buttonY = lineY + 104;
        var last = step == FirstRunSteps.Length - 1;
        var nextLabel = last ? Tr.Of("Done", "Terminer") : Tr.Of("Next", "Suivant");
        var backLabel = Tr.Of("Back", "Retour");
        var nextWidth = Button.WidthFor(_canvas, nextLabel, padding: 22);

        // The card has no height left: the word about GitHub goes left of the buttons, and stays at every step. A run
        // that cannot update says nothing about it.
        if (_updates?.View.Active == true)
        {
            var room = panelRight - nextWidth - Button.WidthFor(_canvas, backLabel, padding: 22) - 28 - x;
            var lines = WrapLines(_canvas, FirstRunUpdatesNote, Face.Body, 13, room);
            if (lines.Count > 2)
            {
                lines = [lines[0], _canvas.Fit(string.Join(' ', lines.Skip(1)), Face.Body, 13, room)];
            }

            var lineHeight = _canvas.LineHeight(Face.Body, 13);
            var mentionTop = buttonY + (34 - lines.Count * lineHeight) / 2;
            Add(
                new Painted((c, _) =>
                {
                    for (var i = 0; i < lines.Count; i++)
                    {
                        c.Text(lines[i], Face.Body, 13, x, mentionTop + i * lineHeight, Theme.Muted);
                    }
                })
                {
                    Id = FirstRunUpdatesId,
                    Bounds = new Box(x, mentionTop, room, lines.Count * lineHeight),
                },
                scrolls: false);
        }

        if (step > 0)
        {
            var backWidth = Button.WidthFor(_canvas, backLabel, padding: 22);
            Add(new Button(backLabel, FirstRunBack) { Id = "run.back", Bounds = new Box(panelRight - nextWidth - 12 - backWidth, buttonY, backWidth, 34) }, scrolls: false);
        }

        Add(new Button(nextLabel, FirstRunNext) { Id = "run.next", Accented = true, Bounds = new Box(panelRight - nextWidth, buttonY, nextWidth, 34) }, scrolls: false);
        _contentBottom = WindowHeight;
    }

    /// <summary>The key the desktop chose for Save, in a first step where the user has none to type.</summary>
    private void FirstRunDesktopKey(float x, float y, float width)
    {
        var box = new Box(x, y, 280, 34);
        var bound = ShownKey(HotkeyAction.SaveDefault);
        var text = ShownKeyText(HotkeyAction.SaveDefault);
        var asKeys = bound is not null && HotkeyGesture.TryParse(bound, out _);
        Add(
            new Painted((c, _) =>
            {
                c.Frame(box, 4, Theme.Raised, Theme.Control);
                if (asKeys)
                {
                    c.Keycaps(Features.KeyText(bound!), box.X + 8, box.MidY);
                }
                else
                {
                    c.TextMid(c.Fit(text, Face.Body, 13, box.W - 16), Face.Body, 13, box.X + 8, box.MidY, bound is null ? Theme.Muted : Theme.Text);
                }
            })
            {
                Id = HotkeyShownPrefix + HotkeyAction.SaveDefault,
                Bounds = box,
            },
            scrolls: false);
        var noteTop = y + 34 + 12;
        Add(new Painted((c, _) => c.Wrapped(DesktopKeysNote, Face.Body, 13, x, noteTop, width, Theme.Muted)) { Id = HotkeyNoteId }, scrolls: false);
    }

    /// <summary>The command to bind, with its Copy button, in a first step where nothing can grab a key.</summary>
    private void FirstRunCommand(float x, float y, float width)
    {
        var command = Features.SaveCommand();
        if (command is null)
        {
            Add(new Painted((c, _) => c.Wrapped(CommandGenericIntro, Face.Body, 13, x, y, width, Theme.Muted)) { Id = HotkeyNoteId }, scrolls: false);
            return;
        }

        Add(new Painted((c, _) => c.Wrapped(CommandIntro, Face.Body, 13, x, y, width, Theme.Muted)) { Id = HotkeyNoteId }, scrolls: false);
        var fieldTop = y + WrapLines(_canvas, CommandIntro, Face.Body, 13, width).Count * _canvas.LineHeight(Face.Body, 13) + 8;
        var copyWidth = CopyButtonWidth();
        var box = new Box(x, fieldTop, width - copyWidth - 12, 34);
        Add(
            new Painted((c, _) =>
            {
                c.Frame(box, 4, Theme.Raised, Theme.Control);
                c.TextMid(c.Fit(command, Face.Mono, 13, box.W - 20), Face.Mono, 13, box.X + 10, box.MidY, Theme.Text);
            })
            {
                Id = HotkeyCommandId,
                Bounds = box,
            },
            scrolls: false);
        Add(new Button(_commandCopied ? CopiedLabel : CopyLabel, CopyCommand) { Id = HotkeyCopyId, Bounds = new Box(x + width - copyWidth, fieldTop, copyWidth, 34) }, scrolls: false);
    }

    private void FirstRunNote(string text, float x, float y) =>
        Add(new Painted((c, _) => c.TextMid(text, Face.Body, 14, x, y + 8, Theme.Muted)), scrolls: false);

    private void FirstRunBack()
    {
        CancelCapture();
        _firstRunStep = Math.Max(0, _firstRunStep - 1);
        _firstRunClipDone = false;
        _focusId = "run.next";
        Refresh();
    }

    private void FirstRunNext()
    {
        CancelCapture();
        if (_firstRunStep < FirstRunSteps.Length - 1)
        {
            _firstRunStep++;
            _focusId = "run.next";
            Refresh();
            return;
        }

        _firstRunStep = 0;
        _firstRunClipDone = false;
        _focusId = NavId;
        Section = FirstSection;
        _scroll = 0;
        Refresh();
    }
}
