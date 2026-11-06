using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// An offline look-ahead peak limiter: no sample leaves it above the ceiling, and the gain never jumps.
/// </summary>
/// <remarks>
/// For each frame the gain needed to bring its loudest channel under the ceiling is known. The limiter takes the
/// lowest of those gains over the next 5 ms, then averages that over 5 ms. The result is at or below every needed gain,
/// and it moves by at most 1/240 of its depth per sample, so the gain is already down when a peak arrives and there is
/// no click. It comes back up with a 50 ms time constant. The whole clip is in memory, so there is no delay to
/// compensate and nothing is allocated beyond a few small rings.
/// </remarks>
public static class Limiter
{
    private const double LookAheadSeconds = 0.005;
    private const double ReleaseSeconds = 0.050;

    /// <summary>Returns the largest absolute sample value.</summary>
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var s in samples)
        {
            var a = Math.Abs(s);
            if (a > peak) peak = a;
        }

        return peak;
    }

    /// <summary>
    /// Brings every sample under <paramref name="ceilingLinear"/>, in place. Returns false if nothing had to be done.
    /// </summary>
    /// <param name="channels">Samples are interleaved; the gain is shared by the channels of a frame.</param>
    public static bool Apply(Span<float> samples, int channels, float ceilingLinear)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (!(ceilingLinear > 0f)) throw new ArgumentOutOfRangeException(nameof(ceilingLinear));

        // Rounding in the gain multiplication can leave a peak a few parts per million above the ceiling.
        if (Peak(samples) <= ceilingLinear * 1.000001f)
        {
            Clamp(samples, ceilingLinear);
            return false;
        }

        var frames = samples.Length / channels;
        var window = Math.Max(1, (int)(LookAheadSeconds * InternalFormat.SampleRate));
        var releaseCoefficient = 1.0 - Math.Exp(-1.0 / (ReleaseSeconds * InternalFormat.SampleRate));

        // Sliding minimum of the needed gain over frames [j, j + window - 1], as a monotonic deque held in two rings.
        var capacity = window + 1; // indices j - 1 .. j + window - 1 can coexist before the front is trimmed
        var dequeIndex = new int[capacity];
        var dequeValue = new float[capacity];
        var head = 0;
        var count = 0;

        // The last `window` minima, for the moving average.
        var minima = new float[window];
        var runningSum = 0.0;
        var released = 1.0;

        for (var t = 0; t < frames + window - 1; t++)
        {
            var needed = 1f;
            if (t < frames)
            {
                var peak = 0f;
                for (var c = 0; c < channels; c++) peak = Math.Max(peak, Math.Abs(samples[t * channels + c]));
                if (peak > ceilingLinear) needed = ceilingLinear / peak;

                while (count > 0 && dequeValue[(head + count - 1) % capacity] >= needed) count--;
                var tail = (head + count) % capacity;
                dequeIndex[tail] = t;
                dequeValue[tail] = needed;
                count++;
            }

            // Frame j is the first one whose whole look-ahead window has been seen.
            var j = t - window + 1;
            while (count > 0 && dequeIndex[head] < j)
            {
                head = (head + 1) % capacity;
                count--;
            }

            var minimum = count > 0 ? dequeValue[head] : 1f;
            var slot = ((j % window) + window) % window;
            if (j >= 1) runningSum -= minima[slot];
            minima[slot] = minimum;
            runningSum += minimum;

            if (j < 0) continue;

            var smoothed = runningSum / window;
            released = Math.Min(smoothed, released + (1.0 - released) * releaseCoefficient);

            var gain = (float)released;
            for (var c = 0; c < channels; c++)
            {
                var index = j * channels + c;
                samples[index] = Math.Clamp(samples[index] * gain, -ceilingLinear, ceilingLinear);
            }
        }

        return true;
    }

    private static void Clamp(Span<float> samples, float ceilingLinear)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            if (samples[i] > ceilingLinear) samples[i] = ceilingLinear;
            else if (samples[i] < -ceilingLinear) samples[i] = -ceilingLinear;
        }
    }
}
