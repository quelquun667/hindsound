using Hindsound.Export;

namespace Hindsound.Tests.Export;

public class NormalizerTests
{
    private const double Ceiling = -1;
    private const double Target = -16;

    private static double Lufs(float[] stereo) => LoudnessMeter.Measure(stereo, 2);

    // ---------------------------------------------------------------- Peak

    [Fact]
    public void Peak_mode_raises_a_quiet_clip_until_its_peak_is_at_the_ceiling()
    {
        var audio = Signals.Sine(440, Signals.DbToLinear(-12), 2);

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Peak, Target, Ceiling);

        Assert.Equal(11.0, gain, 0.01);                       // -12 -> -1
        Assert.Equal(-1.0, Signals.PeakDb(audio), 0.01);
    }

    [Fact]
    public void Peak_mode_lowers_a_clip_that_is_above_the_ceiling()
    {
        var audio = Signals.Sine(440, 1.0, 1);

        Normalizer.Normalize(audio, 2, NormalizationMode.Peak, Target, Ceiling);

        Assert.Equal(-1.0, Signals.PeakDb(audio), 0.01);
    }

    [Fact]
    public void Peak_mode_does_not_raise_a_near_silent_clip_by_more_than_20_dB()
    {
        var audio = Signals.Sine(440, Signals.DbToLinear(-70), 2); // would need +69 dB

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Peak, Target, Ceiling);

        Assert.Equal(20.0, gain, 0.001);
        Assert.Equal(-50.0, Signals.PeakDb(audio), 0.01);
    }

    [Fact]
    public void Peak_mode_leaves_digital_silence_alone()
    {
        var audio = new float[48000 * 2];

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Peak, Target, Ceiling);

        Assert.Equal(0, gain);
        Assert.All(audio, s => Assert.Equal(0f, s));
    }

    // ---------------------------------------------------------------- Loudness

    [Fact]
    public void Loudness_mode_brings_a_very_quiet_clip_to_the_target_without_passing_the_ceiling()
    {
        // 1 kHz stereo at -30 dBFS reads -30 LUFS: +14 dB, a peak of -16 dBFS.
        var audio = Signals.Sine(1000, Signals.DbToLinear(-30), 10);

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Loudness, Target, Ceiling);

        Assert.Equal(14.0, gain, 0.1);
        Assert.Equal(Target, Lufs(audio), 0.1);
        Assert.True(Signals.PeakDb(audio) <= Ceiling + 1e-4);
    }

    [Fact]
    public void Loudness_mode_lowers_a_clip_that_is_too_loud()
    {
        var audio = Signals.Sine(1000, Signals.DbToLinear(-6), 10);

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Loudness, Target, Ceiling);

        Assert.Equal(-10.0, gain, 0.1);
        Assert.Equal(Target, Lufs(audio), 0.1);
    }

    [Fact]
    public void Loudness_mode_holds_the_ceiling_when_peaks_stick_out_of_a_quiet_clip()
    {
        // A quiet tone with short loud clicks: the gain that fixes the loudness would put the clicks far over -1 dBFS.
        var audio = Signals.Sine(1000, Signals.DbToLinear(-30), 10);
        for (var burst = 0; burst < 10; burst++)
            for (var i = 0; i < 48; i++) audio[2 * (48000 * burst + 20000 + i)] = audio[2 * (48000 * burst + 20000 + i) + 1] = 0.5f;

        Normalizer.Normalize(audio, 2, NormalizationMode.Loudness, Target, Ceiling);

        var ceilingLinear = Signals.DbToLinear(Ceiling);
        Assert.True(Signals.Peak(audio) <= ceilingLinear * 1.000001);
        Assert.True(Signals.Peak(audio) > ceilingLinear * 0.99); // and it does reach it: the limiter does not overshoot downwards
    }

    [Fact]
    public void Loudness_mode_does_not_raise_a_near_silent_clip_by_more_than_20_dB()
    {
        // -62 dBFS reads -62 LUFS: above the -70 gate, so it is measured and would need +46 dB.
        var audio = Signals.Sine(1000, Signals.DbToLinear(-62), 10);

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Loudness, Target, Ceiling);

        Assert.Equal(20.0, gain, 0.001);
        Assert.Equal(-42.0, Signals.PeakDb(audio), 0.01);
    }

    [Fact]
    public void Loudness_mode_leaves_a_clip_under_the_absolute_gate_alone()
    {
        var audio = Signals.Sine(1000, Signals.DbToLinear(-85), 5);
        var copy = (float[])audio.Clone();

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Loudness, Target, Ceiling);

        Assert.Equal(0, gain);
        Assert.Equal(copy, audio);
    }

    [Fact]
    public void Loudness_mode_leaves_a_silent_clip_silent()
    {
        var audio = new float[48000 * 2 * 3];

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Loudness, Target, Ceiling);

        Assert.Equal(0, gain);
        Assert.All(audio, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Off_does_nothing()
    {
        var audio = Signals.Sine(440, 0.01, 1);
        var copy = (float[])audio.Clone();

        var gain = Normalizer.Normalize(audio, 2, NormalizationMode.Off, Target, 0);

        Assert.Equal(0, gain);
        Assert.Equal(copy, audio);
    }
}
