using System.Globalization;

namespace Hindsound.Mac.Audio.Playback;

/// <summary>What <c>--prove-preview</c> measures in what it heard: level, peak, exact zeros, dominant frequency, length of sound.</summary>
internal static class ToneMeasure
{
    /// <summary>The samples of one channel.</summary>
    public static float[] Channel(ReadOnlySpan<float> interleaved, int channels, int channel)
    {
        var frames = interleaved.Length / channels;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = interleaved[(i * channels) + channel];
        }

        return samples;
    }

    /// <summary>The RMS level, in dB of full scale; minus infinity for exact silence or nothing.</summary>
    public static double LevelDbfs(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return double.NegativeInfinity;
        }

        double sum = 0;
        foreach (var sample in samples)
        {
            sum += (double)sample * sample;
        }

        return 10 * Math.Log10(sum / samples.Length);
    }

    /// <summary>The highest absolute sample, in dB of full scale; minus infinity for exact silence or nothing.</summary>
    public static double PeakDbfs(ReadOnlySpan<float> samples)
    {
        double peak = 0;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs((double)sample));
        }

        return 20 * Math.Log10(peak);
    }

    public static int ExactZeros(ReadOnlySpan<float> samples)
    {
        var zeros = 0;
        foreach (var sample in samples)
        {
            if (sample == 0)
            {
                zeros++;
            }
        }

        return zeros;
    }

    /// <summary>
    /// The frequency with the most power, from 20 Hz to 20 kHz in steps of 5 Hz, over at most 8 192 samples from the middle,
    /// under a Hann window. 0 for silence or fewer than 256 samples.
    /// </summary>
    public static double Dominant(ReadOnlySpan<float> samples, int rate)
    {
        const int Most = 8192;
        if (samples.Length < 256 || double.IsNegativeInfinity(LevelDbfs(samples)))
        {
            return 0;
        }

        var length = Math.Min(Most, samples.Length);
        var window = samples.Slice((samples.Length - length) / 2, length);
        var weighted = new double[length];
        for (var i = 0; i < length; i++)
        {
            weighted[i] = window[i] * (0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (length - 1))));
        }

        double best = 0;
        double bestPower = -1;
        var top = Math.Min(20000, (rate / 2) - 5);
        for (var hz = 20; hz <= top; hz += 5)
        {
            var coefficient = 2 * Math.Cos(2 * Math.PI * hz / rate);
            double s1 = 0;
            double s2 = 0;
            foreach (var value in weighted)
            {
                var s0 = value + (coefficient * s1) - s2;
                s2 = s1;
                s1 = s0;
            }

            var power = (s1 * s1) + (s2 * s2) - (coefficient * s1 * s2);
            if (power > bestPower)
            {
                bestPower = power;
                best = hz;
            }
        }

        return best;
    }

    /// <summary>How long there was sound: the blocks of 10 ms whose RMS level is above <paramref name="thresholdDb"/>.</summary>
    public static double AudibleSeconds(ReadOnlySpan<float> samples, int rate, double thresholdDb)
    {
        var block = rate / 100;
        var audible = 0;
        for (var start = 0; start + block <= samples.Length; start += block)
        {
            if (LevelDbfs(samples.Slice(start, block)) > thresholdDb)
            {
                audible++;
            }
        }

        return audible / 100.0;
    }

    /// <summary>A level for the log, with a dot: "-6.0", or "-inf".</summary>
    public static string Db(double value) =>
        double.IsNegativeInfinity(value) ? "-inf" : value.ToString("0.0", CultureInfo.InvariantCulture);
}
