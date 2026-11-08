using System.Globalization;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary>What a change of a setting has to reach. The app reads this to know what to apply.</summary>
[Flags]
internal enum SettingsChange
{
    None = 0,
    Hotkeys = 1,
    Clip = 2,
    Buffer = 4,
    Devices = 8,
    SourceApp = 16,
    Export = 32,
    General = 64,
    Accent = 128,
    Startup = 256,
    Updates = 512,
}

/// <summary>Whether a typed value can be kept; if not, the sentence shown next to the field.</summary>
internal readonly record struct FieldCheck(bool Ok, string? Message)
{
    public static FieldCheck Fine => new(true, null);

    public static FieldCheck Bad(string message) => new(false, message);
}

/// <summary>One row of a list: the stored value and what the user reads.</summary>
internal readonly record struct DropItem(string Value, string Label);

/// <summary>
/// Every change the window can make to the settings, and every refusal. No window here: a value that is not valid never
/// reaches <see cref="Settings"/>, and everything else is written and announced at once, as there is no OK button.
/// </summary>
internal sealed class SettingsEditor(AppSettings settings, Action<SettingsChange> changed)
{
    public const int BufferSliderStep = 60;
    public const int BufferSliderMin = 60;
    public const int BufferSliderUsualMax = 600;
    public const int CustomClip = 5;

    public const string CustomClipHelp = "m:ss or seconds, up to 30:00";
    public const string CustomClipError = "Use m:ss or seconds, from 0:01 to 30:00";
    public const string AccentFormatError = "Use a color code like #D4F53C";
    public const string AccentDarkError = "Too dark: it wouldn't show in a game. Pick a lighter color.";
    public const string TemplateEmptyError = "The name can't be empty";
    public const string TemplateCharsError = "A file name can't contain \\ / : * ? \" < > |";
    public const string FolderError = "Pick a full folder path";

    public static readonly int[] ClipPresets = [15, 30, 60, 120, 300];
    public static readonly string[] ClipLabels = ["15s", "30s", "1 min", "2 min", "5 min", "Custom"];
    public static readonly int[] Bitrates = [128, 192, 320];

    /// <summary>The object itself: the app reads the same one. It is replaced when the file was edited by hand.</summary>
    public AppSettings Settings { get; set; } = settings;

    // ---------------------------------------------------------------- durations

    /// <summary>The selected button of the clip length, <see cref="CustomClip"/> for any other length.</summary>
    public int ClipSegment
    {
        get
        {
            var index = Array.IndexOf(ClipPresets, Settings.ClipSeconds);
            return index < 0 ? CustomClip : index;
        }
    }

    /// <summary>Changes the clip; a clip longer than the buffer takes the buffer up with it.</summary>
    public void SetClipSeconds(int seconds)
    {
        seconds = Math.Clamp(seconds, AppSettings.MinClipSeconds, AppSettings.MaxClipSeconds);
        if (seconds == Settings.ClipSeconds)
        {
            return;
        }

        Settings.ClipSeconds = seconds;
        changed(SettingsChange.Clip | RaiseBuffer());
    }

    public static FieldCheck CheckCustomClip(string text) =>
        TryParseDuration(text, out _) ? FieldCheck.Fine : FieldCheck.Bad(CustomClipError);

    public void CommitCustomClip(string text)
    {
        if (TryParseDuration(text, out var seconds))
        {
            SetClipSeconds(seconds);
        }
    }

    /// <summary>"45", "45s", "1:30", "2m", "2 min". Nothing else, and nothing outside what a clip can be.</summary>
    public static bool TryParseDuration(string? text, out int seconds)
    {
        seconds = 0;
        var value = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int parsed;
        var colon = value.IndexOf(':');
        if (colon >= 0)
        {
            if (!Whole(value[..colon], out var minutes) || value.Length - colon - 1 != 2 || !Whole(value[(colon + 1)..], out var rest) || rest > 59 || minutes > AppSettings.MaxClipSeconds / 60)
            {
                return false;
            }

            parsed = minutes * 60 + rest;
        }
        else if (value.EndsWith("min", StringComparison.Ordinal) || value.EndsWith('m'))
        {
            if (!Whole(value.TrimEnd('n', 'i', 'm', ' '), out var minutes) || minutes > AppSettings.MaxClipSeconds / 60)
            {
                return false;
            }

            parsed = minutes * 60;
        }
        else if (!Whole(value.TrimEnd('s', ' '), out parsed))
        {
            return false;
        }

        if (parsed < AppSettings.MinClipSeconds || parsed > AppSettings.MaxClipSeconds)
        {
            return false;
        }

        seconds = parsed;
        return true;
    }

