using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

/// <summary>A capture that reports to its factory, so a test can tell what is still open and make it misbehave.</summary>
internal sealed class TrackedSource(CaptureRequest request, TrackingFactory owner) : ICaptureSource
{
    private int _disposed;
    private int _started;

    public CaptureRequest Request { get; } = request;

    public WaveFormat Format { get; } = request.Flow == AudioFlow.System
        ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
        : WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

    public string? DeviceName => "device name";

    public event CapturePacketHandler? PacketAvailable;

    public event Action<Exception?>? Stopped;

    public void Start()
    {
        Volatile.Write(ref _started, 1);
        owner.Started(Request.Flow);
        owner.OnStart?.Invoke(this);
    }

    public void Stop()
    {
        owner.OnStop?.Invoke(this);
        Stopped?.Invoke(null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            owner.Closed(Request.Flow, Volatile.Read(ref _started) == 1);
        }
    }

    public void Deliver(ReadOnlySpan<float> samples, long stamp)
    {
        PacketAvailable?.Invoke(MemoryMarshal.AsBytes(samples), PacketFlags.None, 0, stamp);
    }

    public void Die(Exception exception) => Stopped?.Invoke(exception);
}

internal sealed class TrackingFactory : ICaptureSourceFactory
{
    private readonly ConcurrentQueue<long> _openStamps = new();
    private int _live;
    private int _maxLive;
    private int _openCalls;
    private int _resolveCalls;
    private int _maxRunning;
    private readonly int[] _running = new int[2];

    public Action<TrackedSource>? OnStart { get; set; }

    public Action<TrackedSource>? OnStop { get; set; }

    public Func<AudioFlow, string?>? Resolve { get; set; }

    public Func<CaptureRequest, Task>? BeforeOpen { get; set; }

    public int Live => Volatile.Read(ref _live);

    public int MaxLive => Volatile.Read(ref _maxLive);

    /// <summary>The most captures started and not yet closed on one side at any moment.</summary>
    public int MaxRunningPerSide => Volatile.Read(ref _maxRunning);

    /// <summary>Calls to open, including those that are stuck.</summary>
    public int OpenCalls => Volatile.Read(ref _openCalls);

    public int ResolveCalls => Volatile.Read(ref _resolveCalls);

    public IReadOnlyList<long> OpenStamps => [.. _openStamps];

    public volatile TrackedSource? Latest;

    public string? ResolveDevice(AudioFlow flow, string? requestedId)
    {
        Interlocked.Increment(ref _resolveCalls);
        return Resolve is null ? "device" : Resolve(flow);
    }

    public async Task<ICaptureSource> OpenAsync(CaptureRequest request)
    {
        Interlocked.Increment(ref _openCalls);
        _openStamps.Enqueue(Stopwatch.GetTimestamp());
        if (BeforeOpen is not null)
        {
            await BeforeOpen(request);
        }

        var source = new TrackedSource(request, this);
        var live = Interlocked.Increment(ref _live);
        int seen;
        while (live > (seen = Volatile.Read(ref _maxLive)) && Interlocked.CompareExchange(ref _maxLive, live, seen) != seen)
        {
        }

        Latest = source;
        return source;
    }

    public void Started(AudioFlow flow)
    {
        var running = Interlocked.Increment(ref _running[(int)flow]);
        int seen;
        while (running > (seen = Volatile.Read(ref _maxRunning)) && Interlocked.CompareExchange(ref _maxRunning, running, seen) != seen)
        {
        }
    }

    public void Closed(AudioFlow flow, bool started)
    {
        Interlocked.Decrement(ref _live);
        if (started)
        {
            Interlocked.Decrement(ref _running[(int)flow]);
        }
    }
}

internal static class Eventually
{
    public static async Task True(Func<bool> condition, string what, double seconds = 20)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed.TotalSeconds > seconds)
            {
                Assert.Fail($"Waited {seconds} s for: {what}");
            }

            await Task.Delay(2);
        }
    }
}

[Collection(nameof(TimingSensitive))]
public class SourceFailureTests
{
    private static readonly COMException Invalidated = new("AUDCLNT_E_DEVICE_INVALIDATED");

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    private static AudioEngine SystemOnly(TrackingFactory factory, TimeSpan? tick = null, string? systemDeviceId = "chosen") =>
        new(new AudioEngineOptions(Seconds(2), MicEnabled: false, SystemDeviceId: systemDeviceId), factory, new FakeWatcher(), tickInterval: tick);

