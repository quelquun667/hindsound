using System.IO;

namespace Hindsound.Export;

public enum ExportFormat
{
    Wav,
    Mp3,
}

public enum NormalizationMode
{
    /// <summary>No normalization. A mix that would clip is still held under 0 dBFS.</summary>
    Off,

    /// <summary>Scale so the loudest sample reaches the ceiling.</summary>
    Peak,

    /// <summary>Scale to a target integrated loudness (ITU-R BS.1770), then limit to the ceiling.</summary>
    Loudness,
}

public enum TrackMode
{
    /// <summary>PC audio and microphone summed into one stereo file.</summary>
    Mix,

    /// <summary>Two files of the same duration: <c>_system</c> (stereo) and <c>_mic</c> (mono).</summary>
    MultiTrack,
}

/// <summary>Everything the export needs to know. Independent of the settings file: the caller maps its settings onto this.</summary>
public sealed record ExportOptions
{
    public const string DefaultFileNameTemplate = "Hindsound_{app}_{date}_{time}";

    public ExportFormat Format { get; init; } = ExportFormat.Mp3;

    /// <summary>128, 192 or 320. Ignored for WAV.</summary>
    public int Mp3BitrateKbps { get; init; } = 192;

    public NormalizationMode Normalization { get; init; } = NormalizationMode.Loudness;

    /// <summary>Integrated loudness aimed at by <see cref="NormalizationMode.Loudness"/>.</summary>
    public double TargetLufs { get; init; } = -16;

    /// <summary>No sample peak goes above this level after normalization.</summary>
    public double PeakCeilingDb { get; init; } = -1;

    public double SystemGainDb { get; init; }

    public double MicGainDb { get; init; }

    public TrackMode TrackMode { get; init; } = TrackMode.Mix;

    /// <summary>
    /// Multitrack only: normalize both tracks with the same gain, measured on their mix, so their balance is kept.
    /// If false, each track is normalized on its own.
    /// </summary>
    public bool LinkedTrackGain { get; init; } = true;

    /// <summary>May contain environment variables such as <c>%USERPROFILE%</c>.</summary>
    public string OutputFolder { get; init; } = DefaultOutputFolder();

    public string FileNameTemplate { get; init; } = DefaultFileNameTemplate;

    public static string DefaultOutputFolder() =>
        Hindsound.Settings.UserFolders.Under(Environment.SpecialFolder.MyMusic, "Hindsound");
}
