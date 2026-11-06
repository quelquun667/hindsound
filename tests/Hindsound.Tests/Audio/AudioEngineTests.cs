using System.Diagnostics;
using System.Runtime.InteropServices;
using Hindsound.Audio;
using Xunit.Abstractions;

namespace Hindsound.Tests.Audio;

public class AudioEngineTests
{
    private const int FramesPerSecond = InternalFormat.SampleRate;

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    // Every frame: the right channel mirrors the left, and the microphone holds the same wall-clock frame, or is silent.
    private static void AssertAligned(AudioSnapshot snapshot)
    {
        Assert.Equal(snapshot.Mic.Length * 2, snapshot.System.Length);
        for (var frame = 0; frame < snapshot.Mic.Length; frame++)
        {
            var left = snapshot.System[frame * 2];
            var right = snapshot.System[frame * 2 + 1];
            var mic = snapshot.Mic[frame];
            if (right != -left)
            {
                Assert.Fail($"Channels differ at frame {frame}: {left} and {right}");
            }

            if (left != 0f && mic != 0f && left != mic)
            {
                Assert.Fail($"Sources differ at frame {frame}: {left} and {mic}");
            }
        }
    }

    private static int LongestZeroRun(float[] samples, int channels)
    {
        var longest = 0;
        var current = 0;
        for (var i = 0; i < samples.Length; i += channels)
        {
            current = samples[i] == 0f ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    [Fact]
    public async Task Both_sources_come_back_aligned_ending_at_the_same_instant()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        rig.Run(5);
        var snapshot = rig.Engine.Snapshot(Seconds(3));

        Assert.Equal(3 * FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(3 * 2 * FramesPerSecond, snapshot.System.Length);
        Assert.Equal(Seconds(3), snapshot.Duration);
        AssertAligned(snapshot);
        Assert.Equal(0, LongestZeroRun(snapshot.Mic, 1));
        Assert.Equal(0, LongestZeroRun(snapshot.System, 2));

        // It holds the last 3 seconds, up to the very last frame.
        Assert.Equal(Ramp.ThroughBuffer(5 * FramesPerSecond - 1), snapshot.Mic[^1]);
        Assert.Equal(Ramp.ThroughBuffer(2 * FramesPerSecond), snapshot.Mic[0]);
    }

    [Fact]
    public async Task With_less_than_asked_it_gives_what_there_is()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        rig.Run(2);
        var snapshot = rig.Engine.Snapshot(Seconds(30));

        Assert.Equal(2 * FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(Seconds(2), snapshot.Duration);
        Assert.Equal(Ramp.ThroughBuffer(0), snapshot.Mic[0]);
        AssertAligned(snapshot);
    }

    [Fact]
    public async Task Asking_for_more_than_the_buffer_gives_the_buffer()
    {
        using var rig = new AudioTestRig(bufferSeconds: 4);
        await rig.Engine.StartAsync();

        rig.Run(10);
        var snapshot = rig.Engine.Snapshot(Seconds(60));

        Assert.Equal(4 * FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(0, LongestZeroRun(snapshot.Mic, 1));
        AssertAligned(snapshot);
    }

    [Fact]
    public async Task Ten_seconds_of_pc_silence_stay_in_place_and_the_end_stays_aligned()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        rig.Run(5);
        rig.Run(10, system: false);
        rig.Run(5);
        var snapshot = rig.Engine.Snapshot(Seconds(20));

        Assert.Equal(20 * FramesPerSecond, snapshot.Mic.Length);
        AssertAligned(snapshot);

        // Mic heard throughout, the PC silent for exactly those 10 seconds.
        Assert.Equal(0, LongestZeroRun(snapshot.Mic, 1));
        Assert.Equal(10 * FramesPerSecond, LongestZeroRun(snapshot.System, 2));
        Assert.Equal(0f, snapshot.System[5 * FramesPerSecond * 2]);
        Assert.NotEqual(0f, snapshot.System[(5 * FramesPerSecond - 1) * 2]);
        Assert.Equal(0f, snapshot.System[(15 * FramesPerSecond - 1) * 2]);
        Assert.Equal(Ramp.ThroughBuffer(15 * FramesPerSecond), snapshot.System[15 * FramesPerSecond * 2]);
    }

    [Fact]
    public async Task A_silent_pc_at_the_end_of_the_clip_does_not_cut_the_clip_short()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        rig.Run(5);
        rig.Run(4, system: false);
        var snapshot = rig.Engine.Snapshot(Seconds(9));

        Assert.Equal(9 * FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(Ramp.ThroughBuffer(9 * FramesPerSecond - 1), snapshot.Mic[^1]);
        Assert.Equal(0f, snapshot.System[^2]);
        AssertAligned(snapshot);
    }

    [Fact]
    public async Task A_silent_pc_and_no_mic_still_gives_a_silent_clip_of_the_right_length()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        await rig.Engine.StartAsync();

        rig.Clock.Advance(12);
        var snapshot = rig.Engine.Snapshot(Seconds(10));

        Assert.Equal(10 * 2 * FramesPerSecond, snapshot.System.Length);
        Assert.All(snapshot.System, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public async Task An_active_source_is_not_cut_for_a_packet_still_on_its_way()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        rig.Run(1);
        rig.Clock.Advance(0.05);

        // Both are live, only 50 ms behind: the clip ends where the sources do, with no zeros at the end.
        var snapshot = rig.Engine.Snapshot(Seconds(1));

        Assert.Equal(Ramp.ThroughBuffer(FramesPerSecond - 1), snapshot.Mic[^1]);
        Assert.Equal(Ramp.ThroughBuffer(FramesPerSecond - 1), snapshot.System[^2]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sources_a_little_apart_end_together_at_the_older_end_with_no_zeros(bool micBehind)
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        rig.Run(1);
        rig.Run(0.05, system: micBehind, mic: !micBehind);
        var snapshot = rig.Engine.Snapshot(Seconds(1));

        // One source is 50 ms ahead: still live, so the clip stops where the other one has got to.
        Assert.Equal(FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(Ramp.ThroughBuffer(FramesPerSecond - 1), snapshot.Mic[^1]);
        Assert.Equal(Ramp.ThroughBuffer(FramesPerSecond - 1), snapshot.System[^2]);
        AssertAligned(snapshot);
    }

    [Fact]
    public async Task A_mic_at_44100_16_bit_stereo_lines_up_with_the_pc_sound()
    {
        using var rig = new AudioTestRig();
        rig.Factory.MicFormat = new NAudio.Wave.WaveFormat(44100, 16, 2);
        await rig.Engine.StartAsync();

        // Both sources go from silence to a tone edge at exactly the same moment, 2 s in.
        var systemPacket = new float[960];
        var micPacket = new short[441 * 2];
        for (var packet = 0; packet < 300; packet++)
        {
            var stamp = rig.Clock.Ticks;
            rig.Clock.Advance(0.01);
            var level = packet >= 200 ? 0.5f : 0f;
            Array.Fill(systemPacket, level);
            Array.Fill(micPacket, (short)(level * 32768));
            rig.SystemSource.Deliver(systemPacket, stamp);
            rig.MicSource.DeliverBytes(MemoryMarshal.AsBytes(micPacket.AsSpan()), stamp);
        }

        var snapshot = rig.Engine.Snapshot(Seconds(3));

        Assert.Equal(snapshot.Mic.Length * 2, snapshot.System.Length);
        Assert.InRange(snapshot.Duration.TotalSeconds, 2.99, 3.0);

        // The edge lands at the same place in both, to within the 2 ms the sync lets through.
        var systemEdge = Array.FindIndex(snapshot.System, sample => sample > 0.25f) / 2;
        var micEdge = Array.FindIndex(snapshot.Mic, sample => sample > 0.25f);
        Assert.InRange(micEdge - systemEdge, -SilencePadder.JitterFrames, SilencePadder.JitterFrames);

        Assert.Equal(0.5f, snapshot.Mic[^1], 3);
    }

    [Fact]
    public async Task With_the_mic_off_there_is_no_mic_at_all()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        await rig.Engine.StartAsync();

        rig.Run(2, mic: false);
        var snapshot = rig.Engine.Snapshot(Seconds(1));

        Assert.Empty(snapshot.Mic);
        Assert.Equal(2 * FramesPerSecond, snapshot.System.Length);
        Assert.Equal(Seconds(1), snapshot.Duration);
        Assert.False(rig.Engine.Status.MicEnabled);
        Assert.False(rig.Engine.Status.MicCapturing);
        Assert.Equal(61 * InternalFormat.SystemBytesPerSecond, rig.Engine.Status.MemoryBytes);
    }

    [Fact]
    public async Task Turning_the_mic_off_forgets_it_and_frees_its_memory()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(1);
        var memoryWithMic = rig.Engine.Status.MemoryBytes;
        var mic = rig.MicSource;

        await rig.Engine.SetDevicesAsync(null, null, micEnabled: false);
        rig.Run(1, mic: false);

        Assert.True(mic.Disposed);
        Assert.Empty(rig.Engine.Snapshot(Seconds(1)).Mic);
        Assert.True(rig.Engine.Status.MemoryBytes < memoryWithMic);
    }

    [Fact]
    public async Task Turning_the_mic_on_later_keeps_the_sources_aligned()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        await rig.Engine.StartAsync();
        rig.Run(3, mic: false);

        await rig.Engine.SetDevicesAsync(null, null, micEnabled: true);
        rig.Run(2);
        var snapshot = rig.Engine.Snapshot(Seconds(5));

        Assert.Equal(5 * FramesPerSecond, snapshot.Mic.Length);
        AssertAligned(snapshot);
        Assert.Equal(3 * FramesPerSecond, LongestZeroRun(snapshot.Mic, 1));
        Assert.Equal(Ramp.ThroughBuffer(3 * FramesPerSecond), snapshot.Mic[3 * FramesPerSecond]);
    }

    [Fact]
    public async Task Pause_keeps_the_buffer_saveable_and_stops_the_capture()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(5);
        var system = rig.SystemSource;

        await rig.Engine.PauseAsync();

        Assert.True(system.Disposed);
        Assert.Equal(CaptureActivity.Paused, rig.Engine.Status.Activity);
        rig.Clock.Advance(60);

        // 60 s later, the clip still ends where the pause began.
        var snapshot = rig.Engine.Snapshot(Seconds(5));
        Assert.Equal(5 * FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(Ramp.ThroughBuffer(5 * FramesPerSecond - 1), snapshot.Mic[^1]);
        Assert.Equal(Seconds(5), rig.Engine.Status.Buffered);
    }

    [Fact]
    public async Task Resume_joins_the_audio_with_nothing_for_the_paused_time()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(5);

        await rig.Engine.PauseAsync();
        rig.Clock.Advance(60);
        await rig.Engine.ResumeAsync();
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
        rig.Run(5);

        var snapshot = rig.Engine.Snapshot(Seconds(30));

        // 10 seconds of audio, not 70, with no hole at the join.
        Assert.Equal(10 * FramesPerSecond, snapshot.Mic.Length);
        Assert.Equal(0, LongestZeroRun(snapshot.Mic, 1));
        Assert.Equal(0, LongestZeroRun(snapshot.System, 2));
        AssertAligned(snapshot);
        Assert.Equal(Ramp.ThroughBuffer(5 * FramesPerSecond - 1), snapshot.Mic[5 * FramesPerSecond - 1]);
        Assert.Equal(Ramp.ThroughBuffer(65 * FramesPerSecond), snapshot.Mic[5 * FramesPerSecond]);
    }