    private static void DieAtOnce(TrackedSource source) => Task.Run(() => source.Die(Invalidated));

    [Fact]
    public async Task A_source_that_dies_at_once_is_not_reopened_in_a_tight_loop()
    {
        var factory = new TrackingFactory { OnStart = DieAtOnce };
        using var engine = SystemOnly(factory);

        await engine.StartAsync();
        await Task.Delay(Seconds(2));

        // With a tick of 1 s, the reopenings come 1 s, then 2 s apart. The loop this replaces did 21 120.
        var opens = factory.OpenCalls;
        factory.OnStart = null;
        Assert.InRange(opens, 1, 3);
    }

    [Fact]
    public async Task A_source_that_keeps_dying_is_reported_as_no_device()
    {
        var factory = new TrackingFactory { OnStart = DieAtOnce };
        using var engine = SystemOnly(factory, TimeSpan.FromHours(1));

        await engine.StartAsync();
        await Eventually.True(() => factory.Live == 0, "the dead source is closed");

        var status = engine.Status;
        Assert.Equal(CaptureActivity.NoDevice, status.Activity);
        Assert.False(status.SystemCapturing);
        Assert.Null(status.SystemDeviceName);
    }

    [Fact]
    public async Task The_wait_before_reopening_grows_then_stops_growing()
    {
        const double TickMs = 100;
        var factory = new TrackingFactory { OnStart = DieAtOnce };
        using var engine = SystemOnly(factory, TimeSpan.FromMilliseconds(TickMs));

        await engine.StartAsync();
        await Eventually.True(() => factory.OpenCalls >= 9, "nine openings");
        factory.OnStart = null;

        var gaps = factory.OpenStamps.Zip(factory.OpenStamps.Skip(1), (a, b) => (b - a) * 1000.0 / Stopwatch.Frequency).ToArray();

        // A timer never fires early by more than its 16 ms of resolution, whereas it can fire late on a busy machine.
        const double Resolution = 16;
        Assert.True(gaps[0] >= TickMs - Resolution, $"First wait: {gaps[0]:F0} ms");
        Assert.True(gaps[1] >= 2 * TickMs - Resolution, $"Second wait: {gaps[1]:F0} ms");
        Assert.True(gaps[2] >= 3 * TickMs - Resolution, $"Third wait: {gaps[2]:F0} ms");

        // Growing without a bound, these three would all be at least 600 ms.
        Assert.True(gaps[5..8].Min() < 6 * TickMs, $"Later waits: {string.Join(", ", gaps[5..8].Select(gap => gap.ToString("F0")))} ms");
    }

    [Fact]
    public async Task A_source_that_stops_dying_is_captured_again_by_itself()
    {
        var factory = new TrackingFactory { OnStart = DieAtOnce };
        using var engine = SystemOnly(factory, TimeSpan.FromMilliseconds(20));
        await engine.StartAsync();
        await Eventually.True(() => factory.OpenCalls >= 3, "three openings");

        factory.OnStart = null;

        await Eventually.True(() => engine.Status.Activity == CaptureActivity.Capturing, "capturing again");
    }

    [Fact]
    public async Task A_source_that_ran_for_a_while_is_reopened_at_once()
    {
        using var rig = new AudioTestRig(micEnabled: false, systemDeviceId: "speakers");
        await rig.Engine.StartAsync();
        var first = rig.SystemSource;
        rig.Run(6, mic: false);

        first.Die(Invalidated);

        // The tick of this rig is an hour, so only a reopening with no wait can get here.
        await Eventually.True(() => first.Disposed && rig.Factory.Opened.Count == 2, "a new capture");
        await Eventually.True(() => rig.Engine.Status.Activity == CaptureActivity.Capturing, "capturing");
    }

    [Fact]
    public async Task A_source_that_died_quickly_waits_for_its_turn_to_be_reopened()
    {
        using var rig = new AudioTestRig(micEnabled: false, systemDeviceId: "speakers");
        await rig.Engine.StartAsync();
        var first = rig.SystemSource;

        first.Die(Invalidated);
        await Eventually.True(() => first.Disposed, "the dead source is closed");

        Assert.Single(rig.Factory.Opened);
        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);

