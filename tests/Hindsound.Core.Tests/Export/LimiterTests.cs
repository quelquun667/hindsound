using Hindsound.Export;

namespace Hindsound.Tests.Export;

public class LimiterTests
{
    [Fact]
    public void Audio_already_under_the_ceiling_is_not_touched()
    {
        var audio = Signals.Sine(440, 0.5, 1);
        var copy = (float[])audio.Clone();

        var changed = Limiter.Apply(audio, 2, 0.9f);

        Assert.False(changed);
        Assert.Equal(copy, audio);
    }

    [Fact]
    public void No_sample_is_above_the_ceiling_after_limiting_noise_four_times_too_loud()
    {
        var random = new Random(1234);
        var audio = new float[48000 * 2 * 2];
        for (var i = 0; i < audio.Length; i++) audio[i] = (float)(random.NextDouble() * 8 - 4);
        const float ceiling = 0.891f;

        Limiter.Apply(audio, 2, ceiling);

        Assert.True(Signals.Peak(audio) <= ceiling);
    }

    [Fact]
    public void The_gain_moves_smoothly_so_a_short_burst_makes_no_click()
    {
        // A constant level makes the gain readable: output / input. A 10 ms burst three times too loud.
        const float ceiling = 0.5f;
        var audio = Signals.Constant(0.1f, 48000, channels: 1);
        for (var i = 24000; i < 24480; i++) audio[i] = 1.5f;

        Limiter.Apply(audio, 1, ceiling);

        var gains = audio.Select((v, i) => i >= 24000 && i < 24480 ? v / 1.5 : v / 0.1).ToArray();
        var biggestStep = Enumerable.Range(1, gains.Length - 1).Max(i => Math.Abs(gains[i] - gains[i - 1]));

        Assert.True(Signals.Peak(audio) <= ceiling);
        Assert.Equal(ceiling, audio[24000 + 240], 3);              // the burst is held at the ceiling
        Assert.True(biggestStep < 0.005, $"biggest gain step {biggestStep}"); // 1/240 of a 0.67 reduction is 0.0028
        Assert.True(gains[24000 - 300] > 0.999);                   // untouched well before the burst
        Assert.True(gains[24480 + 20000] > 0.99);                  // released 0.4 s later
    }

    [Fact]
    public void The_gain_is_already_down_when_the_peak_arrives()
    {
        var audio = Signals.Constant(0.1f, 24000, channels: 1);
        audio[12000] = 1.0f;

        Limiter.Apply(audio, 1, 0.5f);

        Assert.True(audio[12000] <= 0.5f);
        Assert.True(audio[12000 - 120] / 0.1f < 0.85f); // 2.5 ms earlier the gain is already well below 1
    }

    [Fact]
    public void Both_channels_of_a_frame_get_the_same_gain()
    {
        var audio = new float[2 * 48000];
        for (var i = 0; i < 48000; i++)
        {
            audio[2 * i] = 0.2f;          // left, loud
            audio[2 * i + 1] = 0.02f;     // right, quiet
        }

        for (var i = 24000; i < 24200; i++) audio[2 * i] = 2f;

        Limiter.Apply(audio, 2, 0.8f);

        for (var i = 23000; i < 25000; i += 50)
            Assert.Equal(audio[2 * i] / (i >= 24000 && i < 24200 ? 2f : 0.2f), audio[2 * i + 1] / 0.02f, 4);
    }
}
