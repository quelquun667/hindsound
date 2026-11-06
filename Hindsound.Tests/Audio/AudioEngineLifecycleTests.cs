using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Hindsound.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

/// <summary>A capture that reports to its factory, so a test can tell what is still open and make it misbehave.</summary>
internal sealed class TrackedSource(CaptureRequest request, TrackingFactory owner) : ICaptureSource
{
    private int _disposed;

    public CaptureRequest Request { get; } = request;

    public WaveFormat Format { get; } = request.Flow == AudioFlow.System
        ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
        : WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

    public string? DeviceName => "device name";

    public event CaptureDataAvailableHandler? PacketAvailable;

    public event Action<Exception?>? Stopped;

    public void Start() => owner.OnStart?.Invoke(this);

    public void Stop()
    {
        owner.OnStop?.Invoke(this);
        Stopped?.Invoke(null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            owner.Closed();
        }
    }

    public void Deliver(ReadOnlySpan<float> samples, long stamp)
    {
        PacketAvailable?.Invoke(MemoryMarshal.AsBytes(samples), AudioClientBufferFlags.None, 0, stamp);
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

    public Action<TrackedSource>? OnStart { get; set; }

    public Action<TrackedSource>? OnStop { get; set; }

    public Func<AudioFlow, string?>? Resolve { get; set; }

    public Func<CaptureRequest, Task>? BeforeOpen { get; set; }

    public int Live => Volatile.Read(ref _live);

    public int MaxLive => Volatile.Read(ref _maxLive);

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

    public void Closed() => Interlocked.Decrement(ref _live);
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