        // A device change or the user's own refresh does not wait.
        await rig.Engine.RefreshDevicesAsync();
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
    }

    [Fact]
    public async Task A_death_with_no_refresh_at_all_goes_to_no_device_and_comes_back()
    {
        using var rig = new AudioTestRig(micEnabled: false, tick: TimeSpan.FromMilliseconds(20));
        await rig.Engine.StartAsync();
        var first = rig.SystemSource;

        rig.Factory.SystemDevice = null;
        first.Die(Invalidated);
        await Eventually.True(() => rig.Engine.Status.Activity == CaptureActivity.NoDevice, "no device");

        rig.Factory.SystemDevice = "headphones";
        await Eventually.True(() => rig.Engine.Status.Activity == CaptureActivity.Capturing, "capturing again");

        Assert.NotSame(first, rig.SystemSource);
    }

    [Fact]
    public async Task Refreshes_do_not_pile_up_behind_a_slow_device()
    {
        var stuck = new TaskCompletionSource();
        var factory = new TrackingFactory { BeforeOpen = _ => stuck.Task };
        var watcher = new FakeWatcher();
        using var engine = new AudioEngine(new AudioEngineOptions(Seconds(2), MicEnabled: false, SystemDeviceId: "chosen"), factory, watcher);
        var start = engine.StartAsync();
        await Eventually.True(() => factory.OpenCalls == 1, "the opening under way");

        for (var i = 0; i < 100; i++)
        {
            watcher.Raise();
        }

        stuck.SetResult();
        await start;
        await engine.RefreshDevicesAsync();

        // The start, the one refresh that stood for all hundred, and the explicit one.
        Assert.InRange(factory.ResolveCalls, 1, 3);
    }
}

[Collection(nameof(TimingSensitive))]
public class StatusEventTests
{
    [Fact]
    public async Task After_a_stop_the_last_event_says_stopped_even_with_a_tick_under_way()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var armed = 0;
        var ticks = 1_000_000_000L;
        long Clock()
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                entered.Set();
                release.Wait();
            }

            return Interlocked.Add(ref ticks, 1000);
        }

        var factory = new TrackingFactory();
        using var engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(2), MicEnabled: false), factory, new FakeWatcher(), Clock, TimeSpan.FromMilliseconds(10));
        var published = new ConcurrentQueue<CaptureActivity>();
        engine.StatusChanged += status => published.Enqueue(status.Activity);
        await engine.StartAsync();

        // The next read of the clock comes from a tick building its status: it has read "Capturing" already.
        Volatile.Write(ref armed, 1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var stop = engine.StopAsync();
        await Eventually.True(() => engine.Status.Activity == CaptureActivity.Stopped, "the stop has run");
        await Task.Delay(50);

        release.Set();
        await stop;
        await Eventually.True(() => published.LastOrDefault() == CaptureActivity.Stopped, "the event for the stop");
        await Task.Delay(100);

        Assert.Equal(CaptureActivity.Stopped, published.Last());
        Assert.Equal(CaptureActivity.Stopped, engine.Status.Activity);
    }

    [Fact]
    public async Task Status_handlers_never_overlap_and_the_last_event_is_the_last_state()
    {
        using var rig = new AudioTestRig();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var inside = 0;
        var overlaps = 0;
        var calls = 0;
        var last = (int)CaptureActivity.Stopped;
        rig.Engine.StatusChanged += status =>
        {
            if (Interlocked.Increment(ref inside) > 1)
            {
                Interlocked.Increment(ref overlaps);
            }

            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }

            Volatile.Write(ref last, (int)status.Activity);
            Interlocked.Decrement(ref inside);
        };

        var start = rig.Engine.StartAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var pause = rig.Engine.PauseAsync();
        await Eventually.True(() => rig.Engine.Status.Activity == CaptureActivity.Paused, "the pause has run");
        await Task.Delay(100);

        release.Set();
        await Task.WhenAll(start, pause);

        Assert.Equal(0, Volatile.Read(ref overlaps));
        await Eventually.True(() => Volatile.Read(ref last) == (int)CaptureActivity.Paused, "the last event");
    }
}

