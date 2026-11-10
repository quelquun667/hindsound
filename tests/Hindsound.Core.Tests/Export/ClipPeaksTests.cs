using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Tests.Export;

public class ClipPeaksTests
{
    // Reference values worked out by hand: bar = (dB + 60) / 60, with dB = 20 log10(peak).
    //   0.1  -> -20 dB    -> 40 / 60 = 0.66667
    //   0.01 -> -40 dB    -> 20 / 60 = 0.33333
    //   0.5  -> -6.0206   -> 53.9794 / 60 = 0.89966
    //   1.0  ->   0 dB    -> 1
    private const float Tolerance = 1e-4f;

    /// <summary>2400 stereo frames (100 per bar) of silence with the given samples set.</summary>
    private static AudioSnapshot Snapshot(params (int Frame, int Channel, float Value)[] samples)
    {
        var system = new float[2400 * 2];
        foreach (var (frame, channel, value) in samples) system[2 * frame + channel] = value;
        return new AudioSnapshot(system, []);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(float.NegativeInfinity)]
    public void ToBar_gives_an_empty_bar_for_a_peak_that_is_not_positive(float linear)
    {
        Assert.Equal(0f, ClipPeaks.ToBar(linear));
    }

    [Fact]
    public void There_are_24_bars_and_they_follow_hand_computed_values()
    {
        var snapshot = Snapshot(
            (550, 0, 0.1f),     // bar 5, left
            (1050, 1, 0.01f),   // bar 10, right
            (1599, 0, -0.5f),   // bar 15, last frame, negative: the absolute value counts
            (2399, 1, 1.0f));   // bar 23

        var peaks = ClipPeaks.Compute(snapshot);

        Assert.Equal(24, peaks.Bars.Count);
        Assert.Equal(0.66667f, peaks.Bars[5], Tolerance);
        Assert.Equal(0.33333f, peaks.Bars[10], Tolerance);
        Assert.Equal(0.89966f, peaks.Bars[15], Tolerance);
        Assert.Equal(1f, peaks.Bars[23], Tolerance);
        foreach (var i in Enumerable.Range(0, 24).Except([5, 10, 15, 23])) Assert.Equal(0f, peaks.Bars[i]);
        Assert.False(peaks.IsSilent);
    }

    [Fact]
    public void The_most_recent_part_of_the_clip_is_the_last_bar()
    {
        var peaks = ClipPeaks.Compute(Snapshot((2399, 0, 1f)));

        Assert.Equal(1f, peaks.Bars[23]);
        Assert.All(peaks.Bars.Take(23), b => Assert.Equal(0f, b));
    }

    [Fact]
    public void A_bar_is_the_peak_of_its_part_not_its_average()
    {
        // One loud frame among 100 in bar 3 (frames 300..399).
        var peaks = ClipPeaks.Compute(Snapshot((321, 0, 0.1f), (322, 0, 0.05f)));

        Assert.Equal(0.66667f, peaks.Bars[3], Tolerance);
    }

    [Fact]
    public void The_microphone_counts_too()
    {
        var mic = new float[2400];
        mic[2000] = 0.1f; // bar 20
        var peaks = ClipPeaks.Compute(new AudioSnapshot([], mic));

        Assert.Equal(0.66667f, peaks.Bars[20], Tolerance);
        Assert.False(peaks.IsSilent);
    }

    [Fact]
    public void A_shorter_source_is_aligned_to_the_end()
    {
        // System: 2400 frames of silence. Mic: the last 1200 frames, constant 0.5 -> bars 12 to 23.
        var mic = Enumerable.Repeat(0.5f, 1200).ToArray();
        var peaks = ClipPeaks.Compute(new AudioSnapshot(new float[2400 * 2], mic));

        for (var i = 0; i < 12; i++) Assert.Equal(0f, peaks.Bars[i]);
        for (var i = 12; i < 24; i++) Assert.Equal(0.89966f, peaks.Bars[i], Tolerance);
    }

    [Fact]
    public void Digital_silence_gives_empty_bars_and_is_silent()
    {
        var peaks = ClipPeaks.Compute(new AudioSnapshot(new float[4800 * 2], new float[4800]));

        Assert.All(peaks.Bars, b => Assert.Equal(0f, b));
        Assert.True(peaks.IsSilent);
    }

    [Theory]
    [InlineData(0.0005f, true)]   // -66 dBFS
    [InlineData(0.0009f, true)]   // -60.9 dBFS
    [InlineData(0.0011f, false)]  // -59.2 dBFS
    [InlineData(0.002f, false)]   // -54 dBFS
    public void Silence_is_a_peak_under_minus_60_dBFS(float peak, bool silent)
    {
        Assert.Equal(silent, ClipPeaks.Compute(Snapshot((1000, 0, peak))).IsSilent);
    }

    [Fact]
    public void A_bar_below_minus_60_dBFS_is_empty()
    {
        var peaks = ClipPeaks.Compute(Snapshot((1000, 0, 0.0005f), (2000, 0, 0.5f)));

        Assert.Equal(0f, peaks.Bars[10]);
    }

    [Fact]
    public void A_clip_of_a_single_frame_fills_every_bar_with_it()
    {
        var peaks = ClipPeaks.Compute(new AudioSnapshot([0.5f, 0.25f], []));

        Assert.Equal(24, peaks.Bars.Count);
        Assert.All(peaks.Bars, b => Assert.Equal(0.89966f, b, Tolerance));
        Assert.False(peaks.IsSilent);
    }

    [Fact]
    public void A_clip_of_five_frames_has_a_value_in_every_bar()
    {
        var system = new float[10];
        system[8] = 0.1f; // frame 4, the last one
        var peaks = ClipPeaks.Compute(new AudioSnapshot(system, []));

        Assert.Equal(24, peaks.Bars.Count);
        Assert.Equal(0.66667f, peaks.Bars[23], Tolerance);
        Assert.Equal(0f, peaks.Bars[0]);
        Assert.False(peaks.IsSilent);
    }

    [Fact]
    public void An_empty_snapshot_gives_24_empty_bars_and_is_silent()
    {
        var peaks = ClipPeaks.Compute(new AudioSnapshot([], []));

        Assert.Equal(24, peaks.Bars.Count);
        Assert.All(peaks.Bars, b => Assert.Equal(0f, b));
        Assert.True(peaks.IsSilent);
    }

    [Fact]
    public void Bars_never_leave_zero_to_one()
    {
        var peaks = ClipPeaks.Compute(Snapshot((100, 0, 5f), (200, 0, float.PositiveInfinity)));

        Assert.All(peaks.Bars, b => Assert.InRange(b, 0f, 1f));
    }
}
