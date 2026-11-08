using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

public class SampleCodecTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 32767)]
    [InlineData(-1f, -32767)]
    [InlineData(0.5f, 16384)]
    [InlineData(-0.5f, -16384)]
    [InlineData(1.5f, 32767)]
    [InlineData(-3f, -32767)]
    [InlineData(float.PositiveInfinity, 32767)]
    [InlineData(float.NegativeInfinity, -32767)]
    public void Float_to_short_is_scaled_rounded_and_clipped(float input, short expected)
    {
        Assert.Equal(expected, SampleCodec.ToShort(input));
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(32767, 1f)]
    [InlineData(-32767, -1f)]
    [InlineData(16384, 0.50001526f)]
    [InlineData(-32768, -1.0000305f)]
    public void Short_to_float_is_fixed(short input, float expected)
    {
        Assert.Equal(expected, SampleCodec.ToFloat(input), 6);
    }

    [Fact]
    public void Every_stored_value_comes_back_unchanged()
    {
        for (var value = -32767; value <= 32767; value++)
        {
            Assert.Equal((short)value, SampleCodec.ToShort(SampleCodec.ToFloat((short)value)));
        }
    }

    [Fact]
    public void A_silent_buffer_stays_exactly_zero()
    {
        Assert.Equal(0f, SampleCodec.ToFloat(SampleCodec.ToShort(0f)));
    }

    [Fact]
    public void Spans_convert_like_single_samples()
    {
        float[] input = [0f, 1f, -1f, 2f, 0.5f];
        var stored = new short[5];
        var output = new float[5];

        SampleCodec.Encode(input, stored);
        SampleCodec.Decode(stored, output);

        Assert.Equal([0, 32767, -32767, 32767, 16384], stored);
        Assert.Equal(new[] { 0f, 1f, -1f, 1f, 16384f / 32767f }, output);
    }
}

[Collection(nameof(TimingSensitive))]
public class RingBufferTests
{
    private static float[] Samples(params int[] stored) => stored.Select(value => value / 32767f).ToArray();

    private static int[] Stored(float[] samples) => samples.Select(sample => (int)SampleCodec.ToShort(sample)).ToArray();

    [Fact]
    public void Keeps_the_latest_samples_across_the_wrap()
    {
        var ring = new RingBuffer(4);

        ring.Write(Samples(1, 2, 3));
        ring.Write(Samples(4, 5, 6));

        Assert.Equal([3, 4, 5, 6], Stored(ring.Snapshot(4)));
        Assert.Equal([5, 6], Stored(ring.Snapshot(2)));
        Assert.Equal(6, ring.Position);
        Assert.Equal(2, ring.OldestPosition);
    }

    [Fact]
    public void Gives_what_there_is_when_asked_for_more()
    {
        var ring = new RingBuffer(8);

        ring.Write(Samples(1, 2, 3));

        Assert.Equal([1, 2, 3], Stored(ring.Snapshot(8)));
        Assert.Empty(new RingBuffer(8).Snapshot(8));
    }

    [Fact]
    public void A_write_larger_than_the_buffer_keeps_its_end()
    {
        var ring = new RingBuffer(3);

        ring.Write(Samples(1));
        ring.Write(Samples(2, 3, 4, 5, 6));

        Assert.Equal([4, 5, 6], Stored(ring.Snapshot(3)));
        Assert.Equal(6, ring.Position);
    }

    [Fact]
    public void Silence_longer_than_the_buffer_replaces_it_and_keeps_counting()
    {
        var ring = new RingBuffer(4);
        ring.Write(Samples(1, 2, 3, 4, 5));

        ring.WriteSilence(1_000_000);
        ring.Write(Samples(7));

        Assert.Equal([0, 0, 0, 7], Stored(ring.Snapshot(4)));
        Assert.Equal(1_000_006, ring.Position);
    }

    [Fact]
    public void Silence_across_the_wrap_clears_only_its_own_slots()
    {
        var ring = new RingBuffer(4);
        ring.Write(Samples(1, 2, 3));

        ring.WriteSilence(2);
        ring.Write(Samples(9));

        Assert.Equal([3, 0, 0, 9], Stored(ring.Snapshot(4)));
    }

