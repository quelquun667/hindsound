using Hindsound.Audio;

namespace Hindsound.Settings;

/// <summary>The RAM shown in the settings for a given buffer length.</summary>
public static class RamEstimate
{
    /// <summary>
    /// Bytes held by the ring buffers for this duration (PC audio, plus the microphone if active): the audio itself
    /// and the fixed slack each source keeps on top of it (<see cref="SourceTrack.SlackFrames"/>). Zero seconds means
    /// no buffer at all.
    /// </summary>
    public static long Bytes(int bufferSeconds, bool micActive)
    {
        if (bufferSeconds <= 0) return 0;

        var channels = InternalFormat.SystemChannels + (micActive ? InternalFormat.MicChannels : 0);
        var perSecond = (long)InternalFormat.SystemBytesPerSecond + (micActive ? InternalFormat.MicBytesPerSecond : 0);
        var slack = (long)SourceTrack.SlackFrames * channels * sizeof(short);
        return perSecond * bufferSeconds + slack;
    }

    /// <summary>Megabytes (10^6 bytes), e.g. 86.688 for 5:00 with the microphone.</summary>
    public static double Megabytes(int bufferSeconds, bool micActive) => Bytes(bufferSeconds, micActive) / 1_000_000.0;

    /// <summary>Text for the settings, e.g. "~87 MB".</summary>
    public static string Format(int bufferSeconds, bool micActive) =>
        $"~{Math.Round(Megabytes(bufferSeconds, micActive), MidpointRounding.AwayFromZero):0} MB";

    public static double Megabytes(AppSettings settings) => Megabytes(settings.BufferSeconds, settings.Audio.MicEnabled);
}
