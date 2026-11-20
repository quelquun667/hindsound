using System.Runtime.CompilerServices;
using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// Which playback streams belong to the application chosen, what the list offers, and what changes when streams come and go:
/// the rules of the capture of one application, without a server.
/// </summary>
public sealed unsafe class PulseAppRulesTests
{
    private const int Own = 150;

    // init (1) > shell of the app (100) > player (101) > its own child (102); this app (150) started from the app's shell;
    // another program (200) started by init; and a process whose parent is itself (300).
    private static readonly ProcessEntry[] Processes =
    [
        new(1, 0, "systemd"),
        new(100, 1, "hsapp"),
        new(101, 100, "paplay"),
        new(102, 101, "worker"),
        new(150, 100, "hindsound"),
        new(200, 1, "paplay"),
        new(300, 300, "odd"),
    ];

    [Fact]
    public void The_streams_of_the_application_are_those_of_its_process_and_its_descendants_never_another_one_nor_this_app()
    {
        PulsePlaybackStream[] streams =
        [
            new(10, 0, 101, "paplay", "paplay"),
            new(11, 0, 102, "worker", "Worker"),
            new(12, 0, 200, "paplay", "paplay"),
            new(13, 0, Own, "hindsound", "Hindsound"),
            new(14, 0, null, null, "No process"),
            new(15, 0, 100, "hsapp", "The app itself"),
        ];

        var mine = PulseAppRules.StreamsOf(streams, Processes, root: 100, ownProcessId: Own);

        Assert.Equal([10u, 11u, 15u], mine.Select(stream => stream.Index).Order().ToArray());
    }

    [Fact]
    public void A_process_that_is_its_own_parent_or_ids_that_loop_end_the_walk()
    {
        ProcessEntry[] looping = [new(5, 6, "a"), new(6, 5, "b"), new(300, 300, "odd")];

        Assert.Equal([5, 6], PulseAppRules.TreeOf(looping, 5).Order().ToArray());
        Assert.Equal([300], PulseAppRules.TreeOf(looping, 300).ToArray());
        Assert.Equal([42], PulseAppRules.TreeOf(looping, 42).ToArray());
    }