[Collection(nameof(TimingSensitive))]
public class ResolverFailureTests
{
    private static AudioEngine SystemOnly(TrackingFactory factory, TimeSpan tick) =>
        new(new AudioEngineOptions(TimeSpan.FromSeconds(2), MicEnabled: false), factory, new FakeWatcher(), tickInterval: tick);

    [Fact]
    public async Task A_resolver_that_throws_at_start_means_no_device_and_the_retries_go_on()
    {
        var failing = true;
        var factory = new TrackingFactory
        {
            Resolve = _ => Volatile.Read(ref failing) ? throw new UnauthorizedAccessException("E_ACCESSDENIED") : "device",
        };
        using var engine = SystemOnly(factory, TimeSpan.FromMilliseconds(20));

        await engine.StartAsync();

        Assert.Equal(CaptureActivity.NoDevice, engine.Status.Activity);
        Volatile.Write(ref failing, false);
        await Eventually.True(() => engine.Status.Activity == CaptureActivity.Capturing, "capturing, with no other command");
    }

    [Fact]
    public async Task A_resolver_that_throws_leaves_a_working_capture_alone()
    {
        var failing = false;
        var factory = new TrackingFactory
        {
            Resolve = _ => Volatile.Read(ref failing) ? throw new UnauthorizedAccessException("E_ACCESSDENIED") : "device",
        };
        using var engine = SystemOnly(factory, TimeSpan.FromHours(1));
        await engine.StartAsync();
        var source = factory.Latest;

        Volatile.Write(ref failing, true);
        await engine.RefreshDevicesAsync();

        Assert.Equal(CaptureActivity.Capturing, engine.Status.Activity);
        Assert.Same(source, factory.Latest);
        Assert.Equal(1, factory.Live);
    }
}

[Collection(nameof(TimingSensitive))]
public class StuckDeviceTests
{
    // Far above what a call in these tests takes, far below the ten seconds a stuck call would be given.
    private static readonly TimeSpan StopBound = TimeSpan.FromSeconds(5);

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    private static AudioEngine SystemOnly(TrackingFactory factory, TimeSpan? tick = null, TimeSpan? openLimit = null) =>
        new(new AudioEngineOptions(Seconds(2), MicEnabled: false), factory, new FakeWatcher(), tickInterval: tick, openLimit: openLimit);

    [Fact]
    public async Task A_stuck_open_does_not_block_stop_and_its_late_capture_is_closed()
    {
        var stuck = new TaskCompletionSource();
        var factory = new TrackingFactory { BeforeOpen = _ => stuck.Task };
        using var engine = SystemOnly(factory);
        var start = engine.StartAsync();
        await Eventually.True(() => factory.OpenCalls == 1, "the opening under way");

        var stop = engine.StopAsync();
        await stop.WaitAsync(StopBound);
        await start.WaitAsync(StopBound);
        Assert.Equal(CaptureActivity.Stopped, engine.Status.Activity);

        stuck.SetResult();
        await Eventually.True(() => factory.Latest is not null && factory.Live == 0, "the late capture is closed");
        Assert.Null(engine.Status.SystemDeviceName);
    }

