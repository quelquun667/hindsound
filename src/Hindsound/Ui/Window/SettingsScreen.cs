using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Updates;

namespace Hindsound.Ui.Window;

internal enum Section
{
    Hotkeys,
    Durations,
    Audio,
    Export,
    General,
    FirstRun,

    /// <summary>Confirms an update. Not in the side bar: it opens from the Updates section, the tray menu or a notification.</summary>
    UpdateCard,

    /// <summary>What the memory holds, as it comes in. First in the side bar: the order of the lines is in <see cref="NavRows"/>, not here.</summary>
    Now,
}

/// <summary>What the screen asks of the app. The real one is <see cref="App.HindsoundApp"/>.</summary>
internal interface ISettingsBackend
{
    /// <summary>A setting changed: write the file and apply it.</summary>
    void Applied(SettingsChange change);

    void SaveNow();

    void TogglePause();

    void CopyLast();

    void OpenFolder();

    void ShowTestNotification();

    void SuspendHotkeys();

    IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys);

    IReadOnlyList<HotkeyResult> ResumeHotkeys();
}

/// <summary>What the screen asks of the window it lives in. Coordinates are in design pixels.</summary>
internal interface IWindowHost
{
    void Invalidate();

    /// <summary>Puts the native text box over a field, without touching the keyboard.</summary>
    /// <param name="box">Where, on the window.</param>
    /// <param name="text">The text to put in, if <paramref name="resetText"/>.</param>
    /// <param name="face">Its font.</param>
    /// <param name="size">Its size.</param>
    /// <param name="resetText">Another field, or a reverted one: the box must take this text. Otherwise it stays as typed.</param>
    void ShowEdit(Box box, string text, Face face, float size, bool resetText);

    void HideEdit();

    /// <summary>This window is the foreground one. Otherwise it must not move the keyboard: a game or a menu may hold it.</summary>
    bool IsForeground { get; }

    /// <summary>Gives the keyboard to the native text box.</summary>
    void FocusEdit();

    void CaptureMouse();

    void ReleaseMouse();

    /// <summary>Starts or stops reading the microphone level.</summary>
    void MeterWanted(bool wanted);

    /// <summary>Starts or stops the timer of the live wave.</summary>
    void WaveWanted(bool wanted)
    {
    }

    /// <summary>
    /// This part of the bitmap has already been drawn again: put it on screen, with no full paint. The wave's way of showing what
    /// changed; everything else goes through <see cref="Invalidate"/>.
    /// </summary>
    void InvalidateArea(Box area) => Invalidate();

    /// <summary>The system's folder picker. Null if the user backed out.</summary>
    string? PickFolder(string startFolder);

    void CloseWindow();
}

/// <summary>
/// The whole window as plain logic: pages, focus, keyboard, mouse, what is shown and in what order. Win32 only delivers
/// the input and the canvas, so the keyboard can be tested here, with no window.
/// </summary>
internal sealed partial class SettingsScreen
{
    public const float WindowWidth = 760, WindowHeight = 488, BandHeight = 72, FootHeight = 76, NavWidth = 168;
    public const float ViewBottom = WindowHeight - FootHeight;
    private const float ScrollStep = 48;
    private const string NavId = "nav";

    private readonly ISettingsBackend _backend;
    private readonly IWindowHost _host;
    private readonly HotkeyCapture _capture = new();
    private readonly MicMeter _meter = new();
    private readonly Dictionary<HotkeyAction, (string Gesture, string Message)> _attempts = [];
    private readonly Dictionary<HotkeyAction, string> _hotkeyProblems = [];
    private readonly List<Widget> _chrome = [];
    private readonly List<Widget> _page = [];
    private readonly List<Widget> _stops = [];

    private ICanvas _canvas;
    private AudioEngineStatus? _status;
    private LastClipInfo? _lastClip;
    private IReadOnlyList<AudioDeviceChoice> _outputs = [];
    private IReadOnlyList<AudioDeviceChoice> _mics = [];
    private IReadOnlyList<AudioAppChoice> _apps = [];

    private string? _focusId;
    private string? _pressedId;
    private string? _editId;
    private string? _scrolledAwayId;
    private string? _hoverId;
    private bool _forceEditText;
    private bool _clicking;
    private float _mouseX = -1, _mouseY = -1;
    private float _scroll;
    private float _pressScroll;
    private float _contentBottom;
    private bool _draggingScroll;
    private float _dragGrabY;
    private ListPopup? _popup;
    private (string Id, double Value, Action<double> Keep)? _sliding;

