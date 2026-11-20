using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The mix of an application's stream when the card's clock and the capture's are not quite the same: ten minutes simulated,
/// the server's packets of 20 ms coming at the card's speed, the capture reading by about 10 ms at its own.
/// </summary>
public sealed class PulseAppMixerClockTests
{
    private const int Rate = 48000;
    private const int Channels = 2;
    private const int Fragment = Rate / 50;
    private const int Prime = 2 * Fragment;
    private const int Max = Rate / 4;
    private const int Wrap = 1 << 22;
    private static readonly PulseMonitorKey A = new(1, 0);

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void A_card_off_by_100_ppm_for_ten_minutes_leaves_no_hole_and_its_delay_does_not_grow(int ppm)
    {
        var mixer = new PulseAppMixer(Channels, Prime, Max);
        mixer.Add(A);
        var random = new Random(ppm + 1000);
        var packet = new float[Fragment * Channels];
        var output = new float[Rate / 10 * Channels];
        var packetEvery = Fragment / (1 + (ppm / 1e6));
        var nextPacket = 0.0;
        long produced = 0;
        long now = 0;
        float? last = null;
        var holes = 0;
        var jumps = new List<string>();
        var (low, high) = (int.MaxValue, int.MinValue);
        double early = 0, late = 0;
        int earlyReads = 0, lateReads = 0;
        while (now < 600L * Rate)
        {
            // About 10 ms a read, as the capture's thread wakes: from 5 to 15 ms.
            var read = random.Next(Rate / 200, (3 * Rate / 200) + 1);
            now += read;
            while (nextPacket <= now)
            {
                // The first channel holds a constant, so a hole shows as zero; the second, which frame of the card it is.
                for (var frame = 0; frame < Fragment; frame++)
                {
                    packet[frame * Channels] = 0.5f;
                    packet[(frame * Channels) + 1] = (produced + frame) % Wrap;
                }

                mixer.Write(A, packet);
                produced += Fragment;
                nextPacket += packetEvery;
            }

            var held = mixer.Held(A);
            var span = output.AsSpan(0, read * Channels);
            mixer.Mix(span);
            if (last is null && span[0] == 0)
            {
                continue;
            }

            for (var frame = 0; frame < read; frame++)
            {
                if (span[frame * Channels] != 0.5f)
                {
                    holes++;
                }
            }

            // From one read to the next, the card's frames go on by what was read, one more or one less at most.
            var end = span[((read - 1) * Channels) + 1];
            if (last is { } previous)
            {
                var step = ((end - previous) % Wrap + Wrap) % Wrap;
                if (Math.Abs(step - read) > 1)
                {
                    jumps.Add($"{now / (double)Rate:0.00} s: {step} for {read}");
                }
            }

            last = end;
            if (now < 10L * Rate)
            {
                continue;
            }

            (low, high) = (Math.Min(low, held), Math.Max(high, held));
            if (now < 70L * Rate)
            {
                early += held;
                earlyReads++;
            }
            else if (now >= 540L * Rate)
            {
                late += held;
                lateReads++;
            }
        }

        (early, late) = (early / earlyReads, late / lateReads);
        var margin = mixer.MarginFrames;
        var summary = $"{ppm} ppm: held from {low} to {high} frames, {early:0} on average in the first minute, {late:0} in the last, {holes} frames of hole, jumps: {string.Join("; ", jumps.Take(5))}";
        Assert.True(holes == 0, summary);
        Assert.True(jumps.Count == 0, summary);
        Assert.True(low >= Prime - margin - Fragment && high <= Prime + margin + Fragment, summary);
        Assert.True(Math.Abs(late - early) <= 2 * margin, summary);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(10, 3, 2)]
    [InlineData(-10, 5, 3)]
    [InlineData(7, 5, 4)]
    [InlineData(10, 7, 5)]
    [InlineData(-10, 10, 6)]
    [InlineData(0, 10, 1)]
    [InlineData(5, 10, 7)]
    public void Equal_clocks_with_jitter_start_with_at_most_two_corrections(int phaseMilliseconds, int jitterMilliseconds, int seed)
    {
        // Packets of 20 ms, read by 10 ms, each packet up to 10 ms early or late and the first one up to 10 ms off the reads.
        var mixer = new PulseAppMixer(Channels, Prime, Max);
        mixer.Add(A);
        var random = new Random(seed);
        var packet = new float[Fragment * Channels];
        Array.Fill(packet, 0.5f);
        var output = new float[Rate / 100 * Channels];
        var next = (phaseMilliseconds + 10) * Rate / 1000;
        var arrival = next;
        var holes = 0;
        var started = false;
        for (long now = 0; now < 60L * Rate; now += Rate / 100)
        {
            while (arrival <= now)
            {
                mixer.Write(A, packet);
                next += Fragment;
                var jitter = random.Next(-jitterMilliseconds * Rate / 1000, (jitterMilliseconds * Rate / 1000) + 1);
                arrival = Math.Max(arrival + 1, next + jitter);
            }

            var heard = mixer.Mix(output);
            started |= heard;
            if (started && !heard)
            {
                holes++;
            }
        }

        Assert.True(mixer.Corrections <= 2, $"{mixer.Corrections} corrections, {holes} reads without sound");
    }

    [Fact]
    public void A_stream_on_time_is_given_as_it_is()
    {
        // The packets come as fast as they are read: nothing is skipped nor given twice.
        var mixer = new PulseAppMixer(1, Prime, Max);
        mixer.Add(A);
        var packet = new float[Fragment];
        var output = new float[Fragment / 2];
        var next = 0f;
        var expected = 0f;
        for (var round = 0; round < 3000; round++)
        {
            if (round % 2 == 0)
            {
                for (var i = 0; i < packet.Length; i++)
                {
                    packet[i] = next++;
                }

                mixer.Write(A, packet);
            }

            if (!mixer.Mix(output))
            {
                continue;
            }

            foreach (var sample in output)
            {
                Assert.Equal(expected++, sample);
            }
        }

        Assert.True(expected > 1000 * output.Length, $"only {expected} samples read");
    }
}
