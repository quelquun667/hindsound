using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Tests.Export;

/// <summary>The sound of an edit: cut to the frame, the volume as asked, the peaks held under the ceiling and nothing raised.</summary>
public sealed class ClipEditRenderTests
{
    private const int Rate = InternalFormat.SampleRate;
    private static readonly float MinusOneDb = ClipEditRender.Ceiling(-1);

    private static float[] Ramp(int frames) => [.. Enumerable.Range(0, frames).Select(i => i * 1e-6f)];

    private static float[] Constant(int frames, float value) => [.. Enumerable.Repeat(value, frames)];

    private static List<float> Render(IReadOnlyList<FakeStream> sources, IReadOnlyList<double> gains, double start, double end, int channels)
    {
        var output = new CollectingWriter();
        ClipEditRender.Render(sources, gains, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), channels, MinusOneDb, output, CancellationToken.None);
        return output.Samples;
    }

    [Fact]
    public void The_volume_is_held_from_minus_12_to_plus_20_dB_and_anything_not_a_number_is_0_dB()
    {
        Assert.Equal(10f, ClipEditRender.Gain(20), 0.0001f);
        Assert.Equal(0.2511886f, ClipEditRender.Gain(-12), 0.0001f);
        Assert.Equal(ClipEditRender.Gain(20), ClipEditRender.Gain(40));
        Assert.Equal(ClipEditRender.Gain(-12), ClipEditRender.Gain(-40));
        Assert.Equal(1f, ClipEditRender.Gain(double.NaN));
        Assert.Equal(1f, ClipEditRender.Gain(double.PositiveInfinity));
    }

    [Fact]
    public void The_cut_starts_and_ends_on_the_frame_asked_and_keeps_every_frame_between()
    {
        // Each sample holds its own frame number: the output says which frames it kept.
        var samples = Render([new FakeStream(Ramp(Rate), 1)], [0], 0.25, 0.5, 1);

        Assert.Equal(Rate / 4, samples.Count);
        Assert.Equal(Rate / 4 * 1e-6f, samples[0], 1e-9f);
        Assert.Equal((Rate / 2 - 1) * 1e-6f, samples[^1], 1e-9f);
    }

    [Fact]
    public void A_stereo_cut_keeps_both_sides_of_every_frame()
    {
        var samples = Render([new FakeStream(Ramp(2 * Rate), 2)], [0], 0.5, 0.75, 2);

        Assert.Equal(2 * (Rate / 4), samples.Count);
        Assert.Equal(Rate * 1e-6f, samples[0], 1e-9f);
        Assert.Equal((Rate + 1) * 1e-6f, samples[1], 1e-9f);
    }

    [Fact]
    public void Six_decibels_more_doubles_the_amplitude()
    {
        var samples = Render([new FakeStream(Constant(Rate, 0.1f), 1)], [6], 0, 1, 1);

        Assert.Equal(Rate, samples.Count);
        Assert.All(samples.Skip(100).Take(1000), sample => Assert.Equal(0.2f, sample, 0.002f));
    }

    [Fact]
    public void A_peak_over_the_ceiling_is_held_under_it_and_the_rest_is_not_raised()
    {
        var source = Constant(Rate, 0.1f);
        for (var i = Rate / 2; i < Rate / 2 + 100; i++)
        {
            source[i] = 0.9f;
        }

        var samples = Render([new FakeStream(source, 1)], [6], 0, 1, 1);

        Assert.True(samples.Max(Math.Abs) <= MinusOneDb * 1.000001f, $"peak {samples.Max(Math.Abs)} over the ceiling {MinusOneDb}");

        // Far from the peak, the sound is what the volume made it: no normalization brings it up to the ceiling.
        Assert.Equal(0.2f, samples[1000], 0.002f);
        Assert.Equal(0.2f, samples[^1000], 0.002f);
    }

    [Fact]
    public void A_mono_track_mixed_with_a_stereo_one_reaches_both_sides_and_the_mix_ends_with_the_longest()
    {
        var system = new FakeStream(Constant(2 * Rate, 0.1f), 2);
        var mic = new FakeStream(Constant(Rate / 2, 0.2f), 1);

        var samples = Render([system, mic], [0, 0], 0, 1, 2);

        Assert.Equal(2 * Rate, samples.Count);
        Assert.Equal(0.3f, samples[200], 0.0001f);
        Assert.Equal(0.3f, samples[201], 0.0001f);

        // The mic ended at half a second: from there, only the system is heard.
        Assert.Equal(0.1f, samples[2 * (Rate - 100)], 0.0001f);
    }

    [Fact]
    public void Each_track_has_its_own_volume_in_a_mix()
    {
        var system = new FakeStream(Constant(Rate, 0.1f), 1);
        var mic = new FakeStream(Constant(Rate, 0.1f), 1);

        var samples = Render([system, mic], [6, -12], 0, 1, 1);

        Assert.Equal(0.1f * ClipEditRender.Gain(6) + 0.1f * ClipEditRender.Gain(-12), samples[500], 0.0001f);
    }
}