    [Fact]
    public async Task Sources_ending_apart_at_a_pause_are_lined_up_for_the_resume()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(5);

        // The PC went quiet 100 ms before the pause.
        rig.Run(0.1, system: false);
        await rig.Engine.PauseAsync();
        rig.Clock.Advance(30);
        await rig.Engine.ResumeAsync();
        rig.Run(1);

        var snapshot = rig.Engine.Snapshot(Seconds(6.1));

        Assert.Equal((int)(6.1 * FramesPerSecond), snapshot.Mic.Length);
        AssertAligned(snapshot);
        Assert.Equal((int)(0.1 * FramesPerSecond), LongestZeroRun(snapshot.System, 2));
    }

    [Fact]
    public async Task Packets_arriving_during_a_pause_are_dropped()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(1);
        var mic = rig.MicSource;
        await rig.Engine.PauseAsync();

        mic.Deliver(new float[480], rig.Clock.Ticks);

        Assert.Equal(FramesPerSecond, rig.Engine.Snapshot(Seconds(5)).Mic.Length);
    }

    [Fact]
    public async Task Growing_the_buffer_keeps_what_is_there_and_shrinking_keeps_the_newest()
    {
        using var rig = new AudioTestRig(bufferSeconds: 20);
        await rig.Engine.StartAsync();
        rig.Run(20);

        await rig.Engine.ResizeBufferAsync(Seconds(5));
        var shrunk = rig.Engine.Snapshot(Seconds(20));
        Assert.Equal(5 * FramesPerSecond, shrunk.Mic.Length);
        Assert.Equal(Ramp.ThroughBuffer(20 * FramesPerSecond - 1), shrunk.Mic[^1]);
        AssertAligned(shrunk);
        Assert.Equal(6 * (2 + 1) * 2 * FramesPerSecond, rig.Engine.Status.MemoryBytes);

        await rig.Engine.ResizeBufferAsync(Seconds(30));
        rig.Run(10);
        var grown = rig.Engine.Snapshot(Seconds(30));
        Assert.Equal(16 * FramesPerSecond, grown.Mic.Length); // The 5 asked for, the 1 s of slack, and the 10 new ones.
        Assert.Equal(Ramp.ThroughBuffer(30 * FramesPerSecond - 1), grown.Mic[^1]);
        Assert.Equal(0, LongestZeroRun(grown.Mic, 1));
        AssertAligned(grown);
    }

    [Fact]
    public async Task No_device_is_a_state_and_not_an_exception()
    {
        using var rig = new AudioTestRig();
        rig.Factory.SystemDevice = null;
        rig.Factory.MicDevice = null;

        await rig.Engine.StartAsync();

        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);
        Assert.False(rig.Engine.Status.SystemCapturing);
        var snapshot = rig.Engine.Snapshot(Seconds(5));
        Assert.Empty(snapshot.System);
        Assert.Empty(snapshot.Mic);

        rig.Clock.Advance(10);
        rig.Factory.SystemDevice = "speakers";
        rig.Factory.MicDevice = "headset-mic";
        await rig.Engine.RefreshDevicesAsync();

        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
        rig.Run(1);
        Assert.Equal(FramesPerSecond, rig.Engine.Snapshot(Seconds(5)).Mic.Length);
    }

    [Fact]
    public async Task A_failure_to_open_is_a_state_and_not_an_exception()
    {
        using var rig = new AudioTestRig();
        rig.Factory.FailEverything = true;

        await rig.Engine.StartAsync();

        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);

        rig.Factory.FailEverything = false;
        await rig.Engine.RefreshDevicesAsync();
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
    }

    [Fact]
    public async Task A_missing_device_is_looked_for_again_without_any_notification()
    {
        using var rig = new AudioTestRig(micEnabled: false, tick: TimeSpan.FromMilliseconds(20));
        rig.Factory.SystemDevice = null;
        var capturing = new TaskCompletionSource();
        rig.Engine.StatusChanged += status =>
        {
            if (status.Activity == CaptureActivity.Capturing)
            {
                capturing.TrySetResult();
            }
        };
        await rig.Engine.StartAsync();
        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);

        rig.Factory.SystemDevice = "speakers";

        await capturing.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_failed_exclusion_is_tried_again_at_the_next_opening()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        rig.Factory.FailExclusion = true;
        await rig.Engine.StartAsync();
        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);

        rig.Factory.FailExclusion = false;
        rig.Factory.SystemDevice = "headset";
        await rig.Engine.RefreshDevicesAsync();

        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);
    }

    [Fact]
    public async Task Unplugging_everything_keeps_the_buffer_and_comes_back_by_itself()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        await rig.Engine.StartAsync();
        rig.Run(3, mic: false);
        var first = rig.SystemSource;

        rig.Factory.SystemDevice = null;
        first.Die(new COMException("AUDCLNT_E_DEVICE_INVALIDATED"));
        await rig.Engine.RefreshDevicesAsync();

        Assert.True(first.Disposed);
        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);
        Assert.Equal(3 * 2 * FramesPerSecond, rig.Engine.Snapshot(Seconds(10)).System.Length);

        rig.Clock.Advance(100);
        rig.Factory.SystemDevice = "headphones";
        rig.Watcher.Raise();
        await rig.Engine.RefreshDevicesAsync();

        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
        Assert.Equal("headphones name", rig.Engine.Status.SystemDeviceName);
        Assert.NotSame(first, rig.SystemSource);

        // With nothing else running, the time without a device leaves no hole.
        rig.Run(1, mic: false);
        var snapshot = rig.Engine.Snapshot(Seconds(10));
        Assert.Equal(4 * 2 * FramesPerSecond, snapshot.System.Length);
        Assert.Equal(0, LongestZeroRun(snapshot.System, 2));
    }

    [Fact]
    public async Task Changing_the_output_while_the_mic_runs_stays_aligned()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(2);
        var speakers = rig.SystemSource;

        rig.Factory.SystemDevice = "headset";
        rig.Watcher.Raise();
        await rig.Engine.RefreshDevicesAsync();
        Assert.True(speakers.Disposed);
        Assert.Equal("headset name", rig.Engine.Status.SystemDeviceName);

        // The new output takes 2 s to deliver its first packet: the mic carries on meanwhile.
        rig.Run(2, system: false);
        rig.Run(2);
        var snapshot = rig.Engine.Snapshot(Seconds(6));

        Assert.Equal(6 * FramesPerSecond, snapshot.Mic.Length);
        AssertAligned(snapshot);
        Assert.Equal(0, LongestZeroRun(snapshot.Mic, 1));
        Assert.Equal(2 * FramesPerSecond, LongestZeroRun(snapshot.System, 2));
    }

    [Fact]
    public async Task A_lost_microphone_is_reported_and_leaves_the_pc_sound_running()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(1);
        rig.Factory.MicDevice = null;

        rig.MicSource.Die(new COMException("gone"));
        await rig.Engine.RefreshDevicesAsync();

        var status = rig.Engine.Status;
        Assert.Equal(CaptureActivity.Capturing, status.Activity);
        Assert.True(status.SystemCapturing);
        Assert.True(status.MicEnabled);
        Assert.False(status.MicCapturing);
    }

    [Fact]
    public async Task A_recorder_that_stops_cleanly_by_itself_is_opened_again()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        await rig.Engine.StartAsync();
        var first = rig.SystemSource;

        first.Stop();
        await rig.Engine.RefreshDevicesAsync();

        Assert.NotSame(first, rig.SystemSource);
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
    }

    [Fact]
    public async Task The_default_output_leaves_our_own_sounds_out()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();

        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);
        Assert.False(rig.MicSource.Request.ExcludeOwnProcess);
        Assert.True(rig.Engine.Status.OwnSoundsExcluded);
    }

    [Fact]
    public async Task A_chosen_output_uses_the_classic_loopback()
    {
        using var rig = new AudioTestRig(systemDeviceId: "speakers");
        await rig.Engine.StartAsync();

        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);
        Assert.Equal("speakers", rig.SystemSource.Request.DeviceId);
        Assert.False(rig.Engine.Status.OwnSoundsExcluded);
    }

    [Fact]
    public async Task A_chosen_output_that_is_gone_is_no_device_and_not_the_default()
    {
        using var rig = new AudioTestRig(micEnabled: false, systemDeviceId: "unplugged");
        await rig.Engine.StartAsync();

        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);
        Assert.Empty(rig.Factory.Opened);
    }

    [Fact]
    public async Task If_the_exclusion_fails_it_falls_back_on_the_classic_loopback()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        rig.Factory.FailExclusion = true;

        await rig.Engine.StartAsync();

        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);
        Assert.False(rig.Engine.Status.OwnSoundsExcluded);
    }

    [Fact]
    public async Task If_the_exclusion_dies_before_any_packet_the_next_capture_is_classic()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        await rig.Engine.StartAsync();
        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);

        rig.SystemSource.Die(new COMException("not supported"));
        await rig.Engine.RefreshDevicesAsync();

        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);
    }

    [Fact]
    public async Task Stop_keeps_the_buffer_until_the_next_start()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(2);

        await rig.Engine.StopAsync();
        Assert.Equal(CaptureActivity.Stopped, rig.Engine.Status.Activity);
        Assert.Equal(2 * FramesPerSecond, rig.Engine.Snapshot(Seconds(5)).Mic.Length);

        await rig.Engine.StartAsync();
        rig.Run(1);
        Assert.Equal(FramesPerSecond, rig.Engine.Snapshot(Seconds(5)).Mic.Length);
    }

    [Fact]
    public async Task Status_changes_reach_listeners_and_a_broken_listener_breaks_nothing()
    {
        using var rig = new AudioTestRig();
        var seen = new List<CaptureActivity>();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Engine.StatusChanged += _ => throw new InvalidOperationException("listener bug");
        rig.Engine.StatusChanged += status =>
        {
            seen.Add(status.Activity);
            if (status.Activity == CaptureActivity.Stopped)
            {
                stopped.TrySetResult();
            }
        };

        await rig.Engine.StartAsync();
        await rig.Engine.PauseAsync();
        await rig.Engine.ResumeAsync();
        await rig.Engine.StopAsync();

        // The listener after the broken one hears everything, up to the stop, which is sent after StopAsync returns.
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(CaptureActivity.Stopped, rig.Engine.Status.Activity);
        Assert.Equal([CaptureActivity.Capturing, CaptureActivity.Paused, CaptureActivity.Capturing, CaptureActivity.Stopped], seen);
    }

    [Fact]
    public async Task Status_reports_activity_devices_buffered_time_and_memory()
    {
        var published = new List<AudioEngineStatus>();
        using var rig = new AudioTestRig(bufferSeconds: 10);
        rig.Engine.StatusChanged += published.Add;

        Assert.Equal(CaptureActivity.Stopped, rig.Engine.Status.Activity);

        await rig.Engine.StartAsync();
        rig.Run(3);
        await rig.Engine.PauseAsync();

        var status = rig.Engine.Status;
        Assert.Equal(CaptureActivity.Paused, status.Activity);
        Assert.Equal(Seconds(3), status.Buffered);
        Assert.Equal(11 * (InternalFormat.SystemBytesPerSecond + InternalFormat.MicBytesPerSecond), status.MemoryBytes);
        Assert.Contains(published, entry => entry.Activity == CaptureActivity.Capturing && entry.SystemDeviceName == "speakers name");
        Assert.Equal(CaptureActivity.Paused, published[^1].Activity);
    }

    [Fact]
    public async Task Start_on_a_started_engine_changes_nothing()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        var opened = rig.Factory.Opened.Count;

        await rig.Engine.StartAsync();
        await rig.Engine.ResumeAsync();

        Assert.Equal(opened, rig.Factory.Opened.Count);
    }
}

