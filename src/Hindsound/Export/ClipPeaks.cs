using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// The shape of a clip, computed once from the snapshot: the bars drawn in the notification, and whether the clip is silent.
/// </summary>
/// <param name="Bars">24 values from 0 to 1, the most recent last.</param>
/// <param name="IsSilent">The clip never goes above <see cref="SilenceThresholdDb"/>.</param>
public sealed record ClipPeaks(IReadOnlyList<float> Bars, bool IsSilent)
{
    public const int BarCount = 24;

    /// <summary>A clip whose peak stays under this level is reported as silent.</summary>
    public const double SilenceThresholdDb = -60;

    /// <summary>The level mapped to a full bar (0 dBFS). A bar is empty at <see cref="SilenceThresholdDb"/> or below.</summary>
    private const double FullBarDb = 0;

    /// <summary>
    /// Splits the clip into 24 equal parts. Each bar is the peak of that part across both sources (the louder of
    /// the PC audio and the microphone), on a dB scale from -60 dBFS (0) to 0 dBFS (1). The sources end at the same
    /// instant, so a shorter one is aligned to the end.
    /// </summary>
    public static ClipPeaks Compute(AudioSnapshot snapshot)
    {
        var system = snapshot.System;
        var mic = snapshot.Mic;
        var systemFrames = system.Length / InternalFormat.SystemChannels;
        var micFrames = mic.Length / InternalFormat.MicChannels;
        var frames = Math.Max(systemFrames, micFrames);

        var peaks = new float[BarCount];
        var overall = 0f;

        if (frames > 0)
        {
            var systemOffset = frames - systemFrames;
            var micOffset = frames - micFrames;

            for (var bar = 0; bar < BarCount; bar++)
            {
                var start = (int)((long)bar * frames / BarCount);
                var end = (int)((long)(bar + 1) * frames / BarCount);
                if (end <= start) end = Math.Min(start + 1, frames); // clip shorter than 24 frames: repeat frames

                var peak = 0f;
                for (var f = start; f < end; f++)
                {
                    var s = f - systemOffset;
                    if (s >= 0)
                    {
                        peak = Math.Max(peak, Math.Abs(system[2 * s]));
                        peak = Math.Max(peak, Math.Abs(system[2 * s + 1]));
                    }

                    var m = f - micOffset;
                    if (m >= 0) peak = Math.Max(peak, Math.Abs(mic[m]));
                }

                peaks[bar] = peak;
                overall = Math.Max(overall, peak);
            }
        }

        var bars = new float[BarCount];
        for (var i = 0; i < BarCount; i++) bars[i] = ToBar(peaks[i]);

        var silent = !(overall > 0f) || ToDb(overall) < SilenceThresholdDb;
        return new ClipPeaks(bars, silent);
    }

    private static double ToDb(float linear) => 20 * Math.Log10(linear);

    private static float ToBar(float linear)
    {
        if (!(linear > 0f)) return 0f;
        var db = ToDb(linear);
        var value = (db - SilenceThresholdDb) / (FullBarDb - SilenceThresholdDb);
        return (float)Math.Clamp(value, 0, 1);
    }
}
