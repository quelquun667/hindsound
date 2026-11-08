using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A window with nothing behind it: a real screen and a real hotkey manager over a fake native layer.</summary>
internal sealed class ScreenRig : IDisposable
{
    public ScreenRig(AppSettings? settings = null, bool firstRun = false)
    {
        Settings = settings ?? new AppSettings().Normalize();
        Native = new FakeHotkeys();
        Manager = new HotkeyManager(Native);
        Backend = new FakeBackend(this);
        Host = new FakeHost();
        Manager.Apply(Settings.Hotkeys);
        Screen = new SettingsScreen(Settings, Backend, Host, Canvas, firstRun);
    }

    public AppSettings Settings { get; }

    public FakeCanvas Canvas { get; } = new();

    public FakeHotkeys Native { get; }

    public HotkeyManager Manager { get; }

    public FakeBackend Backend { get; }

    public FakeHost Host { get; }

    public SettingsScreen Screen { get; }

    public void Press(params uint[] keys)
    {
        foreach (var key in keys)
        {
            Screen.Key(key, 0);
            Screen.KeyUp(key, 0);
        }
    }

    /// <summary>Tabs until the focus is on this widget.</summary>
    public void FocusOn(string id)
    {
        for (var i = 0; i < 40 && Screen.FocusId != id; i++)
        {
            Screen.Key(Vk.Tab, 0);
        }

        Assert.Equal(id, Screen.FocusId);
    }

    public void Dispose() => Manager.Dispose();

    public static AudioEngineStatus Capturing(bool micCapturing = true, CaptureActivity activity = CaptureActivity.Capturing, bool micEnabled = true) =>
        new(activity, true, micEnabled, micCapturing, "Speakers", "Headset", true, TimeSpan.FromSeconds(62), 86_688_000);
}

internal sealed class FakeCanvas : ICanvas
{
    public void SetOrigin(float dy) { }
    public void FillRect(Box box, uint rgb) { }
    public void FillRound(Box box, float radius, uint rgb) => LastRoundFill = rgb;
    public void Frame(Box box, float radius, uint fill, uint border) { }
    public void Ring(Box box, float radius, float thickness, uint rgb) { }
    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Strokes.Add((width, rgb));
    public void Glow(Box box, float sigma, float opacity, uint rgb) { }

    public void Text(string text, Face face, float size, float x, float top, uint rgb)
    {
        Drawn.Add(text);
        DrawnAt.Add((text, x));
        TextColors[text] = rgb;
    }

    public float Measure(string text, Face face, float size) => text.Length * size * 0.5f;
    public float LineHeight(Face face, float size) => size * 1.3f;
    public void WarningIcon(float x, float y, float size, uint rgb) { }
    public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => NotificationAccent = accent;

    public List<string> Drawn { get; } = [];

    /// <summary>Each text with the left edge it was drawn at.</summary>
    public List<(string Text, float X)> DrawnAt { get; } = [];

    /// <summary>The colour each text was last drawn in.</summary>
    public Dictionary<string, uint> TextColors { get; } = [];

    public List<(float Width, uint Rgb)> Strokes { get; } = [];

    public uint? NotificationAccent { get; private set; }

    /// <summary>The colour of the last rounded shape: the scroll thumb, on a page that has one and no list open.</summary>
    public uint LastRoundFill { get; private set; }
}

internal sealed class FakeHost : IWindowHost
{
    public int Invalidated { get; private set; }

    public string? EditText { get; private set; }

    /// <summary>How many times the box was told to take a text, which also loses the caret.</summary>
    public int EditResets { get; private set; }

    public bool EditVisible { get; private set; }

    public Box EditBox { get; private set; }

    public bool MeterOn { get; private set; }

    public string? FolderToPick { get; set; }

    public bool MouseHeld { get; private set; }

    public void Invalidate() => Invalidated++;

    public void ShowEdit(Box box, string text, Face face, float size, bool resetText)
    {
        EditBox = box;
        EditVisible = true;
        if (resetText)
        {
            EditText = text;
            EditResets++;
        }
    }

    public void HideEdit() => EditVisible = false;

    /// <summary>The window is the foreground one. The tests that care turn it off.</summary>
    public bool IsForeground { get; set; } = true;

    /// <summary>How many times the screen asked for the keyboard.</summary>
    public int FocusRequests { get; private set; }

    public void FocusEdit() => FocusRequests++;

    public void CaptureMouse() => MouseHeld = true;

    public void ReleaseMouse() => MouseHeld = false;

    public void MeterWanted(bool wanted) => MeterOn = wanted;

    public string? PickFolder(string startFolder) => FolderToPick;

    public void CloseWindow() { }
}

internal sealed class FakeBackend(ScreenRig rig) : ISettingsBackend
{
    public List<SettingsChange> Changes { get; } = [];

    public int Saves { get; private set; }

    public int Pauses { get; private set; }

    public int Copies { get; private set; }

    public int Folders { get; private set; }

    public int Tests { get; private set; }

    public int Suspends { get; private set; }

    public int Resumes { get; private set; }

    public SettingsChange AllChanges => Changes.Aggregate(SettingsChange.None, (a, b) => a | b);

    public void Applied(SettingsChange change) => Changes.Add(change);

    public void SaveNow() => Saves++;

    public void TogglePause() => Pauses++;

    public void CopyLast() => Copies++;

    public void OpenFolder() => Folders++;

    public void ShowTestNotification() => Tests++;

    public void SuspendHotkeys()
    {
        Suspends++;
        rig.Manager.Suspend();
    }

    public IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys) => rig.Manager.Apply(hotkeys);

    public IReadOnlyList<HotkeyResult> ResumeHotkeys()
    {
        Resumes++;
        return rig.Manager.Resume();
    }
}

/// <summary>Like RegisterHotKey: a combination already held, or held by "another app", is refused.</summary>
internal sealed class FakeHotkeys : IHotkeyNative
{
    public HashSet<(uint Mods, uint Key)> OtherApps { get; } = [];

    public Dictionary<int, (uint Mods, uint Key)> Registered { get; } = [];

    public event Action<int>? HotkeyPressed { add { } remove { } }

    public bool Holds(uint mods, uint key) => Registered.Values.Contains((mods, key));

    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        if (OtherApps.Contains((modifiers, virtualKey)) || Holds(modifiers, virtualKey))
        {
            return false;
        }

        Registered[id] = (modifiers, virtualKey);
        return true;
    }

    public void Unregister(int id) => Registered.Remove(id);

    public void Dispose() => Registered.Clear();
}