    [Fact]
    public async Task An_open_that_blocks_its_thread_does_not_block_stop()
    {
        using var release = new ManualResetEventSlim();
        var factory = new TrackingFactory
        {
            BeforeOpen = _ =>
            {
                release.Wait();
                return Task.CompletedTask;
            },
        };
        using var engine = SystemOnly(factory);
        var start = engine.StartAsync();
        await Eventually.True(() => factory.OpenCalls == 1, "the opening under way");

        try
        {
            await engine.StopAsync().WaitAsync(StopBound);
            await start.WaitAsync(StopBound);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_resolver_that_blocks_its_thread_does_not_block_stop()
    {
        using var release = new ManualResetEventSlim();
        var factory = new TrackingFactory
        {
            Resolve = _ =>
            {
                release.Wait();
                return "device";
            },
        };
        using var engine = SystemOnly(factory);
        var start = engine.StartAsync();
        await Eventually.True(() => factory.ResolveCalls == 1, "the lookup under way");

        try
        {
            await engine.StopAsync().WaitAsync(StopBound);
            await start.WaitAsync(StopBound);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_capture_that_will_not_stop_does_not_block_stop()
    {
        using var release = new ManualResetEventSlim();
        var factory = new TrackingFactory { OnStop = _ => release.Wait() };
        using var engine = SystemOnly(factory);
        await engine.StartAsync();

        try
        {
            await engine.StopAsync().WaitAsync(StopBound);
            Assert.Equal(CaptureActivity.Stopped, engine.Status.Activity);
        }
        finally
        {
            release.Set();
        }

        await Eventually.True(() => factory.Live == 0, "the capture closes when it comes back");
    }

    [Fact]
    public async Task A_capture_that_will_not_start_counts_as_missing_and_is_closed()
    {
        using var release = new ManualResetEventSlim();
        var factory = new TrackingFactory { OnStart = _ => release.Wait() };
        using var engine = SystemOnly(factory, TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(50));

        await engine.StartAsync().WaitAsync(StopBound);

        try
        {
            Assert.Equal(CaptureActivity.NoDevice, engine.Status.Activity);
            Assert.False(engine.Status.SystemCapturing);
        }
        finally
        {
            release.Set();
        }

        await Eventually.True(() => factory.Live == 0, "the capture is closed once its start is over");
    }

    [Fact]
    public async Task A_stuck_open_runs_out_of_time_is_not_doubled_and_the_device_comes_back()
    {
        var stuck = new TaskCompletionSource();
        var factory = new TrackingFactory { BeforeOpen = _ => stuck.Task };
        using var engine = new AudioEngine(
            new AudioEngineOptions(Seconds(2), MicEnabled: false, SystemDeviceId: "chosen"),
            factory,
            new FakeWatcher(),
            tickInterval: TimeSpan.FromMilliseconds(10),
            openLimit: TimeSpan.FromMilliseconds(50));

        await engine.StartAsync().WaitAsync(StopBound);
        Assert.Equal(CaptureActivity.NoDevice, engine.Status.Activity);

        // Many retries have come and gone by now, and none of them asked the device again.
        await Task.Delay(300);
        Assert.Equal(1, factory.OpenCalls);

        factory.BeforeOpen = null;
        stuck.SetResult();
        await Eventually.True(() => engine.Status.Activity == CaptureActivity.Capturing, "capturing again");
        await Eventually.True(() => factory.Live == 1, "the late capture is closed, and only that one");
    }
}

[Collection(nameof(TimingSensitive))]
public class DisposalTests
{
    private static AudioEngine SystemOnly(TrackingFactory factory, IDeviceWatcher? watcher = null) =>
        new(new AudioEngineOptions(TimeSpan.FromSeconds(2), MicEnabled: false), factory, watcher ?? new FakeWatcher());

    [Fact]
    public async Task A_start_racing_with_dispose_leaves_no_capture_open()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = new TrackingFactory();
        var engine = SystemOnly(factory);
        await engine.StartAsync();
        factory.OnStop = _ =>
        {
            entered.Set();
            release.Wait();
        };

        var disposing = Task.Run(engine.Dispose);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        factory.OnStop = null;
        var start = engine.StartAsync();
        release.Set();

        await disposing.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => start.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, factory.OpenCalls);
        Assert.Equal(0, factory.Live);
    }

    [Fact]
    public async Task After_dispose_commands_are_refused_a_stop_is_harmless_and_nothing_opens()
    {
        var factory = new TrackingFactory();
        var engine = SystemOnly(factory);
        await engine.StartAsync();

        engine.Dispose();
        engine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(engine.StartAsync);
        await Assert.ThrowsAsync<ObjectDisposedException>(engine.RefreshDevicesAsync);
        await engine.StopAsync();
        Assert.Equal(1, factory.OpenCalls);
        Assert.Equal(0, factory.Live);
        Assert.Equal(CaptureActivity.Stopped, engine.Status.Activity);
    }
}

internal sealed class WatcherThatHangsOnDispose(ManualResetEventSlim release) : IDeviceWatcher
{
    public event Action? DevicesChanged
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Dispose() => release.Wait();
}

[Collection(nameof(TimingSensitive))]
public class ListenerDeadlockTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static AudioEngine SystemOnly(TrackingFactory factory, IDeviceWatcher? watcher = null) =>
        new(new AudioEngineOptions(TimeSpan.FromSeconds(2), MicEnabled: false), factory, watcher ?? new FakeWatcher());

    [Fact]
    public async Task Dispose_does_not_wait_for_a_listener_that_waits_for_the_disposing_thread()
    {
        using var interfaceThreadFree = new ManualResetEventSlim();
        var factory = new TrackingFactory();
        var engine = SystemOnly(factory);
        await engine.StartAsync();
        engine.StatusChanged += status =>
        {
            if (status.Activity == CaptureActivity.Stopped)
            {
                interfaceThreadFree.Wait();
            }
        };

        try
        {
            await Task.Run(engine.Dispose).WaitAsync(Bound);
            Assert.Equal(0, factory.Live);
        }
        finally
        {
            interfaceThreadFree.Set();
        }
    }

    [Fact]
    public async Task Stop_does_not_wait_for_a_listener_that_waits_for_the_caller()
    {
        using var callerFree = new ManualResetEventSlim();
        var factory = new TrackingFactory();
        using var engine = SystemOnly(factory);
        await engine.StartAsync();
        engine.StatusChanged += status =>
        {
            if (status.Activity == CaptureActivity.Stopped)
            {
                callerFree.Wait();
            }
        };

        try
        {
            await engine.StopAsync().WaitAsync(Bound);
        }
        finally
        {
            callerFree.Set();
        }
    }

    [Fact]
    public async Task A_listener_can_dispose_the_engine()
    {
        var factory = new TrackingFactory();
        var engine = SystemOnly(factory);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status.Activity == CaptureActivity.Capturing)
            {
                engine.Dispose();
                disposed.TrySetResult();
            }
        };

