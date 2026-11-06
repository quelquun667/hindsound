using System.Diagnostics;

namespace Hindsound.Audio;

/// <summary>What the engine is doing, as a whole.</summary>
public enum CaptureActivity
{
    /// <summary>Not started, or stopped.</summary>
    Stopped,

    /// <summary>At least one source is capturing.</summary>
    Capturing,

    /// <summary>Paused by the user. The buffer is kept and can be saved.</summary>
    Paused,

    /// <summary>Started, but no device to capture from. The buffer is kept and can be saved.</summary>
    NoDevice,
}

/// <summary>What the interface needs to show.</summary>
/// <param name="Activity">The overall state.</param>
/// <param name="SystemCapturing">The sound of the PC is being captured.</param>
/// <param name="MicEnabled">The user wants the microphone.</param>
/// <param name="MicCapturing">The microphone is being captured.</param>
/// <param name="SystemDeviceName">The output captured, if any.</param>
/// <param name="MicDeviceName">The microphone captured, if any.</param>
/// <param name="OwnSoundsExcluded">This app's sounds stay out of the buffer. False on a specific output, or on old Windows.</param>
/// <param name="Buffered">How much audio can be saved right now.</param>
/// <param name="MemoryBytes">Memory held by the buffers.</param>
public sealed record AudioEngineStatus(
    CaptureActivity Activity,
    bool SystemCapturing,
    bool MicEnabled,
    bool MicCapturing,
    string? SystemDeviceName,
    string? MicDeviceName,
    bool OwnSoundsExcluded,
    TimeSpan Buffered,
    long MemoryBytes);

/// <param name="BufferDuration">How much past audio to keep.</param>
/// <param name="MicEnabled">Capture the microphone.</param>
/// <param name="SystemDeviceId">An output to capture, or null for the system default (which keeps this app's sounds out).</param>
/// <param name="MicDeviceId">A microphone, or null for the system default.</param>
public sealed record AudioEngineOptions(
    TimeSpan BufferDuration,
    bool MicEnabled = true,
    string? SystemDeviceId = null,
    string? MicDeviceId = null);

/// <summary>
/// Keeps the last minutes of the PC sound and the microphone in memory, aligned with each other, and hands them
/// over on request. A missing or vanished device is a state, never an exception: the engine reports it, keeps
/// the buffer, and picks up again by itself when a device appears.
/// <para>
/// Commands run one at a time, off the caller's thread. <see cref="Snapshot"/> and <see cref="Status"/> never wait
/// for them, and never hold up the audio threads.
/// </para>
/// </summary>
public sealed class AudioEngine : IDisposable
{
    /// <summary>A source with nothing newer than this is taken as silent, not as having a packet on its way.</summary>
    public const int StalledAfterFrames = InternalFormat.SampleRate / 5;

    private static readonly TimeSpan DefaultTick = TimeSpan.FromSeconds(1);
    private const int TicksBetweenRetries = 3;

    private sealed class OpenSource(ICaptureSource source, CapturePipeline pipeline, string deviceId, bool excludesOwnProcess)
    {
        public ICaptureSource Source { get; } = source;
        public CapturePipeline Pipeline { get; } = pipeline;
        public string DeviceId { get; } = deviceId;
        public bool ExcludesOwnProcess { get; } = excludesOwnProcess;
        public bool Dead { get; set; }
    }

    private readonly ICaptureSourceFactory _factory;
    private readonly IDeviceWatcher _watcher;
    private readonly Func<long>? _clock;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Timer _tick;
    private readonly TimeSpan _tickInterval;

    private AudioEngineOptions _options;
    private CaptureTimeline _timeline;
    private SourceTrack? _systemTrack;
    private SourceTrack? _micTrack;
    private OpenSource? _system;
    private OpenSource? _mic;
    private bool _started;
    private bool _paused;
    private bool _watching;
    private bool _exclusionFailed;
    private bool _wantsRetry;
    private int _ticksSinceRetry;
    private bool _disposed;
    private AudioEngineStatus? _lastPublished;

