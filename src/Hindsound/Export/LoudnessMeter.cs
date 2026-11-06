using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// Integrated loudness after ITU-R BS.1770 (the measure EBU R128 calls LUFS), for 48 kHz audio.
/// K-weighting with the coefficients printed in the standard for 48 kHz (a high shelf, then a high-pass),
/// mean square per channel over 400 ms blocks with 75 % overlap (a new block every 100 ms), channels summed
/// with weight 1.0 (mono and stereo are all front channels), then two-stage gating: absolute at -70 LUFS,
/// relative at 10 LU below the average of the blocks that passed.
/// </summary>
/// <remarks>
/// Feed audio with <see cref="Add"/> in any chunk size, then call <see cref="Finish"/> once. Memory use is
/// constant apart from one number per 100 ms. A clip shorter than one block is measured as a single block.
/// </remarks>
public sealed class LoudnessMeter
{
    private const int StepFrames = InternalFormat.SampleRate / 10; // 100 ms
    private const int BlockSteps = 4;                              // 400 ms
    private const double AbsoluteGateLufs = -70;
    private const double RelativeGateLu = -10;
    private const double Offset = -0.691;

    // Stage 1: shelving filter, stage 2: high-pass (ITU-R BS.1770-4, table 1 and 2, 48 kHz).
    private static readonly double[] ShelfB = [1.53512485958697, -2.69169618940638, 1.19839281085285];
    private static readonly double[] ShelfA = [1.0, -1.69065929318241, 0.73248077421585];
    private static readonly double[] HighPassB = [1.0, -2.0, 1.0];
    private static readonly double[] HighPassA = [1.0, -1.99004745483398, 0.99007225036621];

    private readonly int _channels;
    private readonly Biquad[] _shelf;
    private readonly Biquad[] _highPass;
    private readonly List<double> _stepPowers = [];
    private double _stepSum;
    private int _stepFrames;
    private double _totalSum;
    private long _totalFrames;
    private bool _finished;

    public LoudnessMeter(int channels)
    {
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels), channels, "Mono or stereo only.");
        _channels = channels;
        _shelf = new Biquad[channels];
        _highPass = new Biquad[channels];
    }

    /// <summary>Measures a whole interleaved buffer.</summary>
    public static double Measure(ReadOnlySpan<float> interleaved, int channels)
    {
        var meter = new LoudnessMeter(channels);
        meter.Add(interleaved);
        return meter.Finish();
    }

    /// <summary>Adds interleaved samples. The length must be a whole number of frames.</summary>
    public void Add(ReadOnlySpan<float> interleaved)
    {
        if (_finished) throw new InvalidOperationException("The measurement is finished.");
        if (interleaved.Length % _channels != 0) throw new ArgumentException("Not a whole number of frames.", nameof(interleaved));

        for (var i = 0; i < interleaved.Length; i += _channels)
        {
            var frameSum = 0.0;
            for (var c = 0; c < _channels; c++)
            {
                var y = _highPass[c].Process(_shelf[c].Process(interleaved[i + c], ShelfB, ShelfA), HighPassB, HighPassA);
                frameSum += y * y;
            }

            _stepSum += frameSum;
            _totalSum += frameSum;
            _totalFrames++;
            if (++_stepFrames == StepFrames)
            {
                _stepPowers.Add(_stepSum / StepFrames);
                _stepSum = 0;
                _stepFrames = 0;
            }
        }
    }

    /// <summary>The integrated loudness in LUFS, or <see cref="double.NegativeInfinity"/> if nothing passes the gate.</summary>
    public double Finish()
    {
        _finished = true;

        // Mean power of each full 400 ms block (a block starts every 100 ms).
        var blocks = new List<double>();
        for (var start = 0; start + BlockSteps <= _stepPowers.Count; start++)
        {
            var sum = 0.0;
            for (var k = 0; k < BlockSteps; k++) sum += _stepPowers[start + k];
            blocks.Add(sum / BlockSteps);
        }

        // Shorter than one block: the whole clip counts as one.
        if (blocks.Count == 0 && _totalFrames > 0) blocks.Add(_totalSum / _totalFrames);

        var absoluteGate = FromLufs(AbsoluteGateLufs);
        var passedAbsolute = blocks.Where(p => p > absoluteGate).ToList();
        if (passedAbsolute.Count == 0) return double.NegativeInfinity;

        var relativeGate = FromPower(passedAbsolute.Average()) + RelativeGateLu;
        var relativeGatePower = FromLufs(relativeGate);
        var passedBoth = passedAbsolute.Where(p => p > relativeGatePower).ToList();
        if (passedBoth.Count == 0) return double.NegativeInfinity;

        return FromPower(passedBoth.Average());
    }

    private static double FromPower(double meanSquare) => Offset + 10 * Math.Log10(meanSquare);

    private static double FromLufs(double lufs) => Math.Pow(10, (lufs - Offset) / 10);

    /// <summary>One filter section, direct form II transposed, in double precision.</summary>
    private struct Biquad
    {
        private double _z1;
        private double _z2;

        public double Process(double x, double[] b, double[] a)
        {
            var y = b[0] * x + _z1;
            _z1 = b[1] * x - a[1] * y + _z2;
            _z2 = b[2] * x - a[2] * y;
            return y;
        }
    }
}
