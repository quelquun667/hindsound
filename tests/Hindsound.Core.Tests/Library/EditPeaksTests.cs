using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The bars of the editor: the peak of each slice of the length, read in pieces.</summary>
public sealed class EditPeaksTests
{
    private const int Rate = InternalFormat.SampleRate;

    /// <summary>A stream made by a function of the frame and the channel, which keeps what was asked of it.</summary>
    private sealed class FakeStream(int channels, long frames, Func<long, int, float> sample, TimeSpan? duration = null) : IDecodedStream
    {
        private long _at;

        public int Channels { get; } = channels;
        public TimeSpan Duration { get; } = duration ?? TimeSpan.FromSeconds((double)frames / Rate);
        public int LargestRead { get; private set; }
        public int Reads { get; private set; }

        public void Seek(TimeSpan at) => throw new NotSupportedException();

        public int Read(Span<float> interleaved)
        {
            Reads++;
            LargestRead = Math.Max(LargestRead, interleaved.Length);
            var count = (int)Math.Min(interleaved.Length / Channels, frames - _at);
            for (var f = 0; f < count; f++, _at++)
            {
                for (var c = 0; c < Channels; c++) interleaved[f * Channels + c] = sample(_at, c);
            }

            return count * Channels;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Silence_then_full_scale_gives_empty_bars_then_full_ones()
    {
        const long frames = 2 * Rate;
        var stream = new FakeStream(2, frames, (frame, _) => frame < frames / 2 ? 0f : 1f);

        var bars = EditPeaks.Compute(stream);

        Assert.Equal(EditPeaks.BarCount, bars.Count);
        Assert.Equal(128, bars.Count);
        Assert.All(bars.Take(64), bar => Assert.Equal(0f, bar));
        Assert.All(bars.Skip(64), bar => Assert.Equal(1f, bar));
    }

    [Fact]
    public void A_bar_is_the_peak_of_its_slice_not_its_average()
    {
        // One frame in each slice of 750 rises to -6 dBFS (negative in the right channel only); all the rest is silence.
        const int bars = 128;
        const long frames = 750 * bars;
        var stream = new FakeStream(2, frames, (frame, channel) => frame % 750 == 300 && channel == 1 ? -0.5f : 0f);

        var result = EditPeaks.Compute(stream, bars);

        var expected = ClipPeaks.ToBar(0.5f);
        Assert.InRange(expected, 0.89f, 0.92f);
        Assert.All(result, bar => Assert.Equal(expected, bar, 5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(300)]
    public void The_number_of_bars_is_the_one_asked(int bars)
    {
        var stream = new FakeStream(1, Rate / 2, (_, _) => 0.25f);

        var result = EditPeaks.Compute(stream, bars);

        Assert.Equal(bars, result.Count);
        Assert.All(result, bar => Assert.Equal(ClipPeaks.ToBar(0.25f), bar, 5));
    }

    [Fact]
    public void Each_slice_takes_its_own_share_of_the_length()
    {
        // A mono ramp of loud slices: slice i of 4 rises to (i + 1) / 4.
        const long frames = 4 * 1000;
        var stream = new FakeStream(1, frames, (frame, _) => (frame / 1000 + 1) / 4f);

        var result = EditPeaks.Compute(stream, 4);

        Assert.Equal([ClipPeaks.ToBar(0.25f), ClipPeaks.ToBar(0.5f), ClipPeaks.ToBar(0.75f), 1f], result.ToArray(), (a, b) => Math.Abs(a - b) < 1e-5);
    }

    [Fact]
    public void Frames_past_the_announced_length_count_in_the_last_bar()
    {
        // The file says 1 s but holds 1.5 s; the loud half second at the end must not be lost.
        var stream = new FakeStream(1, Rate * 3 / 2, (frame, _) => frame >= Rate ? 1f : 0f, TimeSpan.FromSeconds(1));

        var result = EditPeaks.Compute(stream, 10);

        Assert.All(result.Take(9), bar => Assert.Equal(0f, bar));
        Assert.Equal(1f, result[9]);
    }

    [Fact]
    public void A_stream_with_no_length_gives_empty_bars_without_reading()
    {
        var stream = new FakeStream(2, Rate, (_, _) => 1f, TimeSpan.Zero);

        var result = EditPeaks.Compute(stream);

        Assert.Equal(EditPeaks.BarCount, result.Count);
        Assert.All(result, bar => Assert.Equal(0f, bar));
        Assert.Equal(0, stream.Reads);
    }

    [Fact]
    public void A_long_clip_is_read_in_pieces_never_whole()
    {
        // Ten minutes of stereo would be 57.6 million floats: each read asks for at most a second.
        const long frames = 10L * 60 * Rate;
        var stream = new FakeStream(2, frames, (_, _) => 0.1f);

        EditPeaks.Compute(stream);

        Assert.InRange(stream.LargestRead, 2, 2 * Rate);
        Assert.True(stream.Reads > 100);
    }

    [Fact]
    public void A_cancelled_computation_stops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var stream = new FakeStream(2, Rate, (_, _) => 0.5f);

        Assert.ThrowsAny<OperationCanceledException>(() => EditPeaks.Compute(stream, cancel: cancel.Token));
        Assert.Equal(0, stream.Reads);
    }

    [Fact]
    public void Silence_and_quiet_stay_at_zero_and_a_bad_bar_count_is_refused()
    {
        var quiet = new FakeStream(1, Rate, (_, _) => 0.0005f); // -66 dBFS: under the floor of the scale

        Assert.All(EditPeaks.Compute(quiet, 16), bar => Assert.Equal(0f, bar));
        Assert.Throws<ArgumentOutOfRangeException>(() => EditPeaks.Compute(new FakeStream(1, Rate, (_, _) => 0f), 0));
    }
}
