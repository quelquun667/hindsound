using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

public class PeakOfTests
{
    private static float[] Noise(int count, int seed)
    {
        var random = new Random(seed);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = (float)(random.NextDouble() * 1.6 - 0.8);
        }

        return samples;
    }

    private static int PeakThroughCopy(RingBuffer ring, long position, int count)
    {
        var copy = new float[count];
        ring.CopyTo(position, copy);
        var peak = 0;
        foreach (var sample in copy)
        {
            peak = Math.Max(peak, Math.Abs((int)SampleCodec.ToShort(sample)));
        }

        return peak;
    }

    [Fact]
    public void It_gives_what_a_copy_would_give_whatever_the_range()
    {
        var ring = new RingBuffer(5000);
        ring.Write(Noise(12_345, 1));
        var random = new Random(2);

        for (var i = 0; i < 2000; i++)
        {
            var count = random.Next(0, 5100);
            var position = random.NextInt64(ring.Position - 6000, ring.Position + 50);

            Assert.Equal(PeakThroughCopy(ring, position, count), ring.PeakOf(position, count));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(99)]
    public void The_loudest_sample_is_found_wherever_it_falls_in_the_vectors(int at)
    {
        var samples = new float[100];
        Array.Fill(samples, 0.01f);
        samples[at] = 0.5f;
        var ring = new RingBuffer(1000);
        ring.Write(samples);

        Assert.Equal(SampleCodec.ToShort(0.5f), ring.PeakOf(0, 100));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(40)]
    public void A_negative_sample_counts_by_its_size_on_any_length(int count)
    {
        var samples = new float[count];
        samples[count - 1] = -0.75f;
        var ring = new RingBuffer(1000);
        ring.Write(samples);

        Assert.Equal(-SampleCodec.ToShort(-0.75f), ring.PeakOf(0, count));
    }

    [Fact]
    public void Full_scale_both_ways_stays_at_the_top_of_the_scale()
    {
        var ring = new RingBuffer(100);
        ring.Write([1f, -1f]);

        Assert.Equal(32767, ring.PeakOf(0, 1));
        Assert.Equal(32767, ring.PeakOf(1, 1));
    }

    [Theory]
    [InlineData(95)]
    [InlineData(104)]
    public void Both_sides_of_the_join_are_read(int loudAt)
    {
        var ring = new RingBuffer(100);
        ring.Write(new float[60]);
        var samples = new float[60];
        samples[loudAt - 60] = 0.5f;
        ring.Write(samples);

        // Positions 90 to 110 sit on either side of the end of the array.
        Assert.Equal(SampleCodec.ToShort(0.5f), ring.PeakOf(90, 20, out var firstHeld));
        Assert.Equal(90, firstHeld);
    }

    [Fact]
    public void A_range_cut_by_the_join_does_not_read_what_is_outside_it()
    {
        var ring = new RingBuffer(100);
        ring.Write(new float[60]);
        var samples = new float[60];
        samples[20] = 0.5f;
        samples[49] = 0.5f;
        ring.Write(samples);

        // Slots 81 to 99, then 0 to 8: the slots of positions 80 and 109, both loud, are just outside the range.
        Assert.Equal(0, ring.PeakOf(81, 28));
    }

    [Fact]
    public void A_buffer_never_full_ends_at_what_is_written_and_starts_at_its_start()
    {
        var ring = new RingBuffer(1000, startPosition: 200);
        ring.Write([0.5f, 0.25f]);

        Assert.Equal(SampleCodec.ToShort(0.5f), ring.PeakOf(100, 200, out var firstHeld));
        Assert.Equal(200, firstHeld);
        Assert.Equal(SampleCodec.ToShort(0.25f), ring.PeakOf(201, 500));
    }

    [Fact]
    public void Nothing_held_gives_zero_and_says_where_held_audio_begins()
    {
        var ring = new RingBuffer(100);
        ring.Write(Noise(250, 3));

        Assert.Equal(0, ring.PeakOf(10, 20, out var tooOld));
        Assert.Equal(30, tooOld);
        Assert.Equal(0, ring.PeakOf(250, 20, out var notYet));
        Assert.Equal(250, notYet);
        Assert.Equal(0, ring.PeakOf(-40, 10, out var beforeAll));
        Assert.Equal(-30, beforeAll);
        Assert.Equal(0, ring.PeakOf(200, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.PeakOf(0, -1));
    }

    [Fact]
    public void A_range_partly_overwritten_says_where_it_stops_being_gone()
    {
        var ring = new RingBuffer(100);
        var samples = new float[250];
        samples[160] = 0.5f;
        ring.Write(samples);

        Assert.Equal(SampleCodec.ToShort(0.5f), ring.PeakOf(120, 100, out var firstHeld));
        Assert.Equal(150, firstHeld);
    }
}

public class PeakOfFramesTests
{
    [Fact]
    public void Every_channel_counts_and_frames_map_to_the_timeline()
    {
        var track = new SourceTrack(2, 1000, originFrame: 500);
        track.AppendContinuation([0f, 0f, 0f, 0.5f, 0f, 0f]);

        Assert.Equal(SampleCodec.ToShort(0.5f), track.PeakOfFrames(501, 1));
        Assert.Equal(0, track.PeakOfFrames(500, 1));
        Assert.Equal(0, track.PeakOfFrames(502, 1));
    }

    [Fact]
    public void Before_the_track_began_is_nothing_and_not_gone()
    {
        var track = new SourceTrack(1, 1000, originFrame: 500);
        track.AppendContinuation([0.5f]);

        Assert.Equal(0, track.PeakOfFrames(100, 50));
        Assert.Equal(SampleCodec.ToShort(0.5f), track.PeakOfFrames(400, 200));
    }

    [Fact]
    public void Not_written_yet_is_nothing_and_not_gone()
    {
        var track = new SourceTrack(1, 1000, 0);
        track.AppendContinuation([0.5f]);

        Assert.Equal(0, track.PeakOfFrames(10, 50));
        Assert.Equal(SampleCodec.ToShort(0.5f), track.PeakOfFrames(0, 50));
    }

    [Fact]
    public void A_range_the_writer_went_past_is_gone_even_in_part()
    {
        var track = new SourceTrack(1, 100, 0);
        track.AppendContinuation(new float[SourceTrack.SlackFrames + 300]);
        var oldest = track.OldestFrame;

        Assert.Equal(200, oldest);
        Assert.Equal(SourceTrack.PeakGone, track.PeakOfFrames(oldest - 10, 5));
        Assert.Equal(SourceTrack.PeakGone, track.PeakOfFrames(oldest - 1, 10));
        Assert.Equal(0, track.PeakOfFrames(oldest, 10));
    }

    [Fact]
    public void What_a_smaller_memory_dropped_is_gone()
    {
        var track = new SourceTrack(1, 48000, 0);
        track.AppendContinuation(new float[90000]);

        track.Resize(1000);
        Assert.Equal(41000, track.OldestFrame);

        Assert.Equal(SourceTrack.PeakGone, track.PeakOfFrames(track.OldestFrame - 1, 2));
        Assert.Equal(0, track.PeakOfFrames(track.OldestFrame, 2));
    }
}

[Collection(nameof(TimingSensitive))]
public class PeakOfBesideTheWriterTests
{
    private const int Packet = 1024;
    private const int Capacity = Packet * 64;
    private const int PacketsRead = 32;

    private static float Loudness(long packet) => (packet % 30000 + 1) / 30002f;

    // Each packet has its own loudness, so a peak from a lap the reader was not looking at shows.
    [Fact]
    public void A_value_the_writer_replaced_during_the_read_is_never_given_as_the_peak()
    {
        var ring = new RingBuffer(Capacity);
        var stop = false;
        var writer = new Thread(() =>
        {
            var samples = new float[Packet];
            for (long packet = 0; !Volatile.Read(ref stop); packet++)
            {
                Array.Fill(samples, Loudness(packet));
                ring.Write(samples);
            }
        });

        writer.Start();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Full, so every packet of the range is written by the time the expected peak is worked out.
            while (ring.Position < Capacity)
            {
                Thread.Yield();
            }

            while (clock.Elapsed.TotalSeconds < 1)
            {
                var first = (ring.OldestPosition + Packet - 1) / Packet;
                var peak = ring.PeakOf(first * Packet, PacketsRead * Packet, out var firstHeld);
                if (firstHeld > first * Packet)
                {
                    Assert.Equal(0, peak);
                    continue;
                }

                var expected = 0;
                for (var packet = first; packet < first + PacketsRead; packet++)
                {
                    expected = Math.Max(expected, SampleCodec.ToShort(Loudness(packet)));
                }

                Assert.Equal(expected, peak);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }
    }

    // Proved by logic and not by timing: with the writers' gate held, a writer cannot get through and a read still ends.
    [Fact]
    public void The_read_ends_while_the_write_gate_is_held()
    {
        var track = new SourceTrack(2, 48000, 0);
        track.AppendContinuation(new float[48000 * 2]);
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (track.WriteGate)
            {
                held.Set();
                release.Wait();
            }
        });
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(30)));

        var writer = Task.Run(() => track.AppendContinuation(new float[960]));
        try
        {
            Assert.False(writer.Wait(100), "The gate is not what writers wait on");

            var read = Task.Run(() => track.PeakOfFrames(0, 48000));

            Assert.True(read.Wait(TimeSpan.FromSeconds(30)), "The read waited for the writers");
            Assert.Equal(0, read.Result);
            Assert.False(writer.IsCompleted);
        }
        finally
        {
            release.Set();
            holder.Join();
        }

        Assert.True(writer.Wait(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Reading_allocates_nothing()
    {
        var track = new SourceTrack(2, 1000, 0);
        track.AppendContinuation(new float[(1000 + SourceTrack.SlackFrames) * 2]);
        track.AppendContinuation(new float[600]);
        _ = track.PeakOfFrames(track.OldestFrame, 500);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = track.PeakOfFrames(track.OldestFrame, 500);
            _ = track.PeakOfFrames(0, 10);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