        await engine.StartAsync();

        await disposed.Task.WaitAsync(Bound);
        Assert.Equal(0, factory.Live);
    }

    [Fact]
    public async Task Dispose_comes_back_even_if_the_device_layer_does_not()
    {
        using var release = new ManualResetEventSlim();
        var engine = SystemOnly(new TrackingFactory(), new WatcherThatHangsOnDispose(release));
        await engine.StartAsync();

        try
        {
            await Task.Run(engine.Dispose).WaitAsync(Bound);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_stuck_listener_holds_up_other_commands_only_briefly()
    {
        using var release = new ManualResetEventSlim();
        using var engine = SystemOnly(new TrackingFactory());
        await engine.StartAsync();
        engine.StatusChanged += _ => release.Wait();

        try
        {
            await engine.PauseAsync().WaitAsync(Bound);
            await engine.ResumeAsync().WaitAsync(Bound);
            Assert.Equal(CaptureActivity.Capturing, engine.Status.Activity);
        }
        finally
        {
            release.Set();
        }
    }
}

public class LoopbackChoiceTests
{
    [Fact]
    public async Task Choosing_the_default_output_by_name_goes_to_classic_loopback()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);

        await rig.Engine.SetDevicesAsync("speakers", null, true);

        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);
        Assert.False(rig.Engine.Status.OwnSoundsExcluded);
    }

    [Fact]
    public async Task Going_back_to_the_system_default_excludes_our_sounds_again()
    {
        using var rig = new AudioTestRig(systemDeviceId: "speakers");
        await rig.Engine.StartAsync();
        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);

        await rig.Engine.SetDevicesAsync(null, null, true);

        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);
        Assert.True(rig.Engine.Status.OwnSoundsExcluded);
    }

    [Fact]
    public async Task A_fallback_on_the_classic_loopback_is_not_reopened_by_every_refresh()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        rig.Factory.FailExclusion = true;
        await rig.Engine.StartAsync();
        var fallback = rig.SystemSource;

        await rig.Engine.RefreshDevicesAsync();

        Assert.Same(fallback, rig.SystemSource);
        Assert.False(fallback.Disposed);
    }

    [Fact]
    public async Task Going_back_to_the_default_tries_a_failed_exclusion_again()
    {
        using var rig = new AudioTestRig(micEnabled: false);
        rig.Factory.FailExclusion = true;
        await rig.Engine.StartAsync();
        await rig.Engine.SetDevicesAsync("speakers", null, false);

        rig.Factory.FailExclusion = false;
        await rig.Engine.SetDevicesAsync(null, null, false);

        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);
    }

    [Fact]
    public async Task Naming_the_default_microphone_does_not_reopen_it()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        var mic = rig.MicSource;

        await rig.Engine.SetDevicesAsync(null, "headset-mic", true);

        Assert.Same(mic, rig.MicSource);
        Assert.False(mic.Disposed);
    }
}