    public SettingsScreen(AppSettings settings, ISettingsBackend backend, IWindowHost host, ICanvas canvas, bool firstRun = false)
    {
        Editor = new SettingsEditor(settings, OnSettingChanged);
        _backend = backend;
        _host = host;
        _canvas = canvas;
        Section = firstRun ? Section.FirstRun : Section.Hotkeys;
        _clipField = new FieldState(ClipFieldText());
        _hexField = new FieldState(Editor.AccentHex);
        _templateField = new FieldState(settings.Export.FileNameTemplate);
        _customClipOpen = Editor.ClipSegment == SettingsEditor.CustomClip;
        _customAccentOpen = AccentColors.PresetOf(Editor.AccentHex) == AccentPreset.Custom;
        Rebuild();
        _focusId = firstRun ? "hotkey." + HotkeyAction.SaveDefault : NavId;
        Refresh();
    }

    public SettingsEditor Editor { get; }

    public Section Section { get; private set; }

    /// <summary>A page of the settings came on screen. The app keeps it: this screen is gone when the window closes, and the menu of the icon leads back to that page.</summary>
    public Action<Section>? SettingsPageSeen { get; set; }

    /// <summary>Off for the picture of a page at rest.</summary>
    public bool ShowFocus { get; set; } = true;

    /// <summary>For a picture: no native box exists, so a field being edited draws its own text.</summary>
    public bool NoNativeEdit { get; init; }

    public string? FocusId => _focusId;

    public Widget? Focus => Find(_focusId);

    public float Scroll => _scroll;

    public float MaxScroll => IsCard ? 0 : Math.Max(0, _contentBottom + 24 - ViewBottom);

    /// <summary>The tab stops, in the order Tab goes through them.</summary>
    public IReadOnlyList<string> StopIds() => _stops.Where(w => w.CanFocus).Select(w => w.Id).ToList();

    /// <summary>Any widget of the window by its id, painted ones included. Null when there is none.</summary>
    public Widget? WidgetById(string id) => _page.Concat(_chrome).FirstOrDefault(w => w.Id == id);

    /// <summary>A whole-window card, with no side bar, no foot and no scrolling.</summary>
    private bool IsCard => Section is Section.FirstRun or Section.UpdateCard;

    public bool Listening => _capture.Listening;

    public bool PopupOpen => _popup is not null;

    public bool WantsMeter =>
        (Section == Section.Audio || (Section == Section.FirstRun && _firstRunStep == 1)) && Editor.Settings.Audio.MicEnabled && _status?.MicCapturing == true;

    public void SetCanvas(ICanvas canvas)
    {
        _canvas = canvas;
        _forceEditText = false;
        Refresh();
    }

    // ---------------------------------------------------------------- news from the app

    public void UpdateStatus(AudioEngineStatus status)
    {
        _status = status;
        WakeWave();
        if (!status.MicCapturing)
        {
            _meter.Reset();
        }

        Refresh();
    }

    public void SetLastClip(LastClipInfo? clip)
    {
        _lastClip = clip;
        if (clip is not null)
        {
            _firstRunClipDone = _firstRunStep == 2;
        }

        Refresh();
    }

    public void SetChoices(IReadOnlyList<AudioDeviceChoice> outputs, IReadOnlyList<AudioDeviceChoice> mics, IReadOnlyList<AudioAppChoice> apps)
    {
        _outputs = outputs;
        _mics = mics;
        _apps = apps;
        Refresh();
    }

    public void SetHotkeyResults(IReadOnlyList<HotkeyResult> results)
    {
        _hotkeyProblems.Clear();
        foreach (var result in results)
        {
            if (!result.Ok && result.Message is not null)
            {
                _hotkeyProblems[result.Action] = result.Message;
            }
        }

        Refresh();
    }

    /// <summary>The file was edited by hand and read again: this is now the object to edit.</summary>
    public void ReplaceSettings(AppSettings settings)
    {
        Editor.Settings = settings;
        _clipField.Draft = ClipFieldText();
        _clipField.Message = null;
        _clipField.Entered = null;
        _hexField.Draft = Editor.AccentHex;
        _hexField.Message = null;
        _hexField.Entered = null;
        _templateField.Draft = settings.Export.FileNameTemplate;
        _templateField.Message = null;
        _templateField.Entered = null;
        _customClipOpen = Editor.ClipSegment == SettingsEditor.CustomClip;
        _customAccentOpen = AccentColors.PresetOf(Editor.AccentHex) == AccentPreset.Custom;
        _forceEditText = true;
        Refresh();
    }

