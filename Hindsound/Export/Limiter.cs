using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// A look-ahead peak limiter: no sample leaves it above the ceiling, and the gain never jumps.
/// </summary>
/// <remarks>
/// For each frame the gain needed to bring its loudest channel under the ceiling is known. The limiter takes the
/// lowest of those gains over the next 5 ms, then averages that over 5 ms. The result is at or below every needed gain,
/// and it moves by at most 1/240 of its depth per sample, so the gain is already down when a peak arrives and there is
/// no click. It comes back up with a 50 ms time constant. <see cref="StreamingLimiter"/> does this piece by piece with
/// a fixed small memory; <see cref="Apply"/> runs it over a whole buffer.
/// </remarks>
public static class Limiter
{
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

    /// <summary>True if a signal with this peak (linear) needs the limiter to hold the ceiling.</summary>
    public static bool Needed(float peak, float ceilingLinear) =>
        // Rounding in the gain multiplication can leave a peak a few parts per million above the ceiling.
        peak > ceilingLinear * 1.000001f;

    /// <summary>
    /// Brings every sample under <paramref name="ceilingLinear"/>, in place. Returns false if nothing had to be done.
    /// </summary>
    /// <param name="channels">Samples are interleaved; the gain is shared by the channels of a frame.</param>
    public static bool Apply(Span<float> samples, int channels, float ceilingLinear)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (!(ceilingLinear > 0f)) throw new ArgumentOutOfRangeException(nameof(ceilingLinear));

        if (!Needed(Peak(samples), ceilingLinear))
        {
            Clamp(samples, ceilingLinear);
            return false;
        }

        var limiter = new StreamingLimiter(channels, ceilingLinear);
        var emitted = limiter.Process(samples);
        limiter.Flush(samples[(emitted * channels)..]);
        return true;
    }

    /// <summary>Holds every sample within the ceiling, with no smoothing.</summary>
    public static void Clamp(Span<float> samples, float ceilingLinear)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            if (samples[i] > ceilingLinear) samples[i] = ceilingLinear;
            else if (samples[i] < -ceilingLinear) samples[i] = -ceilingLinear;
        }
    }
}

/// <summary>
/// The limiter fed in pieces. Its output is delayed by <see cref="DelayFrames"/> frames (the look-ahead), so each
/// <see cref="Process"/> call returns fewer frames than it was given at the start and <see cref="Flush"/> returns the
/// rest at the end. Memory is a few hundred floats, whatever the length of the clip.
/// </summary>
public sealed class StreamingLimiter
{
    private const double LookAheadSeconds = 0.005;
    private const double ReleaseSeconds = 0.050;

    private readonly int _channels;
    private readonly float _ceiling;
    private readonly int _window;
    private readonly int _capacity;
    private readonly double _releaseCoefficient;

    // Sliding minimum of the needed gain over frames [j, j + window - 1], as a monotonic deque held in two rings.
    private readonly long[] _dequeIndex;
    private readonly float[] _dequeValue;
    private int _head;
    private int _count;

    // The last `window` minima, for the moving average.
    private readonly float[] _minima;
    private double _runningSum;
    private double _released = 1.0;

    // The last `window` input frames, waiting for their gain.
    private readonly float[] _delay;

    private long _step;       // frames fed so far, then flush steps
    private long _framesIn;

    public StreamingLimiter(int channels, float ceilingLinear)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (!(ceilingLinear > 0f)) throw new ArgumentOutOfRangeException(nameof(ceilingLinear));

        _channels = channels;
        _ceiling = ceilingLinear;
        _window = Math.Max(1, (int)(LookAheadSeconds * InternalFormat.SampleRate));
        _releaseCoefficient = 1.0 - Math.Exp(-1.0 / (ReleaseSeconds * InternalFormat.SampleRate));
        _capacity = _window + 1; // indices j - 1 .. j + window - 1 can coexist before the front is trimmed
        _dequeIndex = new long[_capacity];
        _dequeValue = new float[_capacity];
        _minima = new float[_window];
        _delay = new float[_window * channels];
    }

    /// <summary>Frames of delay between input and output (the look-ahead).</summary>
    public int DelayFrames => _window - 1;

    /// <summary>
    /// Limits the frames in place. The output frames are written at the start of the span; the number of frames
    /// written is returned (the first call returns <see cref="DelayFrames"/> fewer than it was given).
    /// </summary>
    public int Process(Span<float> frames)
    {
        var count = frames.Length / _channels;
        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var peak = 0f;
            var slot = (int)(_step % _window) * _channels;
            for (var c = 0; c < _channels; c++)
            {
                var sample = frames[i * _channels + c];
                _delay[slot + c] = sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }

            _framesIn++;
            var needed = peak > _ceiling ? _ceiling / peak : 1f;
            if (Step(hasInput: true, needed, frames.Slice(written * _channels, _channels))) written++;
        }

        return written;
    }

    /// <summary>Writes the last <see cref="DelayFrames"/> frames (fewer for a very short clip). Call once, at the end.</summary>
    public int Flush(Span<float> destination)
    {
        var written = 0;
        while (_step < _framesIn + _window - 1)
        {
            if (Step(hasInput: false, 1f, destination.Slice(written * _channels, _channels))) written++;
        }

        return written;
    }

    /// <summary>One step of the look-ahead. Returns true if it finished a frame, written to <paramref name="output"/>.</summary>
    private bool Step(bool hasInput, float needed, Span<float> output)
    {
        var t = _step++;
        if (hasInput)
        {
            while (_count > 0 && _dequeValue[(_head + _count - 1) % _capacity] >= needed) _count--;
            var tail = (_head + _count) % _capacity;
            _dequeIndex[tail] = t;
            _dequeValue[tail] = needed;
            _count++;
        }

        // Frame j is the first one whose whole look-ahead window has been seen.
        var j = t - _window + 1;
        while (_count > 0 && _dequeIndex[_head] < j)
        {
            _head = (_head + 1) % _capacity;
            _count--;
        }

        var minimum = _count > 0 ? _dequeValue[_head] : 1f;
        var slot = (int)(((j % _window) + _window) % _window);
        if (j >= 1) _runningSum -= _minima[slot];
        _minima[slot] = minimum;
        _runningSum += minimum;

        if (j < 0) return false;

        var smoothed = _runningSum / _window;
        _released = Math.Min(smoothed, _released + (1.0 - _released) * _releaseCoefficient);

        var gain = (float)_released;
        var source = (int)(j % _window) * _channels;
        for (var c = 0; c < _channels; c++)
            output[c] = Math.Clamp(_delay[source + c] * gain, -_ceiling, _ceiling);
        return true;
    }
}