[CollectionDefinition(nameof(TimingSensitive), DisableParallelization = true)]
public class TimingSensitive;

[Collection(nameof(TimingSensitive))]
public class SnapshotBlockingTests(ITestOutputHelper output)
{
    private const int BufferFrames = 5 * 60 * InternalFormat.SampleRate;
    private const int PacketFrames = 480;

    private static (SourceTrack System, SourceTrack Mic) FullTracks()
    {
        var system = new SourceTrack(2, BufferFrames, 0);
        var mic = new SourceTrack(1, BufferFrames, 0);
        var systemPacket = new float[PacketFrames * 2];
        var micPacket = new float[PacketFrames];
        Array.Fill(systemPacket, 0.25f);
        Array.Fill(micPacket, 0.25f);
        for (long frame = 0; frame < BufferFrames; frame += PacketFrames)
        {
            system.Append(frame, systemPacket, exact: false);
            mic.Append(frame, micPacket, exact: false);
        }

        return (system, mic);
    }

    // Proved by logic and not by timing: with the writers' gate held, a writer cannot get through and a copy still ends.
    [Fact]
    public void A_full_snapshot_does_not_hold_up_the_audio_thread()
    {
        var (system, mic) = FullTracks();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (system.WriteGate)
            lock (mic.WriteGate)
            {
                held.Set();
                release.Wait();
            }
        });
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(30)));

        var writer = Task.Run(() => system.Append(BufferFrames, new float[PacketFrames * 2], exact: false));
        try
        {
            Assert.False(writer.Wait(100), "The gate is not what writers wait on");

            var copy = Task.Run(() => AudioSnapshot.CopyFrom(system, mic, 0, BufferFrames));
            Assert.True(copy.Wait(TimeSpan.FromSeconds(60)), "A copy waited for the writers' gate");
            Assert.Equal(BufferFrames, copy.Result.Mic.Length);
            Assert.Equal(BufferFrames * 2, copy.Result.System.Length);
        }
        finally
        {
            release.Set();
            holder.Join();
            writer.Wait();
        }
    }

    // A writer at the pace of real audio, whatever the copy takes on this machine. How much a copy loses depends on how
    // long it took, so that is worked out from how far the writer got, and what it loses is exactly the head.
    [Fact]
    public void A_full_snapshot_loses_exactly_what_the_writer_overtook()
    {
        var (system, mic) = FullTracks();
        var stored = SampleCodec.ToFloat(SampleCodec.ToShort(0.25f));
        var capacity = (long)BufferFrames + SourceTrack.SlackFrames;
        var stop = false;
        var written = (long)BufferFrames;
        var writer = new Thread(() =>
        {
            var systemPacket = new float[PacketFrames * 2];
            var micPacket = new float[PacketFrames];
            Array.Fill(systemPacket, 0.25f);
            Array.Fill(micPacket, 0.25f);
            var clock = Stopwatch.StartNew();
            var frame = (long)BufferFrames;
            while (!Volatile.Read(ref stop))
            {
                var due = BufferFrames + (long)(clock.Elapsed.TotalMilliseconds * 48);
                while (frame + PacketFrames <= due)
                {
                    system.Append(frame, systemPacket, exact: false);
                    mic.Append(frame, micPacket, exact: false);
                    frame += PacketFrames;
                    Volatile.Write(ref written, frame);
                }

                Thread.Sleep(1);
            }
        });
        writer.Start();

        var systemCopy = new float[BufferFrames * 2];
        var micCopy = new float[BufferFrames];
        var lostFrames = 0L;
        try
        {
            for (var round = 0; round < 6; round++)
            {
                var before = Volatile.Read(ref written);
                var start = Math.Min(system.EndFrame, mic.EndFrame) - BufferFrames;
                var systemReal = system.CopyFrames(start, systemCopy);
                var micReal = mic.CopyFrames(start, micCopy);
                var after = Volatile.Read(ref written);

                var fewest = Math.Max(0, before - capacity - start);
                var most = Math.Max(0, after + PacketFrames - capacity - start);
                lostFrames += AssertOnlyTheHeadIsLost(systemCopy, 2, systemReal, stored, fewest, most);
                lostFrames += AssertOnlyTheHeadIsLost(micCopy, 1, micReal, stored, fewest, most);
                Thread.Sleep(20);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }

        output.WriteLine($"Frames overtaken in 6 full copies of both sources: {lostFrames}");
    }

    private static long AssertOnlyTheHeadIsLost(float[] copy, int channels, int real, float stored, long fewest, long most)
    {
        var lost = (copy.Length - real) / channels;
        Assert.InRange(lost, fewest, most);
        Assert.Equal(-1, copy.AsSpan(0, lost * channels).IndexOfAnyExcept(0f));
        Assert.Equal(-1, copy.AsSpan(lost * channels).IndexOfAnyExcept(stored));
        return lost;
    }
}
