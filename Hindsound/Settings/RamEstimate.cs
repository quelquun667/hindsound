using Hindsound.Audio;

namespace Hindsound.Settings;

/// <summary>The RAM shown in the settings for a given buffer length.</summary>
public static class RamEstimate
{
    /// <summary>Bytes held by the ring buffers for this duration (PC audio, plus the microphone if active).</summary>
    public static long Bytes(int bufferSeconds, bool micActive)
    {
        var perSecond = (long)InternalFormat.SystemBytesPerSecond + (micActive ? InternalFormat.MicBytesPerSecond : 0);
        return perSecond * Math.Max(0, bufferSeconds);
    }

    /// <summary>Megabytes (10^6 bytes), e.g. 86.4 for 5:00 with the microphone.</summary>
    public static double Megabytes(int bufferSeconds, bool micActive) => Bytes(bufferSeconds, micActive) / 1_000_000.0;

    /// <summary>Text for the settings, e.g. "~86 MB".</summary>
    public static string Format(int bufferSeconds, bool micActive) =>
        $"~{Math.Round(Megabytes(bufferSeconds, micActive), MidpointRounding.AwayFromZero):0} MB";

    public static double Megabytes(AppSettings settings) => Megabytes(settings.BufferSeconds, settings.Audio.MicEnabled);
}
