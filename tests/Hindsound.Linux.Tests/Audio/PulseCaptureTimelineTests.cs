using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>When the frames a capture reads were captured, from when they were read and how many came before.</summary>
public sealed class PulseCaptureTimelineTests
{
    private const int Rate = 48_000;
    private const int Packet = 960; // 20 ms
    private const long Start = 5_000;

    [Fact]
    public void A_reader_on_time_places_each_packet_where_it_was_read()
    {
        var timeline = new PulseCaptureTimeline(Rate);
        for (var i = 0; i < 50; i++)
        {
            var end = (i + 1) * 20;
            Assert.Equal((Start + end - 20, Start + end), timeline.Place(Start + end, (long)i * Packet, (long)(i + 1) * Packet));
        }
    }

    [Fact]
    public void A_late_reader_and_its_catch_up_burst_place_each_packet_when_it_was_captured_and_not_when_it_was_read()
    {
        var timeline = new PulseCaptureTimeline(Rate);
        long frame = 0;
        long ms = Start;

        // One second on time.
        for (var i = 0; i < 50; i++)
        {
            ms += 20;
            timeline.Place(ms, frame, frame + Packet);
            frame += Packet;
        }

        // Held 1.8 s, then the 90 packets captured meanwhile are all read within 10 ms, each where it was captured.
        var heldAt = ms;
        ms += 1_800;
        for (var i = 0; i < 90; i++)
        {
            var read = ms + (i / 9);
            Assert.Equal((heldAt + (i * 20), heldAt + ((i + 1) * 20)), timeline.Place(read, frame, frame + Packet));
            frame += Packet;
        }

        // Caught up: the next packets are placed where they were captured, a little before they are read.
        for (var i = 0; i < 10; i++)
        {
            var captured = heldAt + 1_800 + ((i + 1) * 20);
            Assert.Equal((captured - 20, captured), timeline.Place(captured + 3, frame, frame + Packet));
            frame += Packet;
        }
    }

    [Fact]
    public void A_late_reader_does_not_move_the_anchor_of_a_reader_that_was_on_time_and_a_jitter_never_places_a_packet_early()
    {
        var timeline = new PulseCaptureTimeline(Rate);
        long frame = 0;
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var captured = Start + ((i + 1) * 20);

            // Read on time once in a while, else up to 40 ms late.
            var read = captured + (i % 25 == 0 ? 0 : random.Next(1, 40));
            var (from, to) = timeline.Place(read, frame, frame + Packet);
            frame += Packet;
            Assert.Equal(captured, to);
            Assert.Equal(to - 20, from);
        }
    }

    [Theory]
    [InlineData(50)]
    [InlineData(-50)]
    public void A_drift_of_a_few_parts_per_million_between_the_two_clocks_is_followed(int ppm)
    {
        var timeline = new PulseCaptureTimeline(Rate);
        long frame = 0;
        var worst = 0.0;

        // Two minutes of packets, whose 960 frames last 20 ms on the server's clock and 20 ms plus the drift on this one.
        for (var i = 0; i < 6_000; i++)
        {
            var captured = Start + ((i + 1) * 20 * (1 + (ppm / 1e6)));
            var (_, to) = timeline.Place((long)Math.Ceiling(captured), frame, frame + Packet);
            frame += Packet;
            worst = Math.Max(worst, Math.Abs(to - captured));
        }

        Assert.True(worst <= 2, $"the drift took the placing {worst:0.0} ms away");
    }

    [Fact]
    public void A_stream_whose_position_starts_again_is_placed_from_its_own_start()
    {
        var timeline = new PulseCaptureTimeline(Rate);
        long frame = 0;
        long ms = Start;
        for (var i = 0; i < 50; i++)
        {
            ms += 20;
            timeline.Place(ms, frame, frame + Packet);
            frame += Packet;
        }

        // A new stream, a little later, whose frames count from 0 again.
        ms += 3_000;
        Assert.Equal((ms - 20, ms), timeline.Place(ms, 0, Packet));
        Assert.Equal((ms, ms + 20), timeline.Place(ms + 20, Packet, 2 * Packet));
    }

    [Fact]
    public void A_latency_that_grew_for_good_is_followed_once_the_old_reading_leaves_the_window()
    {
        var timeline = new PulseCaptureTimeline(Rate);
        long frame = 0;
        long ms = Start;
        ms += 20;
        timeline.Place(ms, frame, frame + Packet);
        frame += Packet;

        // From now on every packet is read 100 ms after the first one's anchor says.
        long to = 0;
        var elapsed = 0L;
        while (elapsed < PulseCaptureTimeline.WindowMs + 2_000)
        {
            ms += 20;
            elapsed += 20;
            (_, to) = timeline.Place(ms + 100, frame, frame + Packet);
            frame += Packet;
            if (elapsed < PulseCaptureTimeline.WindowMs)
            {
                Assert.Equal(ms, to);
            }
        }

        Assert.Equal(ms + 100, to);
    }

    [Fact]
    public void An_empty_packet_is_a_moment_and_a_rate_of_nothing_is_refused()
    {
        var timeline = new PulseCaptureTimeline(Rate);
        Assert.Equal((Start, Start), timeline.Place(Start, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PulseCaptureTimeline(0));
    }
}