[Collection(nameof(TimingSensitive))]
public class TogglePauseTests
{
    [Fact]
    public async Task Two_toggles_during_a_stuck_open_end_up_capturing()
    {
        var stuck = new TaskCompletionSource();
        var factory = new TrackingFactory { BeforeOpen = _ => stuck.Task };
        using var engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(2), MicEnabled: false), factory, new FakeWatcher());
        var start = engine.StartAsync();
        await Eventually.True(() => factory.OpenCalls == 1, "the opening under way");

        var first = engine.TogglePauseAsync();
        var second = engine.TogglePauseAsync();
        stuck.SetResult();
        await start;

        Assert.Equal(CaptureActivity.Paused, await first);
        Assert.Equal(CaptureActivity.Capturing, await second);
        Assert.Equal(CaptureActivity.Capturing, engine.Status.Activity);
    }

    [Fact]
    public async Task Toggling_a_stopped_engine_does_nothing()
    {
        using var rig = new AudioTestRig();

        Assert.Equal(CaptureActivity.Stopped, await rig.Engine.TogglePauseAsync());
        Assert.Empty(rig.Factory.Opened);
    }
}

[Collection(nameof(TimingSensitive))]
public class CommandStressTests
{
    [Fact]
    public async Task All_commands_together_leave_no_double_capture_no_error_and_nothing_open()
    {
        var factory = new TrackingFactory();
        var watcher = new FakeWatcher();
        var engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(2)), factory, watcher, tickInterval: TimeSpan.FromMilliseconds(5));
        var errors = new ConcurrentQueue<Exception>();
        using var stop = new CancellationTokenSource();
        var commands = 0;

        async Task Commander(int seed)
        {
            var random = new Random(seed);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    Interlocked.Increment(ref commands);
                    await (random.Next(10) switch
                    {
                        0 => engine.StartAsync(),
                        1 => engine.StopAsync(),
                        2 => engine.PauseAsync(),
                        3 => engine.ResumeAsync(),
                        4 => engine.ResizeBufferAsync(TimeSpan.FromSeconds(1 + random.Next(3))),
                        5 => engine.SetDevicesAsync(null, null, random.Next(2) == 0),
                        6 => engine.SetDevicesAsync("device", "device", true),
                        7 => engine.RefreshDevicesAsync(),
                        8 => engine.TogglePauseAsync(),
                        _ => engine.StartAsync(),
                    });
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }
        }

        var deaths = Task.Run(() =>
        {
            var random = new Random(77);
            var samples = new float[960];
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    factory.Latest?.Deliver(samples.AsSpan(0, factory.Latest.Format.Channels * 480), 0);
                    if (random.Next(50) == 0)
                    {
                        factory.Latest?.Die(new COMException("gone"));
                    }

                    if (random.Next(30) == 0)
                    {
                        watcher.Raise();
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }

                Thread.Sleep(1);
            }
        });

        var snapshots = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var snapshot = engine.Snapshot(TimeSpan.FromSeconds(1));
                    _ = engine.Status;
                    if (snapshot.Mic.Length != 0 && snapshot.System.Length != 0 && snapshot.Mic.Length * 2 != snapshot.System.Length)
                    {
                        throw new InvalidOperationException($"Lengths apart: {snapshot.Mic.Length} and {snapshot.System.Length}");
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }
        });

        var commanders = Enumerable.Range(1, 4).Select(seed => Task.Run(() => Commander(seed))).ToArray();
        await Task.Delay(TimeSpan.FromSeconds(3));
        stop.Cancel();
        await Task.WhenAll(commanders.Append(deaths).Append(snapshots)).WaitAsync(TimeSpan.FromSeconds(30));
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await Eventually.True(() => factory.Live == 0, "nothing left open");
        await Task.Run(engine.Dispose).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(commands > 100, $"Only {commands} commands");
        Assert.Empty(errors);

        Assert.Equal(1, factory.MaxRunningPerSide);
    }
}