    private static bool Whole(string text, out int value)
    {
        value = 0;
        return text.Length is > 0 and <= 6 && text.All(char.IsAsciiDigit) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The buffer can never go under this: the longest clip that can be asked for.</summary>
    public int BufferFloor => Math.Max(Settings.LongestClipSeconds(), AppSettings.MinClipSeconds);

    /// <summary>1 to 10 minutes in the mockup. A longer clip or a longer stored buffer stretches the scale.</summary>
    public int BufferSliderMax => Math.Min(
        AppSettings.MaxBufferSeconds,
        Math.Max(BufferSliderUsualMax, Math.Max(RoundUpToMinute(Settings.BufferSeconds), RoundUpToMinute(BufferFloor))));

    public int ClampBuffer(int seconds) => Math.Clamp(seconds, BufferFloor, AppSettings.MaxBufferSeconds);

    public void SetBufferSeconds(int seconds)
    {
        seconds = ClampBuffer(seconds);
        if (seconds != Settings.BufferSeconds)
        {
            Settings.BufferSeconds = seconds;
            changed(SettingsChange.Buffer);
        }
    }

    /// <summary>The live text under the slider, for the value being dragged as much as for the stored one.</summary>
    public string RamText(int bufferSeconds) => RamEstimate.Format(bufferSeconds, Settings.Audio.MicEnabled) + " of RAM";

    private static int RoundUpToMinute(int seconds) => (seconds + 59) / 60 * 60;

    private SettingsChange RaiseBuffer()
    {
        if (Settings.BufferSeconds >= BufferFloor)
        {
            return SettingsChange.None;
        }

        Settings.BufferSeconds = Math.Min(BufferFloor, AppSettings.MaxBufferSeconds);
        return SettingsChange.Buffer;
    }

    // ---------------------------------------------------------------- hotkeys

    public static readonly (HotkeyAction Action, string Label)[] HotkeyRows =
    [
        (HotkeyAction.SaveDefault, "Save clip"),
        (HotkeyAction.Save15s, "Save up to 15s"),
        (HotkeyAction.Save1m, "Save up to 1 min"),
        (HotkeyAction.Save5m, "Save up to 5 min"),
        (HotkeyAction.TogglePause, "Pause or resume"),
        (HotkeyAction.CopyLast, "Copy last clip"),
    ];

    public string? GestureOf(HotkeyAction action) => GestureOf(Settings.Hotkeys, action);

    public static string? GestureOf(HotkeySettings hotkeys, HotkeyAction action) => action switch
    {
        HotkeyAction.SaveDefault => hotkeys.SaveDefault,
        HotkeyAction.Save15s => hotkeys.Save15s,
        HotkeyAction.Save1m => hotkeys.Save1m,
        HotkeyAction.Save5m => hotkeys.Save5m,
        HotkeyAction.TogglePause => hotkeys.TogglePause,
        _ => hotkeys.CopyLast,
    };

    /// <summary>A copy of the current hotkeys with one of them changed: what is tried before it is kept.</summary>
    public HotkeySettings WithGesture(HotkeyAction action, string? gesture)
    {
        var current = Settings.Hotkeys;
        var copy = new HotkeySettings
        {
            SaveDefault = current.SaveDefault,
            Save15s = current.Save15s,
            Save1m = current.Save1m,
            Save5m = current.Save5m,
            TogglePause = current.TogglePause,
            CopyLast = current.CopyLast,
        };
        switch (action)
        {
            case HotkeyAction.SaveDefault: copy.SaveDefault = gesture; break;
            case HotkeyAction.Save15s: copy.Save15s = gesture; break;
            case HotkeyAction.Save1m: copy.Save1m = gesture; break;
            case HotkeyAction.Save5m: copy.Save5m = gesture; break;
            case HotkeyAction.TogglePause: copy.TogglePause = gesture; break;
            default: copy.CopyLast = gesture; break;
        }

        return copy;
    }

    /// <summary>The action, other than this one, that already has this gesture.</summary>
    public HotkeyAction? DuplicateOf(HotkeyAction action, string gesture)
    {
        foreach (var (other, _) in HotkeyRows)
        {
            if (other != action && string.Equals(GestureOf(other), gesture, StringComparison.OrdinalIgnoreCase))
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>Keeps hotkeys that Windows accepted. A new "Save 5 min" hotkey can raise the buffer.</summary>
    public void SetHotkeys(HotkeySettings hotkeys)
    {
        Settings.Hotkeys = hotkeys;
        changed(SettingsChange.Hotkeys | RaiseBuffer());
    }

    // ---------------------------------------------------------------- audio

    public void SetSourceApp(string? exeName)
    {
        var value = string.IsNullOrWhiteSpace(exeName) ? null : exeName;
        if (value != Settings.Audio.SourceApp)
        {
            Settings.Audio.SourceApp = value;
            changed(SettingsChange.SourceApp);
        }
    }

    public void SetOutput(string id)
    {
        if (id != Settings.Audio.OutputDeviceId)
        {
            Settings.Audio.OutputDeviceId = id;
            changed(SettingsChange.Devices);
        }
    }

    public void SetMic(string id)
    {
        if (id != Settings.Audio.MicDeviceId)
        {
            Settings.Audio.MicDeviceId = id;
            changed(SettingsChange.Devices);
        }
    }

    public void SetMode(string mode)
    {
        if (mode != Settings.Audio.Mode)
        {
            Settings.Audio.Mode = mode;
            changed(SettingsChange.Export);
        }
    }

    public void SetSystemGain(double db)
    {
        var value = Math.Clamp(db, AudioSettings.MinGainDb, AudioSettings.MaxGainDb);
        if (value != Settings.Audio.SystemGainDb)
        {
            Settings.Audio.SystemGainDb = value;
            changed(SettingsChange.Export);
        }
    }

    public void SetMicGain(double db)
    {
        var value = Math.Clamp(db, AudioSettings.MinGainDb, AudioSettings.MaxGainDb);
        if (value != Settings.Audio.MicGainDb)
        {
            Settings.Audio.MicGainDb = value;
            changed(SettingsChange.Export);
        }
    }

    /// <summary>The list of the "Record" row. An app chosen earlier and not running now stays listed.</summary>
    public static IReadOnlyList<DropItem> RecordChoices(IReadOnlyList<AudioAppChoice> apps, string? selected, bool selectedRunning = false)
    {
        var items = new List<DropItem> { new(string.Empty, "Whole PC") };
        items.AddRange(apps.Select(a => new DropItem(a.ExeName, a.Name)));
        if (!string.IsNullOrEmpty(selected) && !items.Any(i => string.Equals(i.Value, selected, StringComparison.OrdinalIgnoreCase)))
        {
            // An app that has not played anything yet has no session, so it can be running and still not be listed.
            items.Add(new DropItem(selected, selectedRunning ? Path.GetFileNameWithoutExtension(selected) : selected + " (not running)"));
        }

        return items;
    }

    /// <summary>The list of the "Output" row. A device that went away stays listed, rather than changing by itself.</summary>
    public static IReadOnlyList<DropItem> OutputChoices(IReadOnlyList<AudioDeviceChoice> devices, string selected)
    {
        var items = new List<DropItem> { new(AudioSettings.DefaultDevice, "Default output") };
        AddDevices(items, devices, selected);
        return items;
    }

    /// <summary>The list of the "Microphone" row: Default, the devices, Off.</summary>
    public static IReadOnlyList<DropItem> MicChoices(IReadOnlyList<AudioDeviceChoice> devices, string selected)
    {
        var items = new List<DropItem> { new(AudioSettings.DefaultDevice, "Default microphone") };
        var off = new DropItem(AudioSettings.DisabledMic, "Off");
        AddDevices(items, devices, string.Equals(selected, off.Value, StringComparison.OrdinalIgnoreCase) ? off.Value : selected);
        items.RemoveAll(i => i.Value == off.Value);
        items.Add(off);
        return items;
    }

    private static void AddDevices(List<DropItem> items, IReadOnlyList<AudioDeviceChoice> devices, string selected)
    {
        items.AddRange(devices.Select(d => new DropItem(d.Id, d.Name)));
        if (!items.Any(i => i.Value == selected))
        {
            items.Add(new DropItem(selected, "Unplugged device"));
        }
    }

    // ---------------------------------------------------------------- export

    public void SetFormat(string format) => SetExport(Settings.Export.Format != format, () => Settings.Export.Format = format);

    public void SetBitrate(int kbps) => SetExport(Settings.Export.Mp3Bitrate != kbps, () => Settings.Export.Mp3Bitrate = kbps);

    public void SetNormalization(string mode) => SetExport(Settings.Export.Normalization != mode, () => Settings.Export.Normalization = mode);

    public void SetCopyToClipboard(bool on) => SetExport(Settings.Export.CopyToClipboard != on, () => Settings.Export.CopyToClipboard = on);

    /// <summary>The folder as the app will really use it.</summary>
    public string FolderShown => SettingsMapping.OutputFolder(Settings.Export.OutputFolder);

    public FieldCheck SetFolder(string? path)
    {
        var folder = path?.Trim();
        if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(folder)))
        {
            return FieldCheck.Bad(FolderError);
        }

        SetExport(Settings.Export.OutputFolder != folder, () => Settings.Export.OutputFolder = folder);
        return FieldCheck.Fine;
    }

    public static FieldCheck CheckTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return FieldCheck.Bad(TemplateEmptyError);
        }