    public void MeterTick(float peak)
    {
        var before = _meter.Level;
        _meter.Feed(peak);
        if (Math.Abs(before - _meter.Level) > 0.001f)
        {
            _host.Invalidate();
        }
    }

    // ---------------------------------------------------------------- pages

    public void ShowSection(Section section)
    {
        if (section == Section)
        {
            return;
        }

        // A click on a notification or in the menu can land here in the middle of a capture: the field goes away, and the
        // next key must not become a hotkey nobody sees, with the others still suspended.
        CancelCapture();
        LeaveFocused();
        _popup = null;
        var leftCard = Section == Section.UpdateCard;
        Section = section;
        WakeWave();
        if (section == Section.Now)
        {
            // Nothing read the memory while another page was up: without this, the right of the wave stays empty until the next tick.
            _wave?.ReadAgain();
        }

        _scroll = 0;
        if (NavRows.IsSettingsPage(section))
        {
            SettingsPageSeen?.Invoke(section);
        }

        if (_focusId is not null && _focusId != NavId && !_focusId.StartsWith("band.", StringComparison.Ordinal) && !_focusId.StartsWith("foot.", StringComparison.Ordinal))
        {
            _focusId = NavId;
        }

        // A restart only ever follows a card on screen: whatever takes the window off it, a notification click as much as
        // Cancel, drops the click. After the section changed, so the panel finds the window already off the card.
        if (leftCard)
        {
            _updates?.CancelUpdate();
        }

        Refresh();
    }

    public void ScrollTo(float y)
    {
        _scroll = Math.Clamp(y, 0, MaxScroll);
        SyncEdit();
        _host.Invalidate();
    }

    // ---------------------------------------------------------------- updates

    /// <summary>The Updates section of General draws from this panel. Null, the default, leaves General as it is.</summary>
    public void SetUpdates(IUpdatesPanel? panel)
    {
        _updates = panel;
        if (panel is null && Section == Section.UpdateCard)
        {
            ShowUpdates();
            return;
        }

        Refresh();
    }

    // A click on an update notice or on "Update to X..." only brings the first-launch card back: it goes by its own buttons.
    // Left, its steps would never come back, and the updater, which waits for them, would start at once.
    private bool KeepsFirstRun => Section == Section.FirstRun;

    /// <summary>The panel's view changed: draws the section and the card again, keeping the focus.</summary>
    public void RefreshUpdates() => Refresh();

    /// <summary>Shows General, scrolled to the Updates section. Not over the first-launch card: see <see cref="KeepsFirstRun"/>.</summary>
    public void ShowUpdates()
    {
        if (KeepsFirstRun)
        {
            return;
        }

        ShowSection(Section.General);
        if (_updatesTop is { } top)
        {
            ScrollTo(top - 12 - BandHeight);
        }
    }

    /// <summary>Shows the card that confirms an update. The keyboard lands on its first button.</summary>
    public void ShowUpdateCard()
    {
        if (_updates is null || Section == Section.UpdateCard || KeepsFirstRun)
        {
            return;
        }

        CancelCapture();
        _cardReturnFocus = Section == Section.General ? _focusId : null;
        ShowSection(Section.UpdateCard);
        _focusId = CardStops().FirstOrDefault();
        Refresh();
    }

    /// <summary>Cancel, or Escape: the click no longer stands, and General shows the section again.</summary>
    private void CloseUpdateCard()
    {
        var back = _cardReturnFocus;
        _cardReturnFocus = null;

        // Leaving the card cancels the update.
        ShowUpdates();

        // Back where the card was opened from, when that was the section: Tab goes on from there.
        if (back is not null && Find(back) is { CanFocus: true })
        {
            _focusId = back;
            Refresh();
            Reveal();
        }
    }

    /// <summary>Where Tab goes on a card. The update card keeps the keyboard on its own buttons.</summary>
    private IReadOnlyList<string> CardStops() =>
        Section == Section.UpdateCard ? StopIds().Where(id => id.StartsWith(CardPrefix, StringComparison.Ordinal)).ToList() : StopIds();

    private void OnSettingChanged(SettingsChange change)
    {
        _backend.Applied(change);
        WakeWave();
        Refresh();
    }

