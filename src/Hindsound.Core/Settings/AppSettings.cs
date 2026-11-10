using System.Text.Json.Serialization;
using Hindsound.Input;

namespace Hindsound.Settings;

/// <summary>Everything stored in settings.json, with its default values.</summary>
public sealed class AppSettings
{
    public const int MinClipSeconds = 1;
    public const int MaxClipSeconds = 1800;
    public const int MaxBufferSeconds = 1800;
    public const int DefaultClipSeconds = 30;
    public const int DefaultBufferSeconds = 300;

    public HotkeySettings Hotkeys { get; set; } = new();

    public int ClipSeconds { get; set; } = DefaultClipSeconds;

    public int BufferSeconds { get; set; } = DefaultBufferSeconds;

    public AudioSettings Audio { get; set; } = new();

    public ExportSettings Export { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    /// <summary>The longest clip any enabled shortcut or the default duration can ask for.</summary>
    public int LongestClipSeconds()
    {
        var longest = ClipSeconds;
        if (!string.IsNullOrWhiteSpace(Hotkeys.Save15s)) longest = Math.Max(longest, 15);
        if (!string.IsNullOrWhiteSpace(Hotkeys.Save1m)) longest = Math.Max(longest, 60);
        if (!string.IsNullOrWhiteSpace(Hotkeys.Save5m)) longest = Math.Max(longest, 300);
        return longest;
    }

    /// <summary>
    /// Brings every value back into its allowed range (in place). Unknown or invalid values fall back to their default.
    /// The buffer is never shorter than the longest clip.
    /// </summary>
    public AppSettings Normalize()
    {
        Hotkeys ??= new();
        Audio ??= new();
        Export ??= new();
        General ??= new();

        Hotkeys.Normalize();
        ClipSeconds = Math.Clamp(ClipSeconds, MinClipSeconds, MaxClipSeconds);
        BufferSeconds = Math.Clamp(Math.Max(BufferSeconds, LongestClipSeconds()), MinClipSeconds, MaxBufferSeconds);
        Audio.Normalize();
        Export.Normalize();
        General.Normalize();
        return this;
    }

    internal static string OneOf(string? value, string fallback, params string[] allowed)
    {
        foreach (var option in allowed)
        {
            if (string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return fallback;
    }
}

/// <summary>Shortcut texts such as "Ctrl+Shift+F10"; null means no shortcut.</summary>
public sealed class HotkeySettings
{
    public const string DefaultSaveShortcut = "Ctrl+Shift+F10";

    public string? SaveDefault { get; set; } = DefaultSaveShortcut;

    public string? Save15s { get; set; }

    public string? Save1m { get; set; }

    public string? Save5m { get; set; }

    public string? TogglePause { get; set; }

    public string? CopyLast { get; set; }

    internal void Normalize()
    {
        SaveDefault = Clean(SaveDefault) ?? DefaultSaveShortcut;
        Save15s = Clean(Save15s);
        Save1m = Clean(Save1m);
        Save5m = Clean(Save5m);
        TogglePause = Clean(TogglePause);
        CopyLast = Clean(CopyLast);
    }

    // Canonical text if valid, otherwise null (an unreadable shortcut is dropped rather than kept as garbage).
    private static string? Clean(string? text) =>
        HotkeyGesture.TryParse(text, out var gesture) ? gesture.ToString() : null;
}

public sealed class AudioSettings
{
    public const string DefaultDevice = "default";
    public const string DisabledMic = "disabled";
    public const double MinGainDb = -60;
    public const double MaxGainDb = 24;

    public string OutputDeviceId { get; set; } = DefaultDevice;

    /// <summary>The name of the exe of the only app to record, or null for the whole PC.</summary>
    public string? SourceApp { get; set; }

    /// <summary>"default", a device id, or "disabled" for no microphone.</summary>
    public string MicDeviceId { get; set; } = DefaultDevice;

    /// <summary>"mix" (one file) or "multitrack" (PC sound and microphone apart).</summary>
    public string Mode { get; set; } = "mix";

    public double SystemGainDb { get; set; }

    public double MicGainDb { get; set; }

    public bool MicEnabled => !string.Equals(MicDeviceId, DisabledMic, StringComparison.OrdinalIgnoreCase);

    internal void Normalize()
    {
        OutputDeviceId = string.IsNullOrWhiteSpace(OutputDeviceId) ? DefaultDevice : OutputDeviceId;
        SourceApp = string.IsNullOrWhiteSpace(SourceApp) ? null : SourceApp.Trim();
        MicDeviceId = string.IsNullOrWhiteSpace(MicDeviceId) ? DefaultDevice : MicDeviceId;
        Mode = AppSettings.OneOf(Mode, "mix", "mix", "multitrack");
        SystemGainDb = ClampGain(SystemGainDb);
        MicGainDb = ClampGain(MicGainDb);
    }

    private static double ClampGain(double db) => double.IsFinite(db) ? Math.Clamp(db, MinGainDb, MaxGainDb) : 0;
}

public sealed class ExportSettings
{
    /// <summary>
    /// The stored spelling of "the Music folder of the user, in a folder of ours": the app reads it as the default whatever
    /// system it is on. The separator is the one of the system, or a settings file would hold a path of another.
    /// </summary>
    public static string DefaultFolder { get; } = OperatingSystem.IsWindows() ? "%USERPROFILE%\\Music\\Hindsound" : "%HOME%/Music/Hindsound";
    public const string DefaultTemplate = "Hindsound_{app}_{date}_{time}";
    public const int MaxKnownClipFolders = 8;

    /// <summary>"mp3" or "wav".</summary>
    public string Format { get; set; } = "mp3";

    /// <summary>128, 192 or 320.</summary>
    public int Mp3Bitrate { get; set; } = 192;

    /// <summary>"off", "peak" or "loudness".</summary>
    public string Normalization { get; set; } = "loudness";

    public double TargetLufs { get; set; } = -16;

    public double PeakCeilingDb { get; set; } = -1;

    public string OutputFolder { get; set; } = DefaultFolder;

    /// <summary>
    /// The earlier output folders, as they were stored, most recent first: their clips stay in the library. Null when
    /// there is none, so that the key only enters the file once a folder has really been left.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? KnownClipFolders { get; set; }

    public string FileNameTemplate { get; set; } = DefaultTemplate;

    public bool CopyToClipboard { get; set; } = true;

    internal void Normalize()
    {
        Format = AppSettings.OneOf(Format, "mp3", "mp3", "wav");
        Mp3Bitrate = Mp3Bitrate is 128 or 192 or 320 ? Mp3Bitrate : 192;
        Normalization = AppSettings.OneOf(Normalization, "loudness", "off", "peak", "loudness");
        TargetLufs = double.IsFinite(TargetLufs) ? Math.Clamp(TargetLufs, -30, -5) : -16;
        PeakCeilingDb = double.IsFinite(PeakCeilingDb) ? Math.Clamp(PeakCeilingDb, -20, 0) : -1;
        OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? DefaultFolder : OutputFolder;
        KnownClipFolders = CleanKnownFolders(KnownClipFolders);
        FileNameTemplate = string.IsNullOrWhiteSpace(FileNameTemplate) ? DefaultTemplate : FileNameTemplate;
    }

    private static List<string>? CleanKnownFolders(List<string>? folders)
    {
        if (folders is null) return null;

        var clean = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim())
            .Distinct(Library.PathRules.Comparer)
            .Take(MaxKnownClipFolders)
            .ToList();
        return clean.Count == 0 ? null : clean;
    }
}

public sealed class GeneralSettings
{
    /// <summary>Off until the user asks: an app that records audio should not start itself at boot on its own decision.</summary>
    public bool StartWithWindows { get; set; }

    public bool Notifications { get; set; } = true;

    /// <summary>"#RRGGBB". Anything invalid or too dark falls back to Lime.</summary>
    public string AccentColor { get; set; } = AccentColors.LimeHex;

    /// <summary>"right" or "left".</summary>
    public string NotificationPosition { get; set; } = "right";

    /// <summary>"auto" or "soundOnly".</summary>
    public string FullscreenGames { get; set; } = "auto";

    /// <summary>"whenNoOverlay", "always" or "never".</summary>
    public string ConfirmSound { get; set; } = "whenNoOverlay";

    public UpdateSettings Updates { get; set; } = new();

    internal void Normalize()
    {
        AccentColor = AccentColors.Resolve(AccentColor);
        NotificationPosition = AppSettings.OneOf(NotificationPosition, "right", "right", "left");
        FullscreenGames = AppSettings.OneOf(FullscreenGames, "auto", "auto", "soundOnly");
        ConfirmSound = AppSettings.OneOf(ConfirmSound, "whenNoOverlay", "whenNoOverlay", "always", "never");
        Updates ??= new();
        Updates.Normalize();
    }
}

/// <summary>How the app keeps itself up to date. A file without this key has never been asked: it updates by itself.</summary>
public sealed class UpdateSettings
{
    public const string Auto = "auto", Ask = "ask", Off = "off";

    /// <summary>"auto" (install automatically), "ask" (ask me) or "off".</summary>
    public string Mode { get; set; } = Auto;

    /// <summary>"X.Y.Z", or null.</summary>
    public string? SkippedVersion { get; set; }

    internal void Normalize()
    {
        // Anything unreadable means no network: a hand-written "never" must not reach GitHub. Unlike the other values of
        // the file, which fall back to their default.
        Mode = AppSettings.OneOf(Mode, Off, Auto, Ask, Off);
        SkippedVersion = Hindsound.Updates.UpdateVersion.TryParse(SkippedVersion, out _) ? SkippedVersion : null;
    }
}