    [Fact]
    public void A_sandboxed_application_giving_an_id_of_its_own_namespace_is_found_by_its_program_s_name()
    {
        ProcessEntry[] processes = [new(1, 0, "systemd"), new(2, 0, "kthreadd"), new(3000, 1, "bwrap"), new(3001, 3000, "firefox"), new(3002, 3001, "firefox")];
        var byId = processes.ToDictionary(process => process.Id);

        // Pid 2 here is another program, with another name: the stream's program, by its name, is the oldest firefox.
        Assert.Equal(3001, PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 2, "firefox", "Firefox"), byId, processes));

        // A pid this system does not have at all.
        Assert.Equal(3001, PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 77777, "firefox", "Firefox"), byId, processes));

        // Nothing to go by.
        Assert.Null(PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 77777, null, null), byId, processes));
        Assert.Null(PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 77777, "absent", null), byId, processes));
    }

    [Fact]
    public void A_program_started_through_a_link_keeps_its_process_id_when_nothing_bears_its_file_s_name()
    {
        // python3 is a link to python3.12: /proc says python3, the stream says python3.12.
        ProcessEntry[] processes = [new(1, 0, "systemd"), new(500, 1, "python3")];
        var byId = processes.ToDictionary(process => process.Id);

        Assert.Equal(500, PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 500, "python3.12", "Python"), byId, processes));
        Assert.Equal(500, PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 500, null, null), byId, processes));
    }

    [Fact]
    public void A_long_program_name_is_compared_as_the_kernel_cuts_it()
    {
        ProcessEntry[] processes = [new(1, 0, "systemd"), new(700, 1, "a-very-long-pro")];
        var byId = processes.ToDictionary(process => process.Id);

        Assert.Equal("a-very-long-pro", PulseAppRules.KernelName("a-very-long-program-name"));
        Assert.Equal(700, PulseAppRules.OwnerOf(new PulsePlaybackStream(1, 0, 700, "a-very-long-program-name", null), byId, processes));
        Assert.Equal("short", PulseAppRules.KernelName("short"));
        Assert.Null(PulseAppRules.KernelName(""));
        Assert.Null(PulseAppRules.KernelName(null));

        // 14 bytes, then a character of two: the kernel keeps half of it, the comparison none.
        Assert.Equal("abcdefghijklmn", PulseAppRules.KernelName("abcdefghijklmné"));
        Assert.Equal("abcdefghijklmé", PulseAppRules.KernelName("abcdefghijklmé-x"));
    }

    [Fact]
    public void The_list_offers_each_application_once_by_the_name_its_streams_give_without_this_app()
    {
        ProcessEntry[] processes =
        [
            new(1, 0, "systemd"),
            new(4000, 1, "chrome"), new(4001, 4000, "chrome"), new(4002, 4001, "chrome"),
            new(5000, 1, "Isolated Web Co"),
            new(6000, 1, "firefox"), new(6001, 6000, "Isolated Web Co"),
            new(Own, 1, "hindsound"),
        ];
        PulsePlaybackStream[] streams =
        [
            new(1, 0, 4002, "chrome", "Chromium"),
            new(2, 0, 6001, "firefox", "Firefox"),
            new(3, 0, 6001, "firefox", "Firefox"),
            new(4, 0, Own, "hindsound", "Hindsound"),
            new(5, 0, null, null, "Nobody"),
        ];

        var sessions = PulseAppRules.Sessions(streams, processes);
        var apps = AudioAppList.Build(sessions, processes, Own);

        // A browser's audio process is offered as the browser: choosing it records the whole program.
        Assert.Equal(4000, sessions[0].ProcessId);
        Assert.Equal(6000, sessions[1].ProcessId);
        Assert.Equal(0, sessions[4].ProcessId);
        Assert.Equal(
            [new AudioAppInfo("chrome", "Chromium"), new AudioAppInfo("firefox", "Firefox")],
            apps.OrderBy(app => app.ExeName, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_events_that_may_change_the_streams_are_those_of_playback_streams_and_of_outputs_coming_or_going()
    {
        // pa_subscription_event_type_t: the facility in the low bits, new 0x00, change 0x10, remove 0x20.
        Assert.True(PulseAppRules.IsStreamChange(0x0002));
        Assert.True(PulseAppRules.IsStreamChange(0x0012));
        Assert.True(PulseAppRules.IsStreamChange(0x0022));
        Assert.True(PulseAppRules.IsStreamChange(0x0000));
        Assert.True(PulseAppRules.IsStreamChange(0x0020));
        Assert.False(PulseAppRules.IsStreamChange(0x0010));
        Assert.False(PulseAppRules.IsStreamChange(0x0001));
        Assert.False(PulseAppRules.IsStreamChange(0x0017));
        Assert.False(PulseAppRules.IsStreamChange(0x0003));
        Assert.Equal(0x0005u, PulseAppRules.SubscriptionMask);
    }

    [Fact]
    public void A_stream_that_starts_is_opened_one_that_ends_or_moves_is_closed_and_the_others_are_kept()
    {
        var monitors = new Dictionary<uint, string> { [0] = "out.monitor", [1] = "headset.monitor" };
        PulseMonitorKey[] current = [new(1, 0), new(2, 0), new(5, 0)];
        PulsePlaybackStream[] wanted =
        [
            new(2, 0, 101, null, null),
            new(3, 0, 101, null, null),
            new(5, 1, 101, null, null),
            new(6, 9, 101, null, null),
        ];

        var (open, close) = PulseAppRules.Plan(current, wanted, monitors);

        // 3 starts; 5 moved to the headset, recorded again there; 6 is on an output with no monitor; 1 ended; 2 is kept.
        Assert.Equal([(new PulseMonitorKey(3, 0), "out.monitor"), (new PulseMonitorKey(5, 1), "headset.monitor")], open.OrderBy(item => item.Key.SinkInput).ToArray());
        Assert.Equal([new PulseMonitorKey(1, 0), new PulseMonitorKey(5, 0)], close.OrderBy(key => key.SinkInput).ToArray());
    }

    [Fact]
    public void Nothing_changes_when_the_streams_are_those_recorded()
    {
        var monitors = new Dictionary<uint, string> { [0] = "out.monitor" };
        var (open, close) = PulseAppRules.Plan([new PulseMonitorKey(2, 0)], [new PulsePlaybackStream(2, 0, 1, null, null)], monitors);

        Assert.Empty(open);
        Assert.Empty(close);
    }

    [Fact]
    public void A_record_stream_of_one_application_is_never_corked_and_never_moved_by_the_server()
    {
        Assert.Equal(0u, PulseAppRules.RecordFlags & PulseFormats.StartCorked);
        Assert.NotEqual(0u, PulseAppRules.RecordFlags & PulseFormats.DontMove);
        Assert.NotEqual(0u, PulseAppRules.RecordFlags & PulseFormats.AdjustLatency);
    }

    [Fact]
    public void The_sink_input_info_gives_its_index_its_output_and_its_properties_where_libpulse_writes_them()
    {
        var info = default(PaSinkInputInfo);
        Assert.Equal(0, OffsetOf(ref info, ref info.Index));
        Assert.Equal(8, OffsetOf(ref info, ref info.Name));
        Assert.Equal(24, OffsetOf(ref info, ref info.Sink));
        Assert.Equal(28, OffsetOf(ref info, ref info.SampleSpec));
        Assert.Equal(40, OffsetOf(ref info, ref info.ChannelMap));
        Assert.Equal(172, OffsetOf(ref info, ref info.Volume));
        Assert.Equal(304, OffsetOf(ref info, ref info.BufferUsec));
        Assert.Equal(320, OffsetOf(ref info, ref info.ResampleMethod));
        Assert.Equal(336, OffsetOf(ref info, ref info.Mute));
        Assert.Equal(344, OffsetOf(ref info, ref info.Proplist));
    }

    private static int OffsetOf<TStruct, TField>(ref TStruct structure, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref structure), ref Unsafe.As<TField, byte>(ref field));
}

/// <summary>The mix of an application's streams: silence that goes on, streams that start, run dry and end.</summary>
public sealed class PulseAppMixerTests
{
    private static readonly PulseMonitorKey A = new(1, 0);
    private static readonly PulseMonitorKey B = new(2, 0);

    [Fact]
    public void With_no_stream_the_mix_is_silence_of_the_length_asked()
    {
        var mixer = new PulseAppMixer(channels: 2, primeFrames: 4, maxFrames: 16);
        var output = Enumerable.Repeat(9f, 10).ToArray();

        Assert.False(mixer.Mix(output));
        Assert.All(output, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void A_stream_counts_once_it_holds_enough_then_gives_its_samples_in_order()
    {
        var mixer = new PulseAppMixer(channels: 1, primeFrames: 4, maxFrames: 16);
        mixer.Add(A);
        mixer.Write(A, [1, 2, 3]);
        var output = new float[2];

        Assert.False(mixer.Mix(output));

        mixer.Write(A, [4, 5]);
        Assert.True(mixer.Mix(output));
        Assert.Equal([1f, 2f], output);
        Assert.True(mixer.Mix(output));
        Assert.Equal([3f, 4f], output);
    }

    [Fact]
    public void Two_streams_are_summed()
    {
        var mixer = new PulseAppMixer(channels: 2, primeFrames: 1, maxFrames: 16);
        mixer.Add(A);
        mixer.Add(B);
        mixer.Write(A, [0.25f, -0.5f, 0.25f, -0.5f]);
        mixer.Write(B, [0.5f, 0.5f, 0.5f, 0.5f]);
        var output = new float[4];

        Assert.True(mixer.Mix(output));
        Assert.Equal([0.75f, 0f, 0.75f, 0f], output);
    }

    [Fact]
    public void A_stream_that_runs_dry_gives_what_it_has_then_waits_to_hold_enough_again()
    {
        var mixer = new PulseAppMixer(channels: 1, primeFrames: 3, maxFrames: 16);
        mixer.Add(A);
        mixer.Write(A, [1, 2, 3]);
        var output = new float[4];

        Assert.True(mixer.Mix(output));
        Assert.Equal([1f, 2f, 3f, 0f], output);

        mixer.Write(A, [4, 5]);
        Assert.False(mixer.Mix(output));
        mixer.Write(A, [6]);
        Assert.True(mixer.Mix(output));
        Assert.Equal([4f, 5f, 6f, 0f], output);
    }

    [Fact]
    public void A_stream_that_holds_too_much_loses_its_oldest_samples()
    {
        var mixer = new PulseAppMixer(channels: 1, primeFrames: 1, maxFrames: 4);
        mixer.Add(A);
        mixer.Write(A, [1, 2, 3]);
        mixer.Write(A, [4, 5, 6]);
        var output = new float[4];

        Assert.True(mixer.Mix(output));
        Assert.Equal([3f, 4f, 5f, 6f], output);

        // More than it can hold at once: only the end stays.
        mixer.Write(A, [7, 8, 9, 10, 11, 12]);
        Assert.True(mixer.Mix(output));
        Assert.Equal([9f, 10f, 11f, 12f], output);
    }

    [Fact]
    public void A_stretch_the_server_had_no_data_for_is_silence_of_its_length()
    {
        var mixer = new PulseAppMixer(channels: 2, primeFrames: 1, maxFrames: 8);
        mixer.Add(A);
        mixer.Write(A, [1, 1]);
        mixer.WriteSilence(A, 2);
        mixer.Write(A, [2, 2]);
        var output = new float[8];

        Assert.True(mixer.Mix(output));
        Assert.Equal([1f, 1f, 0f, 0f, 0f, 0f, 2f, 2f], output);
    }

    [Fact]
    public void A_stream_removed_is_no_longer_heard_and_what_it_still_writes_is_dropped()
    {
        var mixer = new PulseAppMixer(channels: 1, primeFrames: 1, maxFrames: 8);
        mixer.Add(A);
        mixer.Write(A, [1, 2]);
        mixer.Remove(A);
        mixer.Write(A, [3, 4]);
        var output = new float[2];

        Assert.Equal(0, mixer.Count);
        Assert.False(mixer.Mix(output));
        Assert.Equal([0f, 0f], output);

        // Added again, it starts empty.
        mixer.Add(A);
        Assert.Equal(1, mixer.Count);
        Assert.False(mixer.Mix(output));
    }

    [Fact]
    public void Streams_that_come_and_go_for_long_allocate_nothing_once_added()
    {
        var mixer = new PulseAppMixer(channels: 2, primeFrames: 960, maxFrames: 12_000);
        mixer.Add(A);
        mixer.Add(B);
        var packet = new float[1920];
        var output = new float[960];
        for (var i = 0; i < 2; i++)
        {
            mixer.Write(A, packet);
            mixer.Write(B, packet);
            mixer.WriteSilence(B, 10);
            mixer.Mix(output);
            mixer.Remove(new PulseMonitorKey(99, 0));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            mixer.Write(A, packet);
            mixer.Write(B, packet);
            mixer.WriteSilence(B, 10);
            mixer.Mix(output);
            mixer.Remove(new PulseMonitorKey(99, 0));
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