        if (template.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return FieldCheck.Bad(TemplateCharsError);
        }

        for (var open = template.IndexOf('{'); open >= 0; open = template.IndexOf('{', open + 1))
        {
            var close = template.IndexOf('}', open);
            var tag = close < 0 ? template[open..] : template[open..(close + 1)];
            if (!(tag.Equals("{app}", StringComparison.OrdinalIgnoreCase) || tag.Equals("{date}", StringComparison.OrdinalIgnoreCase) || tag.Equals("{time}", StringComparison.OrdinalIgnoreCase)))
            {
                return FieldCheck.Bad($"Unknown tag {tag}. Use {{app}}, {{date}} or {{time}}");
            }
        }

        return FieldCheck.Fine;
    }

    /// <summary>Keeps the template if it is valid. The text being typed stays with the field either way.</summary>
    public FieldCheck SetTemplate(string template)
    {
        var check = CheckTemplate(template);
        if (check.Ok)
        {
            SetExport(Settings.Export.FileNameTemplate != template, () => Settings.Export.FileNameTemplate = template);
        }

        return check;
    }

    /// <summary>The file a clip would get, for a template as typed. The app name is an example.</summary>
    public string NamePreview(string template, DateTime now) =>
        FileNameTemplate.Render(template, "Discord", now) + (Settings.Export.Format == "wav" ? ".wav" : ".mp3");

    private void SetExport(bool different, Action apply)
    {
        if (different)
        {
            apply();
            changed(SettingsChange.Export);
        }
    }

    // ---------------------------------------------------------------- general

    public static readonly AccentPreset[] AccentOrder = [AccentPreset.Lime, AccentPreset.Mint, AccentPreset.Cyan, AccentPreset.Violet, AccentPreset.Pink, AccentPreset.White];

    public string AccentHex => AccentColors.Resolve(Settings.General.AccentColor);

    public uint AccentRgb
    {
        get
        {
            AccentColors.TryParseHex(AccentHex, out var r, out var g, out var b);
            return ((uint)r << 16) | ((uint)g << 8) | b;
        }
    }

    /// <summary>"Lime", or "Custom" for any colour that is not a preset.</summary>
    public string AccentName => AccentColors.PresetOf(AccentHex).ToString();

    public void SetAccentPreset(AccentPreset preset) => SetAccentValue(AccentColors.Presets[preset]);

    public static FieldCheck CheckAccent(string? text) => AccentColors.Validate(text) switch
    {
        AccentCheck.Valid => FieldCheck.Fine,
        AccentCheck.TooDark => FieldCheck.Bad(AccentDarkError),
        _ => FieldCheck.Bad(AccentFormatError),
    };

    /// <summary>A colour typed by hand: kept and applied only if the check passes.</summary>
    public FieldCheck SetAccentHex(string text)
    {
        var check = CheckAccent(text);
        if (check.Ok)
        {
            SetAccentValue(AccentColors.Normalize(text));
        }

        return check;
    }

    private void SetAccentValue(string hex)
    {
        if (hex != Settings.General.AccentColor)
        {
            Settings.General.AccentColor = hex;
            changed(SettingsChange.Accent);
        }
    }

    public void SetStartWithWindows(bool on)
    {
        if (on != Settings.General.StartWithWindows)
        {
            Settings.General.StartWithWindows = on;
            changed(SettingsChange.Startup);
        }
    }

    public void SetNotifications(bool on) => SetGeneral(Settings.General.Notifications != on, () => Settings.General.Notifications = on);

    public void SetNotificationPosition(string edge) => SetGeneral(Settings.General.NotificationPosition != edge, () => Settings.General.NotificationPosition = edge);

    public void SetFullscreenGames(string mode) => SetGeneral(Settings.General.FullscreenGames != mode, () => Settings.General.FullscreenGames = mode);

    public void SetConfirmSound(string mode) => SetGeneral(Settings.General.ConfirmSound != mode, () => Settings.General.ConfirmSound = mode);

    /// <summary>"auto", "ask" or "off". The updater has to hear of it: it arms or stops its timer.</summary>
    public void SetUpdateMode(string mode)
    {
        if (Settings.General.Updates.Mode != mode)
        {
            Settings.General.Updates.Mode = mode;
            changed(SettingsChange.General | SettingsChange.Updates);
        }
    }

    private void SetGeneral(bool different, Action apply)
    {
        if (different)
        {
            apply();
            changed(SettingsChange.General);
        }
    }
}

/// <summary>An app with a session in the mixer: the name of its exe for the settings, a name for the user.</summary>
internal sealed record AudioAppChoice(string ExeName, string Name);

/// <summary>A device of the lists, apart from the engine's type so the screen stays free of NAudio.</summary>
internal sealed record AudioDeviceChoice(string Id, string Name);
