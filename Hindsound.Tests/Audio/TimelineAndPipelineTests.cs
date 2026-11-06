using System.Runtime.InteropServices;
using Hindsound.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

public class SilencePadderTests
{
    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(1000, 1096)]
    [InlineData(1000, 904)]
    public void Jitter_is_left_alone(long writeEnd, long packetStart)
    {
        Assert.Equal(default, SilencePadder.Place(writeEnd, packetStart, 480, exact: false));
    }

    [Fact]
    public void Slow_clock_repeats_one_frame()
    {
        Assert.Equal(new PacketPlacement(0, 0, true), SilencePadder.Place(1000, 1100, 480, exact: false));
    }

    [Fact]
    public void Fast_clock_drops_one_frame()
    {
        Assert.Equal(new PacketPlacement(0, 1, false), SilencePadder.Place(1000, 900, 480, exact: false));
    }

    [Fact]
    public void A_hole_becomes_zeros_of_exactly_its_length()
    {
        Assert.Equal(new PacketPlacement(480_000, 0, false), SilencePadder.Place(1000, 481_000, 480, exact: false));
    }

    [Fact]
    public void A_large_overlap_drops_what_overlaps()
    {
        Assert.Equal(new PacketPlacement(0, 200, false), SilencePadder.Place(5000, 3800 + 1000, 480, exact: true));
        Assert.Equal(new PacketPlacement(0, 480, false), SilencePadder.Place(5000, 1000, 480, exact: false));
    }

    [Fact]
    public void After_an_interruption_even_a_small_error_is_placed_exactly()
    {
        Assert.Equal(new PacketPlacement(10, 0, false), SilencePadder.Place(1000, 1010, 480, exact: true));
        Assert.Equal(new PacketPlacement(0, 10, false), SilencePadder.Place(1000, 990, 480, exact: true));
    }
}

public class CaptureTimelineTests
{
    [Fact]
    public void Counts_48_frames_per_millisecond()
    {
        var clock = new FakeClock();
        var timeline = new CaptureTimeline(() => clock.Ticks);

        clock.Advance(1);

        Assert.Equal(48000, timeline.NowFrames);
        Assert.Equal(480, timeline.ToFrames(clock.Ticks - 9_900_000));
    }

    [Fact]
    public void Stands_still_while_frozen_and_carries_on_from_there()
    {
        var clock = new FakeClock();
        var timeline = new CaptureTimeline(() => clock.Ticks);
        clock.Advance(1);

        timeline.Freeze();
        clock.Advance(100);
        Assert.Equal(48000, timeline.NowFrames);

        timeline.Resume();
        Assert.Equal(48000, timeline.NowFrames);

        clock.Advance(1);
        Assert.Equal(96000, timeline.NowFrames);
    }

    [Fact]
    public void Does_not_overflow_after_a_year()
    {
        var clock = new FakeClock();
        var timeline = new CaptureTimeline(() => clock.Ticks);

        clock.Advance(365.0 * 24 * 3600);

        Assert.Equal(365L * 24 * 3600 * 48000, timeline.NowFrames);
    }
}

public class CapturePipelineTests
{
    private readonly FakeClock _clock = new();
    private readonly CaptureTimeline _timeline;

    public CapturePipelineTests() => _timeline = new CaptureTimeline(() => _clock.Ticks);

    private static ReadOnlySpan<byte> AsBytes(float[] samples) => MemoryMarshal.AsBytes(samples.AsSpan());

