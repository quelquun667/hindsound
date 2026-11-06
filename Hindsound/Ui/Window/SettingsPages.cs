using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary>What is on each page, top to bottom. The sizes are those of the mockup (FenetreB).</summary>
internal sealed partial class SettingsScreen
{
    public const string LegalNote = "Recording a conversation without telling the people in it can be illegal. Let them know before you keep it.";
    public const string ShorterBufferNote = "A shorter buffer drops the oldest audio";
    public const string SoundHelp = "The beep can be heard in your next clip if you capture a specific output.";

    private const float Left = 200, Right = 728, Width = Right - Left, RowHeight = 30, FieldWidth = 220;
    private const string HexFieldId = "general.hex", TemplateFieldId = "export.template", ClipFieldId = "durations.custom", SwatchId = "general.accent";

    private readonly FieldState _clipField;
    private readonly FieldState _hexField;
    private readonly FieldState _templateField;
    private bool _customClipOpen;
    private bool _customAccentOpen;
    private string? _folderMessage;
    private int _firstRunStep;
    private bool _firstRunClipDone;

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
        var band = StatusText.From(_status);
        Chrome(new Painted((c, ui) => PaintBand(c, ui, band)) { Bounds = new Box(0, 0, WindowWidth, BandHeight) });

        var saveLabel = StatusText.SaveLabel(Editor.Settings.ClipSeconds);
        var saveGesture = Editor.Settings.Hotkeys.SaveDefault ?? string.Empty;
        var saveWidth = Math.Max(_canvas.Measure(saveLabel, Face.Medium, 16), _canvas.Measure(saveGesture, Face.Mono, 11)) + 36;
        var saveBox = new Box(WindowWidth - 24 - MathF.Ceiling(saveWidth), 10, MathF.Ceiling(saveWidth), 52);
        var pauseWidth = Button.WidthFor(_canvas, "Resume", icon: true);
        Chrome(new Button(band.PauseLabel, _backend.TogglePause)
        {
            Id = "band.pause",
            Icon = band.PauseLabel == "Resume" ? ButtonIcon.Play : ButtonIcon.Pause,
            Enabled = band.PauseEnabled,
            Bounds = new Box(saveBox.X - 20 - pauseWidth, 18, pauseWidth, 36),
        });
        Chrome(new Button(saveLabel, _backend.SaveNow)
        {
            Id = "band.save",
            Accented = true,
            SubLabel = saveGesture,
            FontSize = 16,
            Bounds = saveBox,
        });
    }

    private static void PaintBand(ICanvas c, UiState ui, StatusBand band)
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
        }

        c.TextMid(band.Title, Face.Title, 22, x, mid, Theme.Text);
        x += c.Measure(band.Title, Face.Title, 22) + 20;
        if (band.Mic.Length > 0)
        {
            c.TextMid(band.Mic, Face.Body, 14, x, mid, Theme.Muted);
            x += c.Measure(band.Mic, Face.Body, 14) + 20;
            c.TextMid(band.Duration, Face.Mono, 13, x, mid, Theme.Text);
            x += c.Measure(band.Duration, Face.Mono, 13) + 20;
            c.TextMid(band.Ram, Face.Mono, 13, x, mid, Theme.Muted);
        }
    }

    private void BuildNav()
    {
        Chrome(new Painted((c, _) =>
        {
            c.FillRect(new Box(0, BandHeight, NavWidth, ViewBottom - BandHeight), Theme.Panel);
            c.FillRect(new Box(NavWidth - 1, BandHeight, 1, ViewBottom - BandHeight), Theme.Line);
        }));
        Chrome(new NavList(SectionNames, () => (int)Section, i => ShowSection((Section)i))
        {
            Id = NavId,
            Bounds = new Box(12, BandHeight + 16, NavWidth - 25, 5 * 36 + 4 * 4),
        });
    }

    private void BuildFoot()
    {
        var top = WindowHeight - FootHeight;
        var clip = _lastClip;
        Chrome(new Painted((c, _) =>
        {
            c.FillRect(new Box(0, top, WindowWidth, FootHeight), Theme.Panel);
            c.FillRect(new Box(0, top, WindowWidth, 1), Theme.Line);
            var mid = top + FootHeight / 2;
            c.TextMid("Last clip", Face.Title, 18, 24, mid, Theme.Muted);
            var x = 24 + c.Measure("Last clip", Face.Title, 18) + 16;
            if (clip is null)
            {
                c.TextMid("None yet", Face.Body, 13, x, mid, Theme.Muted);
                return;
            }

            c.TextMid(clip.DurationText, Face.Mono, 13, x, mid, Theme.Text);
            x += c.Measure(clip.DurationText, Face.Mono, 13) + 16;
            c.TextMid(clip.TimeText, Face.Mono, 13, x, mid, Theme.Muted);
            x += c.Measure(clip.TimeText, Face.Mono, 13) + 16;
            c.TextMid(clip.SizeText, Face.Mono, 13, x, mid, Theme.Muted);
        }));

        var openWidth = Button.WidthFor(_canvas, "Open folder");
        var copyWidth = Button.WidthFor(_canvas, "Copy");
        var buttonTop = top + (FootHeight - 34) / 2;
        Chrome(new Button("Copy", _backend.CopyLast)
        {
            Id = "foot.copy",
            Enabled = clip is not null,
            Bounds = new Box(WindowWidth - 24 - openWidth - 16 - copyWidth, buttonTop, copyWidth, 34),
        });
        Chrome(new Button("Open folder", _backend.OpenFolder)
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

    private Dropdown List(string id, Box box, Func<IReadOnlyList<DropItem>> items, Func<string> selected, Action<string> pick) =>
        Add(new Dropdown(items, selected, pick, OpenPopup) { Id = id, Bounds = box });

    private void Row(string label, string id, float y, Func<IReadOnlyList<DropItem>> items, Func<string> selected, Action<string> pick)
    {
        RowLabel(label, y);
        List(id, new Box(Right - FieldWidth, y, FieldWidth, RowHeight), items, selected, pick);
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

    private void BuildHotkeys()
    {
        const float pitch = 37, fieldWidth = 280, fieldHeight = 32;
        var y = BandHeight + 18;
        Heading("Hotkeys", y);
        y += 36;
        foreach (var (action, label) in SettingsEditor.HotkeyRows)
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

        y += 6;
        var width = Button.WidthFor(_canvas, "Reset to defaults");
        Add(new Button("Reset to defaults", ResetHotkeys) { Id = "hotkey.reset", Bounds = new Box(Right - width, y, width, 34) });
        _contentBottom = y + 34;
    }

    // ---------------------------------------------------------------- durations

    private void BuildDurations()
    {
        var settings = Editor.Settings;
        var y = BandHeight + 22;
        Heading("Clip length", y);
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
            RowLabel("Custom length", y);
            Add(new TextField(_clipField, SettingsEditor.CheckCustomClip, Editor.CommitCustomClip, ClipFieldText)
            {
                Id = ClipFieldId,
                Bounds = new Box(Left + 120, y, 100, RowHeight),
                Help = SettingsEditor.CustomClipHelp,
            });
            y += RowHeight + TextField.MessageHeight;
        }

        y += 28;
        Heading("Keep in memory", y);
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
            var floor = "At least " + NotificationStates.FormatDuration(Editor.BufferFloor) + ": your longest clip";
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
        Heading("Audio", y);
        y += 36;
        Row("Record", "audio.record", y, () => SettingsEditor.RecordChoices(_apps, Editor.Settings.Audio.SourceApp), () => Editor.Settings.Audio.SourceApp ?? string.Empty, Editor.SetSourceApp);
        y += pitch;
        Row("Output", "audio.output", y, () => SettingsEditor.OutputChoices(_outputs, Editor.Settings.Audio.OutputDeviceId), () => Editor.Settings.Audio.OutputDeviceId, Editor.SetOutput);
        y += pitch;
        Row("Microphone", "audio.mic", y, () => SettingsEditor.MicChoices(_mics, Editor.Settings.Audio.MicDeviceId), () => Editor.Settings.Audio.MicDeviceId, Editor.SetMic);
        y += pitch;
        RowLabel("Mic level", y);
        Add(new MeterBar(() => _meter.Level, MeterCaption) { Bounds = new Box(Right - FieldWidth, y + 9, FieldWidth, 12) });
        y += pitch;
        SegmentRow("Tracks", "audio.mode", y, ["Mix", "Separate"], () => Editor.Settings.Audio.Mode == "mix" ? 0 : 1, i => Editor.SetMode(i == 0 ? "mix" : "multitrack"));
        y += pitch;
        GainRow("PC sound gain", "audio.systemGain", y, audio.SystemGainDb, Editor.SetSystemGain, true);
        y += pitch;
        GainRow("Mic gain", "audio.micGain", y, audio.MicGainDb, Editor.SetMicGain, audio.MicEnabled);
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
        if (!Editor.Settings.Audio.MicEnabled)
        {
            return "The microphone is off";
        }

        if (_status is null)
        {
            return "Starting";
        }

        return _status.MicCapturing ? null : _status.Activity == Audio.CaptureActivity.Paused ? "Capture is paused" : "No microphone found";
    }

    // ---------------------------------------------------------------- export

    private void BuildExport()
    {
        const float pitch = 38;
        var export = Editor.Settings.Export;
        var y = BandHeight + 18;
        Heading("Export", y);
        y += 36;
        SegmentRow("Format", "export.format", y, ["WAV", "MP3"], () => Editor.Settings.Export.Format == "wav" ? 0 : 1, i => Editor.SetFormat(i == 0 ? "wav" : "mp3"));
        y += pitch;
        SegmentRow(
            "MP3 quality",
            "export.bitrate",
            y,
            ["128", "192", "320"],
            () => Math.Max(0, Array.IndexOf(SettingsEditor.Bitrates, Editor.Settings.Export.Mp3Bitrate)),
            i => Editor.SetBitrate(SettingsEditor.Bitrates[i]),
            export.Format != "wav");
        y += pitch;
        SegmentRow(
            "Volume",
            "export.normalization",
            y,
            ["Off", "Peak", "Loudness"],
            () => Editor.Settings.Export.Normalization switch { "off" => 0, "peak" => 1, _ => 2 },
            i => Editor.SetNormalization(i switch { 0 => "off", 1 => "peak", _ => "loudness" }));
        y += pitch;

        RowLabel("Folder", y);
        var browse = Button.WidthFor(_canvas, "Browse", 13, 12);
        Add(new Button("Browse", PickFolder) { Id = "export.browse", FontSize = 13, Bounds = new Box(Right - browse, y, browse, RowHeight) });
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

        RowLabel("File name", y);
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
        ToggleRow("Copy clip to clipboard", "export.copy", y, () => Editor.Settings.Export.CopyToClipboard, Editor.SetCopyToClipboard);
        _contentBottom = y + RowHeight;
    }

    // ---------------------------------------------------------------- general

    private void BuildGeneral()
    {
        const float pitch = 36;
        var general = Editor.Settings.General;
        var y = BandHeight + 18;
        Heading("Accent color", y);
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
        var names = order.Select(p => p.ToString()).Append("Custom").ToArray();
        Add(new SwatchGroup(colors, names, SwatchSelected, PickSwatch)
        {
            Id = SwatchId,
            Bounds = new Box(Left + 4, y, Width - 8, 30),
        });
        y += 44;

        if (_customAccentOpen)
        {
            RowLabel("Color code", y);
            Add(new TextField(_hexField, SettingsEditor.CheckAccent, t => Editor.SetAccentHex(t), () => Editor.AccentHex)
            {
                Id = HexFieldId,
                Bounds = new Box(Left + 100, y, 110, RowHeight),
                Help = "Any bright color works.",
            });
            y += RowHeight + TextField.MessageHeight + 6;
        }

        var accent = Editor.AccentRgb;
        var previewTop = y;
        var leftEdge = general.NotificationPosition == "left";
        Add(new Painted((c, _) => c.Notification(SampleClip.Notification(accent), Left, previewTop, accent, leftEdge)));
        y += 64 + 14;
        var divider = y - 7;
        Add(new Painted((c, _) => c.FillRect(new Box(Left, divider, Width, 1), Theme.Line)));
        y += 4;

        ToggleRow("Start with Windows", "general.startup", y, () => Editor.Settings.General.StartWithWindows, Editor.SetStartWithWindows);
        y += pitch;
        ToggleRow("Save notifications", "general.notifications", y, () => Editor.Settings.General.Notifications, Editor.SetNotifications);
        y += pitch;
        SegmentRow("Notification position", "general.position", y, ["Right edge", "Left edge"], () => Editor.Settings.General.NotificationPosition == "left" ? 1 : 0, i => Editor.SetNotificationPosition(i == 1 ? "left" : "right"));
        y += pitch;
        Row("In fullscreen games", "general.fullscreen", y, () => FullscreenChoices, () => Editor.Settings.General.FullscreenGames, Editor.SetFullscreenGames);
        y += pitch;
        Row("Confirmation sound", "general.sound", y, () => SoundChoices, () => Editor.Settings.General.ConfirmSound, Editor.SetConfirmSound);
        y += RowHeight + 12;
        Note(SoundHelp, y);
        y += 20;
        var testWidth = Button.WidthFor(_canvas, "Show test notification");
        Add(new Button("Show test notification", _backend.ShowTestNotification) { Id = "general.test", Bounds = new Box(Left, y, testWidth, 34) });
        y += 34 + 14;
        var noteTop = y;
        Add(new Painted((c, _) => c.Wrapped(LegalNote, Face.Body, 13, Left, noteTop, Width, Theme.Muted)));
        y += 2 * _canvas.LineHeight(Face.Body, 13);
        _contentBottom = y;
    }

    private static readonly DropItem[] FullscreenChoices = [new("auto", "Auto"), new("soundOnly", "Sound only")];

    private static readonly DropItem[] SoundChoices = [new("whenNoOverlay", "When no overlay can show"), new("always", "Always"), new("never", "Never")];

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
            return;
        }

        _customAccentOpen = false;
        _hexField.Message = null;
        Editor.SetAccentPreset(SettingsEditor.AccentOrder[index]);
        _hexField.Draft = Editor.AccentHex;
        Refresh();
    }

    // ---------------------------------------------------------------- first launch

    private static readonly string[] FirstRunSteps = ["Pick your hotkey", "Check your mic", "Try it"];

    public int FirstRunStep => _firstRunStep;

    public bool FirstRunClipDone => _firstRunClipDone;

    private void BuildFirstRun()
    {
        const float x = 40, panelLeft = 260, panelRight = 720;
        var step = _firstRunStep;
        var done = _firstRunClipDone;
        Add(new Painted((c, ui) =>
        {
            c.Text("Welcome to Hindsound", Face.Title, 22, x, BandHeight + 14, Theme.Text);
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
                    c.Stroke(circle.X + 7, mid, circle.X + 10.5f, mid + 4, 2, Theme.OnAccent);
                    c.Stroke(circle.X + 10.5f, mid + 4, circle.X + 17, mid - 4, 2, Theme.OnAccent);
                }
                else
                {
                    c.TextCentered((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Face.Medium, 13, circle, i == step ? Theme.OnAccent : Theme.Muted);
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
                FirstRunNote("This saves the last 30 seconds from anywhere, even in a game.", inner, top);
                Add(
                    new HotkeyField(HotkeyAction.SaveDefault, () => ViewOf(HotkeyAction.SaveDefault), BeginCapture)
                    {
                        Id = "hotkey." + HotkeyAction.SaveDefault,
                        Bounds = new Box(inner, top + 26, 280, 34),
                    },
                    scrolls: false);
                break;
            case 1:
                FirstRunNote("Speak: the bar should move. Off if you only want the PC sound.", inner, top);
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
                var gesture = Editor.Settings.Hotkeys.SaveDefault ?? string.Empty;
                FirstRunNote("Say something, then press your hotkey.", inner, top);
                Add(new Painted((c, _) => c.Keycaps(gesture, inner, top + 42)), scrolls: false);
                if (done)
                {
                    Add(new Painted((c, _) =>
                    {
                        c.TextMid("Now paste it anywhere.", Face.Medium, 14, inner, top + 84, Theme.Text);
                        c.Keycaps("Ctrl+V", inner + 8 + c.Measure("Now paste it anywhere.", Face.Medium, 14), top + 84);
                    }), scrolls: false);
                }

                break;
        }

        var lineY = BandHeight + 258;
        Add(new Painted((c, _) => c.FillRect(new Box(x, lineY, panelRight - x, 1), Theme.Line)), scrolls: false);
        var rowY = lineY + 8;
        Add(new Painted((c, _) =>
        {
            c.TextMid("Start with Windows", Face.Body, 14, x, rowY + 15, Theme.Text);
            c.Wrapped(LegalNote, Face.Body, 13, x, rowY + 38, 420, Theme.Muted);
        }), scrolls: false);
        Add(new Toggle(() => Editor.Settings.General.StartWithWindows, Editor.SetStartWithWindows) { Id = "run.startup", Bounds = new Box(x + 160, rowY + 4, 40, 22) }, scrolls: false);

        var buttonY = lineY + 104;
        var last = step == FirstRunSteps.Length - 1;
        var nextLabel = last ? "Done" : "Next";
        var nextWidth = Button.WidthFor(_canvas, nextLabel, padding: 22);
        if (step > 0)
        {
            var backWidth = Button.WidthFor(_canvas, "Back", padding: 22);
            Add(new Button("Back", FirstRunBack) { Id = "run.back", Bounds = new Box(panelRight - nextWidth - 12 - backWidth, buttonY, backWidth, 34) }, scrolls: false);
        }

        Add(new Button(nextLabel, FirstRunNext) { Id = "run.next", Accented = true, Bounds = new Box(panelRight - nextWidth, buttonY, nextWidth, 34) }, scrolls: false);
        _contentBottom = WindowHeight;
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
        Section = Section.Hotkeys;
        _scroll = 0;
        Refresh();
    }
}
