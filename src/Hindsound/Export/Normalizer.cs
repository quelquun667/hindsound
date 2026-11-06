namespace Hindsound.Export;

/// <summary>
/// Volume normalization at export. Works on 48 kHz interleaved float audio,
/// after gains and mixing and before encoding.
/// </summary>
public static class Normalizer
{
    /// <summary>The most a clip is ever raised, so that near-silent noise is not amplified to full volume.</summary>
    public const double MaxGainDb = 20;

    /// <summary>A clip whose peak is under this (about -120 dBFS) is treated as silence and left alone.</summary>
    private const float SilentPeak = 1e-6f;

    public static float DbToLinear(double db) => (float)Math.Pow(10, db / 20);

    public static double LinearToDb(double linear) => 20 * Math.Log10(linear);

    /// <summary>
    /// The gain in dB the mode asks for on these samples: 0 for <see cref="NormalizationMode.Off"/>, for a silent clip,
    /// and for a clip too quiet to measure. Never above <see cref="MaxGainDb"/>.
    /// </summary>
    public static double ComputeGainDb(ReadOnlySpan<float> samples, int channels, NormalizationMode mode, double targetLufs, double ceilingDb)
    {
        switch (mode)
        {
            case NormalizationMode.Peak:
                return PeakGainDb(Limiter.Peak(samples), ceilingDb);

            case NormalizationMode.Loudness:
                return LoudnessGainDb(LoudnessMeter.Measure(samples, channels), targetLufs);

            default:
                return 0;
        }
    }

    /// <summary>Gain that brings a peak of <paramref name="peak"/> (linear) to the ceiling.</summary>
    public static double PeakGainDb(float peak, double ceilingDb)
    {
        if (!(peak >= SilentPeak)) return 0;
        return Math.Min(ceilingDb - LinearToDb(peak), MaxGainDb);
    }

    /// <summary>Gain that moves a measured loudness to the target. 0 if nothing was measurable (silence).</summary>
    public static double LoudnessGainDb(double measuredLufs, double targetLufs)
    {
        if (double.IsNegativeInfinity(measuredLufs) || double.IsNaN(measuredLufs)) return 0;
        return Math.Min(targetLufs - measuredLufs, MaxGainDb);
    }

    /// <summary>Multiplies by the gain, then limits so that no sample is above the ceiling. In place.</summary>
    public static void Apply(Span<float> samples, int channels, double gainDb, double ceilingDb)
    {
        ApplyGain(samples, gainDb);
        Limiter.Apply(samples, channels, DbToLinear(ceilingDb));
    }

    public static void ApplyGain(Span<float> samples, double gainDb)
    {
        if (gainDb == 0) return;
        var gain = DbToLinear(gainDb);
        for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
    }

    /// <summary>Measures, applies the gain and the limiter. Returns the gain applied, in dB.</summary>
    public static double Normalize(Span<float> samples, int channels, NormalizationMode mode, double targetLufs, double ceilingDb)
    {
        var gainDb = ComputeGainDb(samples, channels, mode, targetLufs, ceilingDb);
        Apply(samples, channels, gainDb, ceilingDb);
        return gainDb;
    }
}