    private CapturePipeline StereoPipeline(SourceTrack track) =>
        new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), track, _timeline);

    [Fact]
    public void A_late_callback_is_placed_by_its_stamp_and_not_by_when_it_ran()
    {
        var track = new SourceTrack(2, 48000, 0);
        var pipeline = StereoPipeline(track);
        var stamp = _clock.Ticks + 1_000_000; // 100 ms in.
        _clock.Advance(0.5); // The callback only ran 500 ms in.

        pipeline.OnPacket(AsBytes(new float[960]), AudioClientBufferFlags.None, 0, stamp);

        Assert.Equal(4800 + 480, track.EndFrame);
    }

    [Theory]
    [InlineData(AudioClientBufferFlags.TimestampError, 1_000_000_000L + 5_000_000)]
    [InlineData(AudioClientBufferFlags.None, 0L)]
    [InlineData(AudioClientBufferFlags.None, 999_999_999_999L)]
    public void Without_a_usable_stamp_the_packet_ends_now(AudioClientBufferFlags flags, long stamp)
    {
        var track = new SourceTrack(2, 48000, 0);
        var pipeline = StereoPipeline(track);
        _clock.Advance(1);

        pipeline.OnPacket(AsBytes(new float[960]), flags, 0, stamp);

        Assert.Equal(48000, track.EndFrame);
    }

    [Fact]
    public void Flagged_silence_is_stored_as_zeros_at_its_place()
    {
        var track = new SourceTrack(1, 48000, 0);
        var pipeline = new CapturePipeline(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), track, _timeline);
        var loud = Enumerable.Repeat(0.5f, 480).ToArray();

        pipeline.OnPacket(AsBytes(loud), AudioClientBufferFlags.None, 0, _clock.Ticks);
        pipeline.OnPacket(AsBytes(loud), AudioClientBufferFlags.Silent, 0, _clock.Ticks + 100_000);

        var stored = new float[960];
        track.CopyFrames(0, stored);
        Assert.Equal(960, track.EndFrame);
        Assert.Equal(0.5f, stored[479], 3);
        Assert.Equal(0f, stored[480]);
    }

    [Fact]
    public void A_discontinuity_flag_places_the_packet_exactly()
    {
        var track = new SourceTrack(1, 48000, 0);
        var pipeline = new CapturePipeline(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), track, _timeline);
        var packet = new float[480];

        pipeline.OnPacket(AsBytes(packet), AudioClientBufferFlags.None, 0, _clock.Ticks);
        pipeline.OnPacket(AsBytes(packet), AudioClientBufferFlags.DataDiscontinuity, 0, _clock.Ticks + 100_000 + 2000);

        // 2000 ticks are 9.6 frames, within drift range: only the flag makes it exact.
        Assert.Equal(480 + 9 + 480, track.EndFrame);
    }

    [Fact]
    public void A_packet_larger_than_one_conversion_is_kept_whole_and_in_order()
    {
        var track = new SourceTrack(1, 48000, 0);
        var pipeline = new CapturePipeline(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), track, _timeline);
        var count = AudioFormatConverter.MaxInputFrames * 2 + 100;
        var packet = Enumerable.Range(0, count).Select(frame => Ramp.Value(frame)).ToArray();

        pipeline.OnPacket(AsBytes(packet), AudioClientBufferFlags.None, 0, _clock.Ticks);

        var stored = new float[count];
        Assert.Equal(count, track.CopyFrames(0, stored));
        for (var frame = 0; frame < count; frame++)
        {
            Assert.Equal(Ramp.ThroughBuffer(frame), stored[frame]);
        }
    }

    [Fact]
    public void Nothing_is_kept_while_the_timeline_is_frozen()
    {
        var track = new SourceTrack(2, 48000, 0);
        var pipeline = StereoPipeline(track);
        _timeline.Freeze();

        pipeline.OnPacket(AsBytes(new float[960]), AudioClientBufferFlags.None, 0, _clock.Ticks);

        Assert.Equal(0, track.EndFrame);
    }

    [Fact]
    public void A_sound_card_clock_off_by_200_ppm_stays_in_place_without_a_hole()
    {
        var track = new SourceTrack(1, 48000 * 120, 0);
        var pipeline = new CapturePipeline(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), track, _timeline);
        var packet = Enumerable.Repeat(0.5f, 480).ToArray();
        var start = _clock.Ticks;

        // In 60 s of packets, the card delivers 200 ppm less than the stamps say: 576 frames short without correction.
        for (var i = 0; i < 6000; i++)
        {
            var stamp = start + (long)(i * 100_020.0 * 1.0);
            _clock.Ticks = stamp + 100_000;
            pipeline.OnPacket(AsBytes(packet), AudioClientBufferFlags.None, 0, stamp);
        }

        var error = track.EndFrame - _timeline.NowFrames;
        Assert.InRange(error, -SilencePadder.JitterFrames - 480, SilencePadder.JitterFrames + 480);

        var stored = new float[track.EndFrame];
        track.CopyFrames(0, stored);
        Assert.DoesNotContain(0f, stored);
    }

    [Theory]
    [InlineData(48000, 2)]
    [InlineData(44100, 2)]
    public void The_whole_callback_allocates_nothing(int rate, int channels)
    {
        var track = new SourceTrack(2, 48000, 0);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        var pipeline = new CapturePipeline(format, track, _timeline);
        var packet = new float[(rate / 100) * channels];

        void Deliver(int index)
        {
            var flags = index % 7 == 0 ? AudioClientBufferFlags.Silent : AudioClientBufferFlags.None;
            pipeline.OnPacket(MemoryMarshal.AsBytes(packet.AsSpan()), flags, 0, _clock.Ticks + index * 100_000L);
        }

        Deliver(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i < 2000; i++)
        {
            Deliver(i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

public class SourceTrackTests
{
    private static float[] Ramped(long firstFrame, int frames, int channels = 1)
    {
        var samples = new float[frames * channels];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] = Ramp.Value(firstFrame + frame);
            }
        }

        return samples;
    }

    [Fact]
    public void A_five_minute_buffer_takes_about_86_megabytes()
    {
        var capacity = 5 * 60 * 48000;

        var system = new SourceTrack(InternalFormat.SystemChannels, capacity, 0);
        var mic = new SourceTrack(InternalFormat.MicChannels, capacity, 0);

        Assert.Equal(192_000, InternalFormat.SystemBytesPerSecond);
        Assert.Equal(96_000, InternalFormat.MicBytesPerSecond);
        Assert.Equal(301 * 192_000L, system.MemoryBytes);
        Assert.Equal(301 * 96_000L, mic.MemoryBytes);
        Assert.Equal(86_688_000L, system.MemoryBytes + mic.MemoryBytes);
    }

    [Fact]
    public void Time_before_the_first_packet_is_silence()
    {
        var track = new SourceTrack(1, 4800, 1000);

        track.Append(1500, Ramped(1500, 100), exact: true);

        Assert.Equal(1000, track.OldestFrame);
        Assert.Equal(1600, track.EndFrame);
        var stored = new float[600];
        track.CopyFrames(1000, stored);
        Assert.Equal(0f, stored[499]);
        Assert.Equal(Ramp.ThroughBuffer(1500), stored[500]);
    }

    [Fact]
    public void Stereo_positions_count_in_frames()
    {
        var track = new SourceTrack(2, 4800, 0);

        track.Append(100, Ramped(100, 50, 2), exact: true);

        Assert.Equal(150, track.EndFrame);
        var stored = new float[4];
        track.CopyFrames(100, stored);
        Assert.Equal(Ramp.ThroughBuffer(101), stored[2]);
        Assert.Equal(Ramp.ThroughBuffer(101), stored[3]);
    }

    [Fact]
    public void Padding_stops_at_the_frame_asked_for_and_never_goes_back()
    {
        var track = new SourceTrack(2, 4800, 0);
        track.Append(0, Ramped(0, 100, 2), exact: true);

        track.PadTo(150);
        Assert.Equal(150, track.EndFrame);

        track.PadTo(120);
        Assert.Equal(150, track.EndFrame);
    }

    [Fact]
    public void Growing_keeps_everything_on_its_frame()
    {
        var track = new SourceTrack(1, 1000, 0);
        track.Append(0, Ramped(0, 5000), exact: true);

        track.Resize(20000);
        track.Append(5000, Ramped(5000, 1000), exact: false);

        Assert.Equal(6000, track.EndFrame);
        Assert.Equal(0, track.OldestFrame);
        var stored = new float[6000];
        Assert.Equal(6000, track.CopyFrames(0, stored));
        for (var frame = 0; frame < 6000; frame++)
        {
            Assert.Equal(Ramp.ThroughBuffer(frame), stored[frame]);
        }
    }

    [Fact]
    public void Shrinking_keeps_the_newest_audio()
    {
        var track = new SourceTrack(1, 100_000, 0);
        track.Append(0, Ramped(0, 90_000), exact: true);

        track.Resize(1000);

        Assert.Equal(90_000, track.EndFrame);
        Assert.Equal(90_000 - 1000 - SourceTrack.SlackFrames, track.OldestFrame);
        var stored = new float[1000];
        Assert.Equal(1000, track.CopyFrames(89_000, stored));
        Assert.Equal(Ramp.ThroughBuffer(89_000), stored[0]);
        Assert.Equal(Ramp.ThroughBuffer(89_999), stored[999]);
    }

    [Fact]
    public void Resizing_during_writes_loses_nothing()
    {
        var track = new SourceTrack(1, 48000, 0);
        var stop = false;
        var written = 0L;

        // At the pace of real audio, however the machine schedules this thread.
        var writer = new Thread(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var frame = 0L;
            while (!Volatile.Read(ref stop))
            {
                var due = (long)(clock.Elapsed.TotalMilliseconds * 48);
                while (frame + 480 <= due)
                {
                    track.Append(frame, Ramped(frame, 480), exact: false);
                    frame += 480;
                    Volatile.Write(ref written, frame);
                }

                Thread.Sleep(1);
            }
        });

        writer.Start();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref written) >= 4800, TimeSpan.FromSeconds(60)));
            track.Resize(96000);
            track.Resize(240000);
            var afterResizes = Volatile.Read(ref written);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref written) >= afterResizes + 4800, TimeSpan.FromSeconds(60)));
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }

        var end = track.EndFrame;
        Assert.Equal(Volatile.Read(ref written), end);
        var oldest = track.OldestFrame;
        var stored = new float[end - oldest];
        Assert.Equal(stored.Length, track.CopyFrames(oldest, stored));
        for (var i = 0; i < stored.Length; i++)
        {
            if (stored[i] != Ramp.ThroughBuffer(oldest + i))
            {
                Assert.Fail($"Frame {oldest + i} is wrong: {stored[i]}");
            }
        }
    }
}
public class DeviceWatcherTests
{
    [Fact]
    public async Task Many_notifications_make_one_event()
    {
        var delay = TimeSpan.FromMilliseconds(50);
        using var watcher = new DeviceWatcher(delay);
        var fired = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.DevicesChanged += () =>
        {
            Interlocked.Increment(ref fired);
            first.TrySetResult();
        };

        // An event can only slip in where this thread itself stalled for longer than the delay, so that is counted
        // and not assumed away. On a machine that keeps up there are none, and this says exactly one event.
        var stalls = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            var before = clock.Elapsed;
            watcher.Signal();
            if (i > 0 && before - previous >= delay)
            {
                stalls++;
            }

            previous = clock.Elapsed;
        }