    /// <param name="options">The starting settings.</param>
    /// <param name="factory">Opens the captures. By default, the real ones.</param>
    /// <param name="watcher">Reports device changes. By default, the real one.</param>
    /// <param name="clock">Current time in 100 ns units, on the clock of the packet stamps. For tests.</param>
    /// <param name="tickInterval">How often the buffered time is reported and a missing device is looked for again.</param>
    public AudioEngine(
        AudioEngineOptions options,
        ICaptureSourceFactory? factory = null,
        IDeviceWatcher? watcher = null,
        Func<long>? clock = null,
        TimeSpan? tickInterval = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _factory = factory ?? new WasapiCaptureSourceFactory();
        _watcher = watcher ?? new DeviceWatcher();
        _clock = clock;
        _timeline = new CaptureTimeline(clock);
        _tickInterval = tickInterval ?? DefaultTick;
        _tick = new Timer(_ => OnTick());
        _watcher.DevicesChanged += QueueRefresh;
    }

    /// <summary>
    /// Raised when the status changes, and about once a second while the buffered time grows. Comes on a thread pool
    /// thread: marshal to the interface thread.
    /// </summary>
    public event Action<AudioEngineStatus>? StatusChanged;

    /// <summary>The current status.</summary>
    public AudioEngineStatus Status => BuildStatus();

    /// <summary>Starts capturing, with an empty buffer. Without a device, it ends in <see cref="CaptureActivity.NoDevice"/>.</summary>
    public Task StartAsync() => RunCommandAsync(async () =>
    {
        if (_started)
        {
            return;
        }

        _timeline = new CaptureTimeline(_clock);
        _systemTrack = null;
        _micTrack = null;
        _started = true;
        _paused = false;
        StartWatching();
        await RefreshSourcesAsync().ConfigureAwait(false);
        _tick.Change(_tickInterval, _tickInterval);
    });

    /// <summary>Stops capturing. The buffer can still be saved, until the next <see cref="StartAsync"/>.</summary>
    public Task StopAsync() => RunCommandAsync(() =>
    {
        if (!_started)
        {
            return Task.CompletedTask;
        }

        _tick.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _started = false;
        _paused = false;
        _wantsRetry = false;
        EndCapture();
        return Task.CompletedTask;
    });

    /// <summary>Stops capturing but keeps the buffer, which can still be saved. The paused time will not exist in it.</summary>
    public Task PauseAsync() => RunCommandAsync(() =>
    {
        if (_started && !_paused)
        {
            _paused = true;
            _wantsRetry = false;
            EndCapture();
        }

        return Task.CompletedTask;
    });

    /// <summary>Captures again, the new audio following straight after what was kept.</summary>
    public Task ResumeAsync() => RunCommandAsync(async () =>
    {
        if (_started && _paused)
        {
            _paused = false;
            await RefreshSourcesAsync().ConfigureAwait(false);
        }
    });

