using System.Numerics;
using Hindsound.Export;

namespace Hindsound.Tests.Export;

public class LoudnessMeterTests
{
    /// <summary>
    /// Tolerance on every loudness reading of a stationary sine: 0.05 LU.
    /// The tolerance of EBU Tech 3341 (the EBU's compliance test of BS.1770 meters) is +/-0.1 LU for these signals;
    /// half of it is used here. The remaining difference to the exact value comes from the 400 ms block holding a
    /// fractional number of cycles and from the filter start-up, both well under 0.01 LU.
    /// </summary>
    private const double ToleranceLu = 0.05;

    /// <summary>
    /// The gain of the two K-weighting filters at a frequency, from the standard's 48 kHz coefficients, evaluated on
    /// the unit circle with complex arithmetic. Independent of the meter's time-domain filtering.
    /// </summary>
    private static double KWeightingPowerGain(double hz)
    {
        var z = Complex.Exp(new Complex(0, -2 * Math.PI * hz / 48000)); // z^-1
        static Complex Response(double[] b, double[] a, Complex zInv) =>
            (b[0] + b[1] * zInv + b[2] * zInv * zInv) / (a[0] + a[1] * zInv + a[2] * zInv * zInv);

        var shelf = Response([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585], z);
        var highPass = Response([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621], z);
        var magnitude = (shelf * highPass).Magnitude;
        return magnitude * magnitude;
    }

    [Fact]
    public void Reference_signal_of_the_standard_997_Hz_at_0_dBFS_on_one_front_channel_reads_minus_3_01_LUFS()
    {
        // BS.1770: a 997 Hz sine at 0 dBFS in one front channel reads -3.01 LKFS. The K filter is about +0.691 dB at
        // 997 Hz, which the -0.691 offset of the formula cancels, leaving 10 log10(1/2) = -3.01.
        var left = Signals.Sine(997, 1.0, 20, channels: 1);
        var stereo = new float[left.Length * 2];
        for (var i = 0; i < left.Length; i++) stereo[2 * i] = left[i];

        var measured = LoudnessMeter.Measure(stereo, 2);

        Assert.Equal(-3.01, measured, ToleranceLu);

        // Same value derived from the filter response instead of the printed -3.01.
        var expected = -0.691 + 10 * Math.Log10(0.5 * KWeightingPowerGain(997));
        Assert.Equal(expected, measured, ToleranceLu);
    }

    [Fact]
    public void Stereo_1_kHz_sine_at_minus_23_dBFS_reads_minus_23_LUFS()
    {
        // EBU Tech 3341 test case 1: stereo 1 kHz sine at -23 dBFS reads -23.0 LUFS.
        var measured = LoudnessMeter.Measure(Signals.Sine(1000, Signals.DbToLinear(-23), 20), 2);

        Assert.Equal(-23.0, measured, ToleranceLu);
    }

    [Fact]
    public void A_mono_sine_reads_3_dB_lower_than_the_same_sine_on_two_channels()
    {
        var mono = LoudnessMeter.Measure(Signals.Sine(1000, Signals.DbToLinear(-20), 10, channels: 1), 1);

        Assert.Equal(-23.0, mono, ToleranceLu);
    }

    [Fact]
    public void Silence_has_no_loudness()
    {
        Assert.True(double.IsNegativeInfinity(LoudnessMeter.Measure(new float[48000 * 2 * 2], 2)));
    }

    [Fact]
    public void A_signal_under_the_absolute_gate_has_no_loudness()
    {
        // -80 dBFS stereo is -80 LUFS, under the -70 LUFS gate.
        var measured = LoudnessMeter.Measure(Signals.Sine(1000, Signals.DbToLinear(-80), 5), 2);

        Assert.True(double.IsNegativeInfinity(measured));
    }

    [Fact]
    public void The_relative_gate_ignores_a_passage_more_than_10_LU_below_the_rest()
    {
        // 10 s at -20 LUFS then 10 s at -45 LUFS. Without the relative gate the average would be near -23;
        // with it only the loud passage counts (the gate sits near -33 LUFS).
        // Of the 100 blocks that pass, 97 are fully loud and the 3 that straddle the junction hold 75 %, 50 % and
        // 25 % of loud audio (the quiet part adds less than 0.001 LU). Their mean power: 10 log10(0.985) = -0.066 LU.
        var loud = Signals.Sine(1000, Signals.DbToLinear(-20), 10);
        var quiet = Signals.Sine(1000, Signals.DbToLinear(-45), 10);
        var expected = -20 + 10 * Math.Log10((97 + 0.75 + 0.5 + 0.25) / 100);

        var measured = LoudnessMeter.Measure([.. loud, .. quiet], 2);

        Assert.Equal(expected, measured, ToleranceLu);
        Assert.True(measured > -21, "the quiet half was not gated out");
    }

    [Fact]
    public void A_passage_less_than_10_LU_below_the_rest_still_counts()
    {
        // 10 s at -20 LUFS and 10 s at -26 LUFS: both pass, the average power is 10 log10((1e-2 + 10^-2.6) / 2) = -23.0.
        var loud = Signals.Sine(1000, Signals.DbToLinear(-20), 10);
        var quiet = Signals.Sine(1000, Signals.DbToLinear(-26), 10);
        var expected = 10 * Math.Log10((Math.Pow(10, -2.0) + Math.Pow(10, -2.6)) / 2);

        var measured = LoudnessMeter.Measure([.. loud, .. quiet], 2);

        Assert.Equal(expected, measured, 0.1); // blocks across the junction add a little spread
    }

    [Fact]
    public void A_clip_shorter_than_one_block_is_measured_as_one_block()
    {
        var measured = LoudnessMeter.Measure(Signals.Sine(1000, Signals.DbToLinear(-23), 0.2), 2);

        // The filter start-up weighs more on 0.2 s: allow a little more.
        Assert.Equal(-23.0, measured, 0.3);
    }

    [Fact]
    public void Feeding_the_audio_in_pieces_gives_the_same_result_as_all_at_once()
    {
        var audio = Signals.Sine(440, 0.1, 7);
        var whole = LoudnessMeter.Measure(audio, 2);

        var meter = new LoudnessMeter(2);
        for (var start = 0; start < audio.Length; start += 2 * 1234)
            meter.Add(audio.AsSpan(start, Math.Min(2 * 1234, audio.Length - start)));

        Assert.Equal(whole, meter.Finish(), 9);
    }
}