        await first.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await Task.Delay(delay * 4);
        Assert.InRange(Volatile.Read(ref fired), 1, 1 + stalls);
    }

    private static TimeSpan previous;
}
public class SnapshotTruthTests
{
    private static SourceTrack Track(int channels, int capacity, long origin, long firstFrame, int frames)
    {
        var track = new SourceTrack(channels, capacity, origin);
        var samples = new float[frames * channels];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] = Ramp.Value(firstFrame + frame);
            }
        }

        track.Append(firstFrame, samples, exact: true);
        return track;
    }

    [Fact]
    public void A_window_overtaken_by_the_writer_loses_the_same_head_on_both_sources()
    {
        // Both hold 1000 + 48000 frames: the PC from frame 11 000, the microphone from frame 21 000.
        var system = Track(2, 1000, 0, 0, 60_000);
        var mic = Track(1, 1000, 0, 0, 70_000);

        var snapshot = AudioSnapshot.CopyFrom(system, mic, 5000, 50_000);

        Assert.Equal(34_000, snapshot.Mic.Length);
        Assert.Equal(68_000, snapshot.System.Length);
        Assert.Equal(Ramp.ThroughBuffer(21_000), snapshot.Mic[0]);
        Assert.Equal(Ramp.ThroughBuffer(21_000), snapshot.System[0]);
        Assert.Equal(Ramp.ThroughBuffer(54_999), snapshot.Mic[^1]);
        Assert.DoesNotContain(0f, snapshot.Mic);
        Assert.DoesNotContain(0f, snapshot.System);
    }

    [Fact]
    public void A_resize_between_the_window_and_the_copy_shortens_the_clip_and_adds_no_silence()
    {
        var system = Track(2, 100_000, 0, 0, 90_000);
        var mic = Track(1, 100_000, 0, 0, 90_000);
        var start = system.EndFrame - 80_000;

        system.Resize(1000);
        mic.Resize(1000);
        var snapshot = AudioSnapshot.CopyFrom(system, mic, start, 80_000);

        Assert.Equal(49_000, snapshot.Mic.Length);
        Assert.Equal(98_000, snapshot.System.Length);
        Assert.Equal(Ramp.ThroughBuffer(41_000), snapshot.Mic[0]);
        Assert.Equal(Ramp.ThroughBuffer(89_999), snapshot.System[^1]);
        Assert.DoesNotContain(0f, snapshot.Mic);
        Assert.DoesNotContain(0f, snapshot.System);
    }

    [Fact]
    public void A_window_entirely_gone_gives_an_empty_clip()
    {
        var system = Track(2, 1000, 0, 0, 200_000);
        var mic = Track(1, 1000, 0, 0, 200_000);

        var snapshot = AudioSnapshot.CopyFrom(system, mic, 1000, 5000);

        Assert.Empty(snapshot.System);
        Assert.Empty(snapshot.Mic);
    }

    [Fact]
    public void A_lost_head_with_the_mic_off_leaves_the_mic_empty()
    {
        var system = Track(2, 1000, 0, 0, 60_000);

        var snapshot = AudioSnapshot.CopyFrom(system, null, 5000, 50_000);

        Assert.Empty(snapshot.Mic);
        Assert.Equal(88_000, snapshot.System.Length);
    }

    [Fact]
    public void Real_silence_is_kept_a_source_that_began_later_or_ended_earlier_cuts_nothing()
    {
        var late = Track(1, 10_000, 3000, 3000, 2000);
        var early = Track(2, 10_000, 0, 0, 2000);

        var snapshot = AudioSnapshot.CopyFrom(early, late, 0, 5000);

        Assert.Equal(5000, snapshot.Mic.Length);
        Assert.Equal(10_000, snapshot.System.Length);
        Assert.Equal(0f, snapshot.Mic[2999]);
        Assert.Equal(Ramp.ThroughBuffer(3000), snapshot.Mic[3000]);
        Assert.Equal(Ramp.ThroughBuffer(1999), snapshot.System[3999]);
        Assert.Equal(0f, snapshot.System[4000]);
    }
}