    /// <summary>Changes how much is kept, keeping the newest audio. Clips can be saved meanwhile.</summary>
    public Task ResizeBufferAsync(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return RunCommandAsync(() =>
        {
            _options = _options with { BufferDuration = duration };
            var frames = FramesOf(duration);
            _systemTrack?.Resize(frames);
            _micTrack?.Resize(frames);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Changes the devices and turns the microphone on or off. Turning it off also forgets what it recorded:
    /// the next clips hold no microphone at all.
    /// </summary>
    public Task SetDevicesAsync(string? systemDeviceId, string? micDeviceId, bool micEnabled) => RunCommandAsync(async () =>
    {
        _options = _options with { SystemDeviceId = systemDeviceId, MicDeviceId = micDeviceId, MicEnabled = micEnabled };
        if (!micEnabled)
        {
            CloseSource(ref _mic);
            Volatile.Write(ref _micTrack, null);
        }

        await RefreshSourcesAsync().ConfigureAwait(false);
    });

    /// <summary>Checks the devices now, as a device change would.</summary>
    public Task RefreshDevicesAsync() => RunCommandAsync(RefreshSourcesAsync);

    /// <summary>
    /// Copies the last <paramref name="duration"/> of both sources, ending at the same instant and aligned, or what
    /// there is if there is less. Takes no lock the audio threads wait on. Copying 5:00 of both sources takes tens
    /// of milliseconds, so call it from a background thread.
    /// </summary>
    public AudioSnapshot Snapshot(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        var system = Volatile.Read(ref _systemTrack);
        var mic = Volatile.Read(ref _micTrack);
        var (start, frames) = WindowOf(system, mic, FramesOf(duration));
        return new AudioSnapshot(Copy(system, start, frames), Copy(mic, start, frames));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Task.Run(StopAsync).GetAwaiter().GetResult();
        _watcher.DevicesChanged -= QueueRefresh;
        _watcher.Dispose();
        _tick.Dispose();
        _commands.Dispose();
    }

    // Where a snapshot starts and how long it is. It ends at the newest instant every live source has reached.
    private (long Start, int Frames) WindowOf(SourceTrack? system, SourceTrack? mic, int requestedFrames)
    {
        if (system is null && mic is null)
        {
            return (0, 0);
        }

        var now = _timeline.NowFrames;
        var end = long.MaxValue;
        var oldest = long.MaxValue;
        foreach (var track in (ReadOnlySpan<SourceTrack?>)[system, mic])
        {
            if (track is null)
            {
                continue;
            }

            oldest = Math.Min(oldest, track.OldestFrame);
            var trackEnd = track.EndFrame;
            if (trackEnd >= now - StalledAfterFrames)
            {
                end = Math.Min(end, trackEnd);
            }
        }

        // Nothing recent from anyone: the PC is silent and the clip still ends now.
        if (end == long.MaxValue)
        {
            end = now;
        }

        var limit = Math.Min(requestedFrames, FramesOf(_options.BufferDuration));
        var start = Math.Max(end - limit, oldest);
        return (start, (int)Math.Max(0, end - start));
    }

    private static float[] Copy(SourceTrack? track, long start, int frames)
    {
        if (track is null || frames == 0)
        {
            return [];
        }

        // Every sample gets written by the copy, real or zero, so clearing the memory first would be wasted time.
        var samples = GC.AllocateUninitializedArray<float>(frames * track.Channels);
        track.CopyFrames(start, samples);
        return samples;
    }

    private Task RunCommandAsync(Func<Task> command)
    {
        return Task.Run(async () =>
        {
            await _commands.WaitAsync().ConfigureAwait(false);
            try
            {
                await command().ConfigureAwait(false);
            }
            finally
            {
                _commands.Release();
            }

            Publish();
        });
    }

    private void StartWatching()
    {
        if (_watching)
        {
            return;
        }

        try
        {
            _watcher.Start();
            _watching = true;
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            // Without notifications the periodic check still finds a new device.
            Debug.WriteLine($"Device watcher did not start: {exception.Message}");
        }
    }

    private async Task RefreshSourcesAsync()
    {
        if (!_started || _paused)
        {
            return;
        }

        await RefreshSourceAsync(AudioFlow.System).ConfigureAwait(false);
        await RefreshSourceAsync(AudioFlow.Mic).ConfigureAwait(false);

        var missing = _system is null || (_options.MicEnabled && _mic is null);
        _wantsRetry = missing;

        // With nothing to capture from, time stops on the timeline, as in a pause.
        if (_system is null && _mic is null)
        {
            FreezeTimeline();
        }
        else
        {
            _timeline.Resume();
        }
    }

    private async Task RefreshSourceAsync(AudioFlow flow)
    {
        ref var open = ref flow == AudioFlow.System ? ref _system : ref _mic;
        var wanted = flow == AudioFlow.System || _options.MicEnabled;
        var requestedId = flow == AudioFlow.System ? _options.SystemDeviceId : _options.MicDeviceId;
        var deviceId = wanted ? _factory.ResolveDevice(flow, requestedId) : null;

        if (open is not null && !open.Dead && open.DeviceId == deviceId)
        {
            return;
        }

        CloseSource(ref open);
        if (deviceId is null)
        {
            return;
        }

        var opened = await TryOpenAsync(flow, deviceId, requestedId).ConfigureAwait(false);
        if (flow == AudioFlow.System)
        {
            _system = opened;
        }
        else
        {
            _mic = opened;
        }
    }

    private async Task<OpenSource?> TryOpenAsync(AudioFlow flow, string deviceId, string? requestedId)
    {
        var exclude = flow == AudioFlow.System && requestedId is null && !_exclusionFailed;
        var opened = await TryOpenOnceAsync(flow, deviceId, exclude).ConfigureAwait(false);
        if (opened is null && exclude)
        {
            _exclusionFailed = true;
            opened = await TryOpenOnceAsync(flow, deviceId, false).ConfigureAwait(false);
        }

        return opened;
    }

    private async Task<OpenSource?> TryOpenOnceAsync(AudioFlow flow, string deviceId, bool exclude)
    {
        ICaptureSource? source = null;
        try
        {
            source = await _factory.OpenAsync(new CaptureRequest(flow, deviceId, exclude)).ConfigureAwait(false);
            var pipeline = new CapturePipeline(source.Format, EnsureTrack(flow), _timeline);
            var open = new OpenSource(source, pipeline, deviceId, exclude);
            source.PacketAvailable += pipeline.OnPacket;
            source.Stopped += exception => OnSourceStopped(open, exception);
            source.Start();
            return open;
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Could not open {flow} capture: {exception.Message}");
            source?.Dispose();
            return null;
        }
    }

    private SourceTrack EnsureTrack(AudioFlow flow)
    {
        ref var track = ref flow == AudioFlow.System ? ref _systemTrack : ref _micTrack;
        if (track is null)
        {
            var channels = flow == AudioFlow.System ? InternalFormat.SystemChannels : InternalFormat.MicChannels;
            Volatile.Write(ref track, new SourceTrack(channels, FramesOf(_options.BufferDuration), _timeline.NowFrames));
        }

        return track;
    }

    private void OnSourceStopped(OpenSource open, Exception? exception)
    {
        // Runs on the capture thread, which is ending: the new capture is opened from elsewhere.
        open.Dead = true;
        if (exception is not null)
        {
            Debug.WriteLine($"Capture stopped: {exception.Message}");

            // The exclusion started but never delivered anything: it does not work here, so use the classic loopback.
            if (open.ExcludesOwnProcess && open.Pipeline.Packets == 0)
            {
                _exclusionFailed = true;
            }
        }

        // A capture the engine is closing is no longer current, and a clean stop of it needs nothing more.
        if (exception is not null || open == _system || open == _mic)
        {
            QueueRefresh();
        }
    }

    private void QueueRefresh()
    {
        if (!_disposed)
        {
            _ = RunQuietlyAsync(RefreshSourcesAsync);
        }
    }

    private async Task RunQuietlyAsync(Func<Task> command)
    {
        try
        {
            await RunCommandAsync(command).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The engine closed while this was on its way.
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            // Nobody waits on a background refresh, and the next one tries again.
            Debug.WriteLine($"Device refresh failed: {exception.Message}");
        }
    }

    private void OnTick()
    {
        if (_wantsRetry && ++_ticksSinceRetry >= TicksBetweenRetries)
        {
            _ticksSinceRetry = 0;
            QueueRefresh();
        }

        Publish();
    }

    // Stops the sources and lines both tracks up on the frame where time stopped.
    private void EndCapture()
    {
        CloseSource(ref _system);
        CloseSource(ref _mic);
        FreezeTimeline();
    }

    private void FreezeTimeline()
    {
        _timeline.Freeze();
        var frame = _timeline.NowFrames;
        _systemTrack?.PadTo(frame);
        _micTrack?.PadTo(frame);
    }

    private void CloseSource(ref OpenSource? open)
    {
        var closing = open;
        open = null;
        if (closing is null)
        {
            return;
        }

        closing.Source.PacketAvailable -= closing.Pipeline.OnPacket;
        try
        {
            closing.Source.Stop();
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Capture did not stop cleanly: {exception.Message}");
        }

        try
        {
            closing.Source.Dispose();
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Capture did not close cleanly: {exception.Message}");
        }
    }

    private AudioEngineStatus BuildStatus()
    {
        var system = Volatile.Read(ref _systemTrack);
        var mic = Volatile.Read(ref _micTrack);
        var openSystem = _system;
        var openMic = _mic;
        var activity = !_started ? CaptureActivity.Stopped
            : _paused ? CaptureActivity.Paused
            : openSystem is null && openMic is null ? CaptureActivity.NoDevice
            : CaptureActivity.Capturing;

        var buffered = TimeSpan.Zero;
        if (system is not null || mic is not null)
        {
            var (_, frames) = WindowOf(system, mic, int.MaxValue);
            buffered = TimeSpan.FromSeconds((double)frames / InternalFormat.SampleRate);
        }

        return new AudioEngineStatus(
            activity,
            openSystem is not null,
            _options.MicEnabled,
            openMic is not null,
            openSystem?.Source.DeviceName,
            openMic?.Source.DeviceName,
            openSystem?.ExcludesOwnProcess ?? false,
            buffered,
            (system?.MemoryBytes ?? 0) + (mic?.MemoryBytes ?? 0));
    }

    private void Publish()
    {
        var status = BuildStatus();
        if (status == _lastPublished)
        {
            return;
        }

        // Once the buffer is full this stops firing, and only real changes get through.
        _lastPublished = status;
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A broken listener must not take the capture down with it.
            Debug.WriteLine($"StatusChanged handler failed: {exception}");
        }
    }

    private static int FramesOf(TimeSpan duration)
    {
        return (int)Math.Min(int.MaxValue / 4, Math.Round(duration.TotalSeconds * InternalFormat.SampleRate));
    }

    private static bool IsCaptureFailure(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException);
}