    [Fact]
    public void CopyTo_gives_zeros_for_what_is_not_held()
    {
        var ring = new RingBuffer(4);
        ring.Write(Samples(1, 2, 3, 4, 5, 6));
        var destination = new float[8];
        Array.Fill(destination, 0.5f);

        var delivered = ring.CopyTo(0, destination);

        // Positions 0 and 1 were overwritten, and 6 and 7 are not written yet.
        Assert.Equal(4, delivered);
        Assert.Equal([0, 0, 3, 4, 5, 6, 0, 0], Stored(destination));
    }

    [Fact]
    public void Starting_position_counts_from_there()
    {
        var ring = new RingBuffer(4, startPosition: 100);

        ring.Write(Samples(1, 2));

        Assert.Equal(102, ring.Position);
        Assert.Equal(100, ring.OldestPosition);
        var destination = new float[2];
        Assert.Equal(2, ring.CopyTo(100, destination));
        Assert.Equal([1, 2], Stored(destination));
    }

    [Fact]
    public void Writing_allocates_nothing()
    {
        var ring = new RingBuffer(1024);
        var packet = new float[700];
        ring.Write(packet);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ring.Write(packet);
            ring.WriteSilence(300);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Memory_is_two_bytes_per_sample()
    {
        Assert.Equal(2000, new RingBuffer(1000).MemoryBytes);
    }

    [Fact]
    public void A_reader_never_gets_a_sample_the_writer_replaced_during_the_copy()
    {
        // A tiny buffer and a writer flat out, so reads are overtaken all the time.
        const int Capacity = 4096;
        var ring = new RingBuffer(Capacity);
        var stop = false;
        var writer = new Thread(() =>
        {
            var packet = new float[256];
            long frame = 0;
            while (!Volatile.Read(ref stop))
            {
                for (var i = 0; i < packet.Length; i++)
                {
                    packet[i] = Ramp.Value(frame++);
                }

                ring.Write(packet);
            }
        });

        writer.Start();
        var destination = new float[Capacity];
        var overtaken = 0;
        var clean = 0;
        var deadline = Environment.TickCount64 + 500;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                var position = ring.OldestPosition;
                var delivered = ring.CopyTo(position, destination);
                if (delivered == Capacity)
                {
                    clean++;
                }
                else
                {
                    overtaken++;
                }

                AssertContinuousAfterLeadingZeros(destination);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }

        Assert.True(clean + overtaken > 0);
    }

    // What was delivered must be a run of the ramp with no jump: only zeros may stand for lost samples, and only first.
    private static void AssertContinuousAfterLeadingZeros(float[] samples)
    {
        var first = 0;
        while (first < samples.Length && samples[first] == 0f)
        {
            first++;
        }

        for (var i = first + 1; i < samples.Length; i++)
        {
            if (samples[i] == 0f)
            {
                // Not written yet at the time of the copy: the rest must be zeros too.
                Assert.All(samples[i..], sample => Assert.Equal(0f, sample));
                return;
            }

            var step = samples[i] - samples[i - 1];
            var expected = 0.8f / 20000;
            var isNext = Math.Abs(step - expected) < 0.0002f;
            var isPeriodWrap = step < -0.79f;
            Assert.True(isNext || isPeriodWrap, $"Jump of {step} at index {i}");
        }
    }
}

[Collection(nameof(TimingSensitive))]
public class RingBufferUnderAWriterAtFullSpeedTests
{
    [Fact]
    public void Snapshot_and_copy_stay_valid_when_the_writer_laps_the_buffer_between_two_reads()
    {
        const int Capacity = 2048;
        var ring = new RingBuffer(Capacity);
        var stop = false;
        var writer = new Thread(() =>
        {
            var packet = new float[6000];
            Array.Fill(packet, 0.25f);
            while (!Volatile.Read(ref stop))
            {
                ring.Write(packet);
            }
        });

        writer.Start();
        var longer = new float[Capacity * 3];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed.TotalSeconds < 2)
            {
                Assert.InRange(ring.Snapshot(Capacity).Length, 0, Capacity);
                ring.CopyTo(ring.OldestPosition, longer);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }
    }
}