[Collection(nameof(TimingSensitive))]
public class ResizeUnderAWriterAtFullSpeedTests
{
    [Fact]
    public void A_resize_overtaken_by_the_writer_holds_only_real_audio()
    {
        var track = new SourceTrack(1, 1000, 0);
        var stop = false;
        var written = 0L;
        var writer = new Thread(() =>
        {
            var packet = new float[480];
            var frame = 0L;
            while (!Volatile.Read(ref stop))
            {
                for (var i = 0; i < packet.Length; i++)
                {
                    packet[i] = Ramp.Value(frame + i);
                }

                track.Append(frame, packet, exact: false);
                frame += packet.Length;
                Volatile.Write(ref written, frame);
            }
        });

        writer.Start();
        var stored = new float[10_000];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed.TotalSeconds < 2)
            {
                // Full, so the oldest frames are the ones being overwritten when the copy to the larger buffer starts.
                track.Resize(1000);
                var filled = Volatile.Read(ref written);
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref written) > filled + 60_000, TimeSpan.FromSeconds(30)));
                track.Resize(400_000);

                var oldest = track.OldestFrame;
                var lost = track.CopyWindow(oldest, stored);
                for (var i = lost; i < stored.Length; i++)
                {
                    if (stored[i] != Ramp.ThroughBuffer(oldest + i))
                    {
                        Assert.Fail($"Frame {oldest + i} is wrong: {stored[i]}");
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }
    }
}
