namespace Hindsound.Audio;

/// <summary>
/// A streaming windowed-sinc resampler for interleaved frames. It exists because the callback must not allocate:
/// NAudio's WdlResampler resizes its buffer whenever the amount it holds changes, which is at nearly every packet.
/// All memory is taken in the constructor. Input position is tracked with exact integers, so it never drifts.
/// </summary>
public sealed class Resampler
{
    // Filter half-length in input frames when not going down in rate, and steps between tabulated filter positions.
    private const int BaseHalfLength = 16;
    private const int Phases = 256;
    private const double KaiserBeta = 9.0;

    private readonly int _channels;
    private readonly int _inputRate;
    private readonly int _outputRate;
    private readonly int _halfLength;
    private readonly int _tapCount;
    private readonly float[] _table;
    private readonly float[] _held;

    private int _heldFrames;
    private int _index;
    private int _fraction;

    /// <param name="inputRate">Input frames per second.</param>
    /// <param name="outputRate">Output frames per second.</param>
    /// <param name="channels">Samples per frame.</param>
    /// <param name="maxInputFrames">The most frames one call to <see cref="Process"/> takes.</param>
    public Resampler(int inputRate, int outputRate, int channels, int maxInputFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);
        _inputRate = inputRate;
        _outputRate = outputRate;
        _channels = channels;

        // Going down in rate, the cutoff drops to the new Nyquist, and the filter gets longer to match.
        var cutoff = Math.Min(1.0, (double)outputRate / inputRate);
        _halfLength = (int)Math.Ceiling(BaseHalfLength / cutoff);
        _tapCount = _halfLength * 2;
        _table = BuildTable(_halfLength, _tapCount, cutoff * 0.96);
        _held = new float[(_tapCount + maxInputFrames + 2) * channels];
        MaxOutputFrames = (int)Math.Ceiling((double)(maxInputFrames + _tapCount) * outputRate / inputRate) + 2;

        // Zeros stand for the time before the first frame, which is where the first output frame sits.
        _heldFrames = _halfLength - 1;
        _index = _halfLength - 1;
    }

    /// <summary>The most output frames one call can produce.</summary>
    public int MaxOutputFrames { get; }

    /// <summary>Takes in frames and writes out as many as can be computed. Allocates nothing.</summary>
    /// <returns>The number of frames written.</returns>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var channels = _channels;
        input.CopyTo(_held.AsSpan(_heldFrames * channels));
        var total = _heldFrames + input.Length / channels;

        var step = _inputRate / _outputRate;
        var stepFraction = _inputRate % _outputRate;
        var produced = 0;
        while (_index + _halfLength < total)
        {
            WriteFrame(output.Slice(produced * channels, channels));
            produced++;

            _index += step;
            _fraction += stepFraction;
            if (_fraction >= _outputRate)
            {
                _fraction -= _outputRate;
                _index++;
            }
        }

        // Keep what the next frames will reach back to.
        var keepFrom = Math.Min(_index - _halfLength + 1, total);
        _held.AsSpan(keepFrom * channels, (total - keepFrom) * channels).CopyTo(_held);
        _heldFrames = total - keepFrom;
        _index -= keepFrom;
        return produced;
    }

    private void WriteFrame(Span<float> frame)
    {
        // The exact filter position falls between two tabulated rows: blend them.
        var position = (long)_fraction * Phases;
        var row = (int)(position / _outputRate);
        var blend = (float)(position % _outputRate) / _outputRate;
        var lower = _table.AsSpan(row * _tapCount, _tapCount);
        var upper = _table.AsSpan((row + 1) * _tapCount, _tapCount);

        var first = _index - _halfLength + 1;
        for (var channel = 0; channel < frame.Length; channel++)
        {
            var sum = 0f;
            var at = first * _channels + channel;
            for (var tap = 0; tap < lower.Length; tap++)
            {
                sum += _held[at] * (lower[tap] + (upper[tap] - lower[tap]) * blend);
                at += _channels;
            }

            frame[channel] = sum;
        }
    }

    // One row of filter taps for each tabulated position between two input frames, each row with a gain of exactly 1.
    private static float[] BuildTable(int halfLength, int tapCount, double cutoff)
    {
        var table = new float[(Phases + 1) * tapCount];
        for (var phase = 0; phase <= Phases; phase++)
        {
            var row = table.AsSpan(phase * tapCount, tapCount);
            var offset = (double)phase / Phases;
            var sum = 0.0;
            for (var tap = 0; tap < tapCount; tap++)
            {
                // The taps sit at input frames -halfLength + 1 to halfLength, around the position wanted.
                var distance = tap - halfLength + 1 - offset;
                var value = Sinc(distance * cutoff) * Kaiser(distance / halfLength);
                row[tap] = (float)value;
                sum += value;
            }

            for (var tap = 0; tap < tapCount; tap++)
            {
                row[tap] = (float)(row[tap] / sum);
            }
        }

        return table;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9)
        {
            return 1;
        }

        var angle = Math.PI * x;
        return Math.Sin(angle) / angle;
    }

    private static double Kaiser(double x)
    {
        var inside = 1 - x * x;
        return inside <= 0 ? 0 : BesselI0(KaiserBeta * Math.Sqrt(inside)) / BesselI0(KaiserBeta);
    }

    private static double BesselI0(double x)
    {
        var sum = 1.0;
        var term = 1.0;
        for (var k = 1; k < 32; k++)
        {
            term *= x / (2 * k) * (x / (2 * k));
            sum += term;
        }

        return sum;
    }
}