    /// <summary>Lays the widgets out again from the current state. Cheap: a page is a few dozen small objects.</summary>
    private void Rebuild()
    {
        _chrome.Clear();
        _page.Clear();
        _stops.Clear();
        BuildBand();
        if (!IsCard)
        {
            BuildNav();
        }

        BuildPage();
        if (!IsCard)
        {
            BuildFoot();
        }

        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    private void Refresh()
    {
        Rebuild();
        if (_focusId is not null && Find(_focusId) is not { CanFocus: true })
        {
            _focusId = IsCard ? CardStops().FirstOrDefault() : NavId;
        }

        SyncEdit();
        _host.MeterWanted(WantsMeter);
        _host.WaveWanted(WantsWave);
        _host.Invalidate();
    }

    private Widget? Find(string? id)
    {
        if (id is null)
        {
            return null;
        }

        foreach (var widget in _stops)
        {
            if (widget.Id == id)
            {
                return widget;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- focus

    /// <summary>Tab and Shift+Tab.</summary>
    public void MoveFocus(bool forward)
    {
        var stops = Section == Section.UpdateCard ? _stops.FindAll(w => w.Id.StartsWith(CardPrefix, StringComparison.Ordinal)) : _stops;
        var current = stops.FindIndex(w => w.Id == _focusId);
        var next = FocusRing.Next(stops.Count, current, forward, i => stops[i].CanFocus);
        if (next >= 0)
        {
            SetFocus(stops[next].Id);
        }
    }

    // By the mouse the choice of Custom is made, and typing comes next. By the keyboard Custom is one stop on the way along
    // the arrows, and Tab goes on to the field.
    private void FocusFieldIfClicked(string id)
    {
        if (_clicking)
        {
            SetFocus(id);
        }
    }

    private void SetFocus(string? id)
    {
        if (id != _focusId)
        {
            LeaveFocused();
            _focusId = id;
            Refresh();
        }

        Reveal();
    }

    // What was being typed or dragged counts as soon as the focus goes elsewhere.
    private void LeaveFocused()
    {
        KeepSlider();
        if (Focus is TextField field)
        {
            field.Commit();
            field.State.Entered = null;
        }
    }

    /// <summary>Brings the focused widget on screen. Tab must never land on something hidden.</summary>
    private void Reveal()
    {
        if (Focus is { Scrolls: true } widget)
        {
            var box = widget.RevealBox;
            if (box.Y - 12 < _scroll + BandHeight)
            {
                _scroll = Math.Clamp(box.Y - 12 - BandHeight, 0, MaxScroll);
            }
            else if (box.Bottom + 12 > _scroll + ViewBottom)
            {
                _scroll = Math.Clamp(box.Bottom + 12 - ViewBottom, 0, MaxScroll);
            }
        }

        SyncEdit();
        _host.Invalidate();
    }

    /// <summary>The native box follows the focus. A field scrolled partly under the band or the foot is only drawn.</summary>
    private void SyncEdit()
    {
        if (_scrolledAwayId != _focusId)
        {
            _scrolledAwayId = null;
        }

        if (Focus is TextField field && OnScreen(field, out var box))
        {
            var wasVisible = _editId is not null;

            // A box only scrolled away still holds the text as typed, with its caret: it must come back as it left.
            var reset = _forceEditText || (_editId ?? _scrolledAwayId) != field.Id;
            _host.ShowEdit(box, field.State.Draft, field.Face, field.Size, reset);
            _editId = field.Id;
            _forceEditText = false;

            // This runs on every piece of news from the app. Only the box appearing may ask for the keyboard.
            if (EditFocus.Wanted(wasVisible, _host.IsForeground))
            {
                _host.FocusEdit();
            }
        }
        else if (_editId is not null)
        {
            _host.HideEdit();
            _scrolledAwayId = _editId == _focusId ? _editId : null;
            _editId = null;
        }
    }

    private bool OnScreen(TextField field, out Box box)
    {
        var inner = field.TextBox;
        box = field.Scrolls ? inner with { Y = inner.Y - _scroll } : inner;
        return !field.Scrolls || (field.Bounds.Y - _scroll >= BandHeight && field.Bounds.Bottom - _scroll <= ViewBottom);
    }

    // ---------------------------------------------------------------- keyboard

    /// <summary>Tabs forward until the focus is on this widget, with the modifiers said here and not read from the real keyboard. False if it never gets there.</summary>
    public bool TabTo(string id)
    {
        const int mostTabs = 40;
        for (var i = 0; i < mostTabs && FocusId != id; i++)
        {
            Key(Vk.Tab, 0);
        }

        return FocusId == id;
    }

    /// <summary>A key went down. Returns false if nothing here wanted it, so Windows can do what it does with it.</summary>
    /// <param name="vk">The virtual key.</param>
    /// <param name="modifiers">The modifiers held, as <see cref="HotkeyGesture"/> flags.</param>
    /// <param name="repeat">The key was already down: this is Windows repeating it.</param>
    public bool Key(uint vk, uint modifiers, bool repeat = false)
    {
        if (_capture.Listening)
        {
            if (!repeat)
            {
                CaptureKey(vk, modifiers);
            }

            return true;
        }

        if (repeat && !KeyRepeat.Acts(vk, Focus is TextField))
        {
            return KeyRepeat.Swallowed(vk);
        }

        if (_popup is { } popup)
        {
            return PopupKey(popup, vk, modifiers);
        }

        if (vk == Vk.Escape && modifiers == 0 && Section == Section.UpdateCard)
        {
            CloseUpdateCard();
            return true;
        }

        if (vk == Vk.Tab && (modifiers & ~HotkeyGesture.ModShift) == 0)
        {
            MoveFocus((modifiers & HotkeyGesture.ModShift) == 0);
            return true;
        }

        // The field was scrolled away with the focus on it: its box is hidden, and this key would go nowhere.
        if (Focus is TextField away && !OnScreen(away, out _) && EditFocus.KeyBringsBack(vk, modifiers))
        {
            Reveal();
        }

        if ((modifiers & (HotkeyGesture.ModControl | HotkeyGesture.ModAlt | HotkeyGesture.ModWin)) != 0)
        {
            return false;
        }

        if (Focus is TextField field)
        {
            return FieldKey(field, vk);
        }

        if (Focus is { Enabled: true } widget && widget.OnKey(vk))
        {
            Reveal();
            return true;
        }

        if (vk is Vk.PageUp or Vk.PageDown)
        {
            ScrollTo(_scroll + (vk == Vk.PageDown ? 1 : -1) * (ViewBottom - BandHeight - ScrollStep));
            return true;
        }

        return false;
    }

    public void KeyUp(uint vk, uint modifiers)
    {
        if (_capture.Listening)
        {
            _capture.KeyUp(modifiers);
            _host.Invalidate();
        }
        else
        {
            Focus?.OnKeyUp(vk);
        }
    }

    private bool FieldKey(TextField field, uint vk)
    {
        switch (vk)
        {
            case Vk.Enter:
                field.Commit();
                Refresh();
                return true;
            case Vk.Escape:
                field.Revert();
                _forceEditText = true;
                if (field.Id == HexFieldId)
                {
                    _customAccentOpen = AccentColors.PresetOf(Editor.AccentHex) == AccentPreset.Custom;
                    if (!_customAccentOpen)
                    {
                        _focusId = SwatchId;
                    }
                }

                Refresh();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The native text box changed.</summary>
    public void EditChanged(string text)
    {
        if (Focus is TextField field)
        {
            field.Typed(text, field.Id switch
            {
                HexFieldId => t => Editor.SetAccentHex(t),
                TemplateFieldId => t => Editor.SetTemplate(t),
                _ => null,
            });
            Refresh();
        }
    }

    // ---------------------------------------------------------------- the hotkey being typed

    private void BeginCapture(HotkeyAction action)
    {
        KeepSlider();
        _attempts.Remove(action);
        _backend.SuspendHotkeys();
        _capture.Begin(action);
        _focusId = "hotkey." + action;
        Refresh();
        Reveal();
    }

    private void CaptureKey(uint vk, uint modifiers)
    {
        var action = _capture.Action!.Value;
        var step = _capture.Key(vk, modifiers);
        switch (step.Kind)
        {
            case CaptureStepKind.Listening:
                _host.Invalidate();
                Refresh();
                break;
            case CaptureStepKind.Cancelled:
                CancelCapture();
                break;
            case CaptureStepKind.CancelledAndMoveOn:
                CancelCapture();
                MoveFocus(forward: true);
                break;
            case CaptureStepKind.Cleared:
                FinishCapture(action, null);
                break;
            case CaptureStepKind.Captured:
                FinishCapture(action, step.Gesture);
                break;
        }
    }

    /// <summary>Whatever ends the listening, the hotkeys come back.</summary>
    private void CancelCapture()
    {
        if (_capture.Listening)
        {
            _capture.End();
            SetHotkeyResults(_backend.ResumeHotkeys());
        }
    }

    // The gesture is only kept once Windows has taken it: while suspended the manager holds nothing, so a refusal means
    // the previous hotkeys must be applied again, or this action would be left with none.
    private void FinishCapture(HotkeyAction action, string? gesture)
    {
        _capture.End();
        if (gesture is not null && Editor.DuplicateOf(action, gesture) is not null)
        {
            _attempts[action] = (gesture, HotkeyResult.DuplicateMessage);
            SetHotkeyResults(_backend.ResumeHotkeys());
            return;
        }

        var tried = Editor.WithGesture(action, gesture);
        _backend.ApplyHotkeys(tried);
        var results = _backend.ResumeHotkeys();
        var mine = results.FirstOrDefault(r => r.Action == action);
        if (mine is { Ok: true })
        {
            _attempts.Remove(action);
            Editor.SetHotkeys(tried);
            SetHotkeyResults(results);
        }
        else
        {
            _attempts[action] = (gesture ?? string.Empty, mine?.Message ?? HotkeyResult.UnavailableMessage);
            SetHotkeyResults(_backend.ApplyHotkeys(Editor.Settings.Hotkeys));
        }
    }

    private void ResetHotkeys()
    {
        CancelCapture();
        _attempts.Clear();
        var defaults = new HotkeySettings();
        var results = _backend.ApplyHotkeys(defaults);
        var main = results.FirstOrDefault(r => r.Action == HotkeyAction.SaveDefault);
        if (main is { Ok: false })
        {
            _attempts[HotkeyAction.SaveDefault] = (defaults.SaveDefault!, main.Message ?? HotkeyResult.InUseMessage);
            defaults.SaveDefault = Editor.GestureOf(HotkeyAction.SaveDefault);
            results = _backend.ApplyHotkeys(defaults);
        }

        Editor.SetHotkeys(defaults);
        SetHotkeyResults(results);
    }

    private HotkeyFieldView ViewOf(HotkeyAction action)
    {
        var listening = _capture.Action == action;
        if (listening)
        {
            return new HotkeyFieldView(null, true, _capture.HeldText(), _capture.Message, false);
        }

        if (_attempts.TryGetValue(action, out var attempt))
        {
            return new HotkeyFieldView(attempt.Gesture.Length > 0 ? attempt.Gesture : null, false, null, attempt.Message, true);
        }

        _hotkeyProblems.TryGetValue(action, out var problem);
        return new HotkeyFieldView(Editor.GestureOf(action), false, null, problem, problem is not null);
    }

    // ---------------------------------------------------------------- slider values

    /// <summary>The value being moved, not yet kept.</summary>
    private double SliderValue(string id, double stored) => _sliding is { } sliding && sliding.Id == id ? sliding.Value : stored;

    private void SliderPreview(string id, double value, Action<double> keep)
    {
        _sliding = (id, value, keep);
        Refresh();
    }

    private void KeepSlider()
    {
        if (_sliding is { } sliding)
        {
            _sliding = null;
            sliding.Keep(sliding.Value);
            Refresh();
        }
    }

    // ---------------------------------------------------------------- the rest of the world

    /// <summary>The window lost the keyboard: nothing is left half done.</summary>
    public void Deactivated()
    {
        CancelCapture();
        KeepSlider();
        if (_popup is not null)
        {
            _popup = null;
            _host.Invalidate();
        }
    }

    public void Closing()
    {
        Deactivated();
        LeaveFocused();
    }

    private void PickFolder()
    {
        if (_host.PickFolder(Editor.FolderShown) is { } folder)
        {
            _folderMessage = Editor.SetFolder(folder).Message;
            Refresh();
        }
    }

    // ---------------------------------------------------------------- mouse

    public void MouseMove(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
        if (_draggingScroll)
        {
            DragScroll(y);
            return;
        }

        if (_popup is { } popup)
        {
            if (popup.Hover(x, y))
            {
                _host.Invalidate();
            }

            return;
        }

        if (Find(_pressedId) is { } pressed)
        {
            pressed.OnDrag(x, LocalY(pressed, y));
            _host.Invalidate();
            return;
        }

        var hover = HitTest(x, y)?.Id;
        if (hover != _hoverId || hover == NavId)
        {
            _hoverId = hover;
            _host.Invalidate();
        }
    }

    public void MouseLeft()
    {
        _mouseX = _mouseY = -1;
        if (_hoverId is not null)
        {
            _hoverId = null;
            _host.Invalidate();
        }
    }

    public void MouseDown(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
        if (_capture.Listening)
        {
            CancelCapture();
        }

        if (_popup is { } popup)
        {
            popup.Press(x, y);
            return;
        }

        if (ScrollThumb() is { } thumb && new Box(thumb.X - 4, BandHeight, thumb.W + 8, ViewBottom - BandHeight).Contains(x, y))
        {
            _draggingScroll = true;
            _dragGrabY = thumb.Contains(x, y) ? y - thumb.Y : thumb.H / 2;
            _host.CaptureMouse();
            DragScroll(y);
            return;
        }

        var widget = HitTest(x, y);
        if (widget is null || !widget.Enabled)
        {
            return;
        }

        // Taking the focus may scroll a half-hidden widget into view, under a mouse that has not moved.
        _pressScroll = _scroll;
        if (widget.CanFocus)
        {
            SetFocus(widget.Id);
            widget = Find(widget.Id) ?? widget;
        }

        _pressedId = widget.Id;
        _host.CaptureMouse();
        _clicking = true;
        widget.OnPress(x, PressY(widget, y));
        _clicking = false;
    }

    public void MouseUp(float x, float y)
    {
        if (_draggingScroll)
        {
            _draggingScroll = false;
            _host.ReleaseMouse();

            // The thumb is lit while it is dragged.
            _host.Invalidate();
            return;
        }

        var pressed = Find(_pressedId);
        _pressedId = null;
        _host.ReleaseMouse();
        pressed?.OnRelease(pressed.Bounds.Contains(x, PressY(pressed, y)));
    }

    /// <summary>The mouse was taken away while a button was held.</summary>
    public void MouseCaptureLost()
    {
        if (_draggingScroll)
        {
            _draggingScroll = false;
            _host.Invalidate();
        }

        if (_pressedId is not null)
        {
            _pressedId = null;
            KeepSlider();
        }
    }

    public void Wheel(int notches)
    {
        if (_popup is { } popup)
        {
            popup.Wheel(notches);
            _host.Invalidate();
            return;
        }

        ScrollTo(_scroll - notches * ScrollStep);
    }

    private float LocalY(Widget widget, float y) => widget.Scrolls ? y + _scroll : y;

    /// <summary>Where the mouse is on a widget, as the page lay when the button went down.</summary>
    private float PressY(Widget widget, float y) => widget.Scrolls ? y + _pressScroll : y;

    private Widget? HitTest(float x, float y)
    {
        foreach (var widget in _chrome)
        {
            if (widget.Bounds.Contains(x, y) && widget.Enabled && widget is not Painted)
            {
                return widget;
            }
        }

        var viewBottom = IsCard ? WindowHeight : ViewBottom;
        if (y < BandHeight || y >= viewBottom)
        {
            return null;
        }

        foreach (var widget in _page)
        {
            if (widget is not Painted && widget.Bounds.Contains(x, LocalY(widget, y)))
            {
                return widget;
            }
        }

        return null;
    }

    private Box? ScrollThumb()
    {
        var max = MaxScroll;
        if (max <= 0)
        {
            return null;
        }

        var track = ViewBottom - BandHeight - 16;
        var height = Math.Max(32, track * (ViewBottom - BandHeight) / (_contentBottom + 24 - BandHeight));
        return new Box(WindowWidth - 12, BandHeight + 8 + (track - height) * (_scroll / max), 6, height);
    }

    private void DragScroll(float y)
    {
        if (ScrollThumb() is { } thumb)
        {
            var track = ViewBottom - BandHeight - 16;
            var share = (y - _dragGrabY - BandHeight - 8) / Math.Max(1, track - thumb.H);
            ScrollTo(share * MaxScroll);
        }
    }

    // ---------------------------------------------------------------- the list that opens

    private void OpenPopup(Dropdown owner)
    {
        KeepSlider();
        _focusId = owner.Id;
        Reveal();
        var box = owner.Scrolls ? owner.Bounds with { Y = owner.Bounds.Y - _scroll } : owner.Bounds;
        _popup = new ListPopup(owner, box, this);
        _host.Invalidate();
    }

    private bool PopupKey(ListPopup popup, uint vk, uint modifiers)
    {
        switch (vk)
        {
            case Vk.Escape:
                ClosePopup();
                break;
            case Vk.Tab:
                ClosePopup();
                MoveFocus((modifiers & HotkeyGesture.ModShift) == 0);
                break;
            case Vk.Enter:
            case Vk.Space:
                popup.PickCurrent();
                break;
            case Vk.Up:
                popup.Move(-1);
                break;
            case Vk.Down:
                popup.Move(1);
                break;
            case Vk.PageUp:
                popup.Move(-ListPopup.MaxVisible);
                break;
            case Vk.PageDown:
                popup.Move(ListPopup.MaxVisible);
                break;
            case Vk.Home:
                popup.Move(-popup.Count);
                break;
            case Vk.End:
                popup.Move(popup.Count);
                break;
            default:
                return false;
        }

        _host.Invalidate();
        return true;
    }

    private void ClosePopup()
    {
        _popup = null;
        _host.Invalidate();
    }

    private sealed class ListPopup
    {
        public const int MaxVisible = 8;
        private const float RowHeight = 28, Pad = 4;

        private readonly Dropdown _owner;
        private readonly IReadOnlyList<DropItem> _items;
        private readonly SettingsScreen _screen;
        private readonly Box _box;
        private int _top;

        public ListPopup(Dropdown owner, Box anchor, SettingsScreen screen)
        {
            _owner = owner;
            _items = owner.Items;
            _screen = screen;
            Index = Math.Max(0, owner.SelectedIndex);
            var rows = Math.Min(MaxVisible, _items.Count);
            var height = rows * RowHeight + 2 * Pad;
            var below = anchor.Bottom + 4;
            var top = below + height <= WindowHeight - 4 ? below : Math.Max(4, anchor.Y - 4 - height);
            _box = new Box(anchor.X, top, anchor.W, height);
            Move(0);
        }

        public int Index { get; private set; }

        public int Count => _items.Count;

        public void Move(int by)
        {
            Index = Math.Clamp(Index + by, 0, Math.Max(0, _items.Count - 1));
            if (Index < _top)
            {
                _top = Index;
            }
            else if (Index >= _top + MaxVisible)
            {
                _top = Index - MaxVisible + 1;
            }
        }

        public void Wheel(int notches) =>
            _top = Math.Clamp(_top - notches, 0, Math.Max(0, _items.Count - MaxVisible));

        public void PickCurrent()
        {
            var index = Index;
            _screen.ClosePopup();
            if (index < _items.Count)
            {
                // What the user saw: the list of the owner may have changed since this one opened.
                _owner.PickValue(_items[index].Value);
            }
        }

        private int RowAt(float x, float y)
        {
            if (!_box.Contains(x, y))
            {
                return -1;
            }

            var row = _top + (int)((y - _box.Y - Pad) / RowHeight);
            return row >= 0 && row < _items.Count ? row : -1;
        }

        public bool Hover(float x, float y)
        {
            var row = RowAt(x, y);
            if (row < 0 || row == Index)
            {
                return false;
            }

            Index = row;
            return true;
        }

        public void Press(float x, float y)
        {
            var row = RowAt(x, y);
            if (row < 0)
            {
                _screen.ClosePopup();
                return;
            }

            Index = row;
            PickCurrent();
        }

        public void Paint(ICanvas c, uint accent)
        {
            c.Frame(_box, 4, Theme.Raised, Theme.Control);
            var selected = _owner.SelectedIndex;
            for (var i = _top; i < Math.Min(_items.Count, _top + MaxVisible); i++)
            {
                var row = new Box(_box.X + 1, _box.Y + Pad + (i - _top) * RowHeight, _box.W - 2, RowHeight);
                if (i == Index)
                {
                    c.FillRect(row, Theme.Hover);
                    c.FillRect(new Box(row.X, row.Y + 5, 3, RowHeight - 10), accent);
                }

                c.TextMid(c.Fit(_items[i].Label, Face.Body, 13, row.W - 24), Face.Body, 13, row.X + 10, row.MidY, i == selected ? Theme.Text : Theme.Muted);
            }

            if (_items.Count > MaxVisible)
            {
                var track = _box.H - 2 * Pad;
                var height = Math.Max(16, track * MaxVisible / _items.Count);
                var y = _box.Y + Pad + (track - height) * _top / (_items.Count - MaxVisible);
                c.FillRound(new Box(_box.Right - 7, y, 4, height), 2, Theme.Control);
            }
        }
    }

    // ---------------------------------------------------------------- painting

    public void Paint(ICanvas c)
    {
        var accent = Editor.AccentRgb;
        var hover = _popup is null ? Find(_hoverId) : null;
        var ui = new UiState(accent, Focus, Find(_pressedId), hover, _mouseX, _mouseY, _scroll, ShowFocus && !_capture.Listening, NoNativeEdit ? null : _editId);
        c.SetOrigin(0);
        c.FillRect(new Box(0, 0, WindowWidth, WindowHeight), Theme.Base);

        // The page first, scrolled; then the band, the side bar and the foot over it: that is all the clipping it takes.
        c.SetOrigin(-_scroll);
        foreach (var widget in _page)
        {
            if (widget.Scrolls)
            {
                widget.Paint(c, ui);
            }
        }

        c.SetOrigin(0);
        foreach (var widget in _page)
        {
            if (!widget.Scrolls)
            {
                widget.Paint(c, ui);
            }
        }

        foreach (var widget in _chrome)
        {
            widget.Paint(c, ui);
        }

        if (ScrollThumb() is { } thumb)
        {
            c.FillRound(thumb, 3, _draggingScroll ? Theme.Muted : Theme.Control);
        }

        _popup?.Paint(c, accent);
    }
}
