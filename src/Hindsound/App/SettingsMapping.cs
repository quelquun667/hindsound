using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.App;

/// <summary>
/// The one translation from the settings file to what the engine and the export expect. The file stores "default" and
/// "disabled"; the engine expects null and a boolean, and a device id of "default" would make it capture nothing, with
/// no error at all. So nothing else reads these values directly.
/// </summary>
internal static class SettingsMapping
{
    public static AudioEngineOptions ToEngineOptions(AppSettings settings)
    {
        var devices = ToDevices(settings.Audio);
        return new AudioEngineOptions(
            TimeSpan.FromSeconds(Math.Max(1, settings.BufferSeconds)),
            devices.MicEnabled,
            devices.SystemDeviceId,
            devices.MicDeviceId,
            SourceApp(settings.Audio.SourceApp));
    }

    /// <summary>What the running app hands to the engine: <c>--no-mic</c> wins over the file, and nothing of it reaches the settings.</summary>
    public static AudioEngineOptions ToEngineOptions(AppSettings settings, AppOptions options)
    {
        var engine = ToEngineOptions(settings);
        return options.NoMic ? engine with { MicEnabled = false, MicDeviceId = null } : engine;
    }

    /// <summary>Whether a new clip asks for the clipboard: <c>--no-clipboard</c> wins over the file, and nothing of it reaches the settings.</summary>
    public static bool CopyWanted(AppSettings settings, AppOptions options) => !options.NoClipboard && settings.Export.CopyToClipboard;

    /// <summary>The one way to the clipboard. With <c>--no-clipboard</c> the attempt never runs, and the copy simply did not happen.</summary>
    public static void CopyToClipboard(AppOptions options, Func<bool> attempt, Action<int, Action> after, Action<bool> done)
    {
        if (options.NoClipboard)
        {
            done(false);
            return;
        }

        ClipboardFiles.CopyWithRetries(attempt, after, done);
    }

    /// <summary>The devices alone: what <see cref="AudioEngine.SetDevicesAsync"/> takes.</summary>
    public static (string? SystemDeviceId, string? MicDeviceId, bool MicEnabled) ToDevices(AudioSettings audio)
    {
        var mic = Clean(audio.MicDeviceId);
        var micEnabled = !string.Equals(mic, AudioSettings.DisabledMic, StringComparison.OrdinalIgnoreCase);
        return (DeviceId(audio.OutputDeviceId), micEnabled ? DeviceId(mic) : null, micEnabled);
    }

    public static ExportOptions ToExportOptions(AppSettings settings)
    {
        var export = settings.Export;
        return new ExportOptions
        {
            Format = ParseEnum(export.Format, ExportFormat.Mp3),
            Mp3BitrateKbps = export.Mp3Bitrate is 128 or 192 or 320 ? export.Mp3Bitrate : 192,
            Normalization = ParseEnum(export.Normalization, NormalizationMode.Loudness),
            TargetLufs = Finite(export.TargetLufs, -16),
            PeakCeilingDb = Finite(export.PeakCeilingDb, -1),
            SystemGainDb = Finite(settings.Audio.SystemGainDb, 0),
            MicGainDb = Finite(settings.Audio.MicGainDb, 0),
            TrackMode = ParseEnum(settings.Audio.Mode, TrackMode.Mix),
            OutputFolder = OutputFolder(export.OutputFolder),
            FileNameTemplate = string.IsNullOrWhiteSpace(export.FileNameTemplate) ? ExportOptions.DefaultFileNameTemplate : export.FileNameTemplate,
        };
    }

    /// <summary>
    /// The stored default names %USERPROFILE%\Music, which is not where a Music folder moved to OneDrive or to another
    /// drive really is. So the default resolves to the real Music folder; any other value is the user's own choice.
    /// </summary>
    public static string OutputFolder(string? stored)
    {
        var folder = Clean(stored);
        if (folder.Length == 0 || IsDefaultFolder(folder))
        {
            return ExportOptions.DefaultOutputFolder();
        }

        return Environment.ExpandEnvironmentVariables(folder);
    }

    private static bool IsDefaultFolder(string folder) =>
        string.Equals(folder.Replace('/', '\\').TrimEnd('\\'), ExportSettings.DefaultFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>The mode the updater runs on. The settings have been normalized, so anything else is a value set in code: no network.</summary>
    public static UpdateMode ToUpdateMode(GeneralSettings general) => general.Updates?.Mode switch
    {
        UpdateSettings.Auto => UpdateMode.Auto,
        UpdateSettings.Ask => UpdateMode.Ask,
        _ => UpdateMode.Off,
    };

    /// <summary>The version the user chose to skip, or null.</summary>
    public static UpdateVersion? ToSkipped(GeneralSettings general) =>
        UpdateVersion.TryParse(general.Updates?.SkippedVersion, out var version) ? version : null;

    private static string? DeviceId(string? stored)
    {
        var id = Clean(stored);
        return id.Length == 0 || string.Equals(id, AudioSettings.DefaultDevice, StringComparison.OrdinalIgnoreCase) ? null : id;
    }

    private static string? SourceApp(string? stored)
    {
        var app = Clean(stored);
        return app.Length == 0 ? null : app;
    }

    private static string Clean(string? text) => text?.Trim() ?? string.Empty;

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    // Enum.TryParse would also take a number written as text: only a name counts.
    private static T ParseEnum<T>(string? text, T fallback)
        where T : struct, Enum
    {
        var name = Clean(text);
        return name.Length > 0 && char.IsAsciiLetter(name[0]) && Enum.TryParse<T>(name, ignoreCase: true, out var value) ? value : fallback;
    }
}
