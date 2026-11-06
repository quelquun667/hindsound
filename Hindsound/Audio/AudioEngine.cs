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
/// <param name="SourceApp">The one application asked for instead of the whole PC, if any.</param>
/// <param name="SourceAppMissing">That application is not running: nothing is wrong, the PC side is silent until it comes back.</param>
public sealed record AudioEngineStatus(
    CaptureActivity Activity,
    bool SystemCapturing,
    bool MicEnabled,
    bool MicCapturing,
    string? SystemDeviceName,
    string? MicDeviceName,
    bool OwnSoundsExcluded,
    TimeSpan Buffered,
    long MemoryBytes,
    string? SourceApp = null,
    bool SourceAppMissing = false);

/// <param name="BufferDuration">How much past audio to keep.</param>
/// <param name="MicEnabled">Capture the microphone.</param>
/// <param name="SystemDeviceId">An output to capture, or null for the system default (which keeps this app's sounds out).</param>
/// <param name="MicDeviceId">A microphone, or null for the system default.</param>
/// <param name="SourceApp">Capture only this application, by exe name such as "Discord.exe". Null is the whole PC. Takes precedence over <paramref name="SystemDeviceId"/>.</param>
public sealed record AudioEngineOptions(
    TimeSpan BufferDuration,
    bool MicEnabled = true,
    string? SystemDeviceId = null,
    string? MicDeviceId = null,
    string? SourceApp = null);

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

    // The longest wait before looking again for a device, in ticks. A source that keeps dying waits 1, 2, then this.
    private const int MaxTicksBetweenRetries = 3;

    // A source that ran this long was working: its death is an event, not a device refusing to capture.
    private static readonly long StableAfterTicks = TimeSpan.FromSeconds(5).Ticks;

    // How long a device gets to answer, and to close, before the engine goes on without it. Commands run one at a
    // time, so one Windows call that never returns must not hold the others up for ever, least of all the stop.
    private static readonly TimeSpan DefaultOpenLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(2);

    // Dispose runs on the interface thread when the app closes, and a process stuck here keeps the app's mutex.
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(3);

    // Commands other than a stop give listeners this long to see the result, then get on with it.
    private static readonly TimeSpan ListenerWaitLimit = TimeSpan.FromSeconds(1);

    private sealed class OpenSource(ICaptureSource source, CapturePipeline pipeline, string deviceId, bool excludesOwnProcess, bool exclusionWanted, long openedAtTicks, int? targetProcessId)
    {
        private volatile bool _dead;
        private volatile bool _closing;

        public ICaptureSource Source { get; } = source;
        public CapturePipeline Pipeline { get; } = pipeline;
        public string DeviceId { get; } = deviceId;
        public bool ExcludesOwnProcess { get; } = excludesOwnProcess;

        /// <summary>The settings asked for the exclusion when this was opened, even if it fell back on the classic loopback.</summary>
        public bool ExclusionWanted { get; } = exclusionWanted;
        public long OpenedAtTicks { get; } = openedAtTicks;

        /// <summary>The application captured, if this captures one and not the whole PC.</summary>
        public int? TargetProcessId { get; } = targetProcessId;

        /// <summary>It ended by itself. Set from the capture thread.</summary>
        public bool Dead
        {
            get => _dead;
            set => _dead = value;
        }

        /// <summary>The engine is ending it: whatever it reports now needs no answer.</summary>
        public bool Closing
        {
            get => _closing;
            set => _closing = value;
        }
    }

    private readonly ICaptureSourceFactory _factory;
    private readonly IDeviceWatcher _watcher;
    private readonly IProcessSnapshot _processes;
    private readonly Func<long>? _clock;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Timer _tick;
    private readonly Timer _retry;
    private readonly TimeSpan _tickInterval;
    private readonly TimeSpan _openLimit;
    private readonly HashSet<string> _stuckCalls = [];
    private readonly Lock _publishGate = new();

    private bool _publishWanted;
    private bool _publishRunning;
    private List<TaskCompletionSource> _publishWaiters = [];

    private CancellationTokenSource _abortOpenings = new();
    private int _stopsWaiting;
    private volatile bool _appMissing;
    private AudioEngineOptions _options;
    private CaptureTimeline _timeline;
    private SourceTrack? _systemTrack;
    private SourceTrack? _micTrack;
    private OpenSource? _system;
    private OpenSource? _mic;
    private bool _started;
    private bool _paused;
    private bool _watching;
    private bool _exclusionDiedEarly;
    private int _quickDeaths;
    private int _refreshQueued;
    private int _disposed;
    private AudioEngineStatus? _lastPublished;

    /// <param name="options">The starting settings.</param>
    /// <param name="factory">Opens the captures. By default, the real ones.</param>
    /// <param name="watcher">Reports device changes. By default, the real one.</param>
    /// <param name="clock">Current time in 100 ns units, on the clock of the packet stamps. For tests.</param>
    /// <param name="tickInterval">How often the buffered time is reported and a missing device is looked for again.</param>
    /// <param name="openLimit">How long a device gets to answer before it counts as missing. A later answer is thrown away.</param>
    /// <param name="processes">Lists the running processes, to find the application to capture. By default, the real list.</param>
    public AudioEngine(
        AudioEngineOptions options,
        ICaptureSourceFactory? factory = null,
        IDeviceWatcher? watcher = null,
        Func<long>? clock = null,
        TimeSpan? tickInterval = null,
        TimeSpan? openLimit = null,
        IProcessSnapshot? processes = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _factory = factory ?? new WasapiCaptureSourceFactory();
        _watcher = watcher ?? new DeviceWatcher();
        _clock = clock;
        _timeline = new CaptureTimeline(clock);
        _tickInterval = tickInterval ?? DefaultTick;
        _openLimit = openLimit ?? DefaultOpenLimit;
        _processes = processes ?? new ToolHelpProcessSnapshot();
        _tick = new Timer(_ => OnTick());
        _retry = new Timer(_ => QueueRefresh());
        _watcher.DevicesChanged += QueueRefresh;
    }

    /// <summary>
    /// Raised when the status changes, and about once a second while the buffered time grows. Comes on a thread pool
    /// thread: marshal to the interface thread. One event at a time, in order, and the last one tells the last state.
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
        _exclusionDiedEarly = false;
        _quickDeaths = 0;
        _appMissing = false;
        StartWatching();
        _tick.Change(_tickInterval, _tickInterval);
        await RefreshSourcesAsync().ConfigureAwait(false);
    });

    /// <summary>Stops capturing. The buffer can still be saved, until the next <see cref="StartAsync"/>.</summary>
    public Task StopAsync()
    {
        AbortOpenings();

        // A listener may be waiting for a thread that is itself waiting for this stop.
        return RunCommandAsync(StopCoreAsync, waitForListeners: false, evenIfDisposed: true);
    }

    /// <summary>Stops capturing but keeps the buffer, which can still be saved. The paused time will not exist in it.</summary>
    public Task PauseAsync() => RunCommandAsync(PauseCoreAsync);

    /// <summary>Captures again, the new audio following straight after what was kept.</summary>
    public Task ResumeAsync() => RunCommandAsync(ResumeCoreAsync);

    /// <summary>
    /// Pauses if capturing, resumes if paused, and does nothing if stopped. The choice is made when the command gets
    /// its turn, so two toggles in a row always cancel out, whatever was still running when they were asked for.
    /// </summary>
    /// <returns>The state this ended in.</returns>
    public async Task<CaptureActivity> TogglePauseAsync()
    {
        var activity = CaptureActivity.Stopped;
        await RunCommandAsync(async () =>
        {
            if (_started)
            {
                await (_paused ? ResumeCoreAsync() : PauseCoreAsync()).ConfigureAwait(false);
            }

            activity = BuildStatus().Activity;
        }).ConfigureAwait(false);
        return activity;
    }

    private async Task PauseCoreAsync()
    {
        if (_started && !_paused)
        {
            _paused = true;
            CancelRetry();
            await EndCaptureAsync().ConfigureAwait(false);
        }
    }

    private async Task ResumeCoreAsync()
    {
        if (_started && _paused)
        {
            _paused = false;
            await RefreshSourcesAsync().ConfigureAwait(false);
        }
    }

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
            await CloseFlowAsync(AudioFlow.Mic).ConfigureAwait(false);
            Volatile.Write(ref _micTrack, null);
        }

        await RefreshSourcesAsync().ConfigureAwait(false);
    });

    /// <summary>
    /// Captures one application instead of the whole PC, or the whole PC again with null. An application that is not
    /// running is not an error: <see cref="AudioEngineStatus.SourceAppMissing"/> says so, its silence is kept in place
    /// like any other, and the capture attaches by itself when the application starts.
    /// </summary>
    /// <param name="exeName">The exe name, such as "Discord.exe". Takes precedence over the chosen output.</param>
    public Task SetSourceAppAsync(string? exeName) => RunCommandAsync(async () =>
    {
        _options = _options with { SourceApp = exeName };
        await RefreshSourcesAsync().ConfigureAwait(false);
    });

    /// <summary>Checks the devices now, as a device change would.</summary>
    public Task RefreshDevicesAsync() => RunCommandAsync(RefreshSourcesAsync);

    /// <summary>
    /// Copies the last <paramref name="duration"/> of both sources, ending at the same instant and aligned, or what
    /// there is if there is less. Takes no lock the audio threads wait on. Copying 5:00 of both sources takes about
    /// 150 ms and 173 MB until the result is released, so call it from a background thread.
    /// </summary>
    public AudioSnapshot Snapshot(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        var system = Volatile.Read(ref _systemTrack);
        var mic = Volatile.Read(ref _micTrack);
        var (start, frames) = WindowOf(system, mic, FramesOf(duration));
        return AudioSnapshot.CopyFrom(system, mic, start, frames);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _watcher.DevicesChanged -= QueueRefresh;
        try
        {
            // A listener never takes part in this: the status it may be blocked on is sent from another thread.
            if (!Task.Run(TearDownAsync).Wait(ShutdownLimit))
            {
                Debug.WriteLine("Gave up waiting for the engine to close: it finishes in the background");
            }
        }
        catch (AggregateException exception)
        {
            Debug.WriteLine($"The engine did not close cleanly: {exception.GetBaseException().Message}");
        }
    }

    // The semaphore is left to the collector: commands may still be waiting on it, and they must get their turn to
    // be refused. Disposing it would leave them waiting for ever.
    private async Task TearDownAsync()
    {
        // Whatever command was running or waiting ahead of this one, nothing is open once this one has run.
        await StopAsync().ConfigureAwait(false);
        try
        {
            _watcher.Dispose();
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Device watcher did not close cleanly: {exception.Message}");
        }

        _tick.Dispose();
        _retry.Dispose();
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

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


    private async Task RunCommandAsync(Func<Task> command, bool waitForListeners = true, bool evenIfDisposed = false)
    {
        // The turn is taken on the caller's thread, so commands run in the order they were asked for.
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            // A command that was waiting when the engine closed must not open anything behind the final stop.
            ObjectDisposedException.ThrowIf(IsDisposed && !evenIfDisposed, this);
            await Task.Run(command).ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
        }

        var delivered = RequestPublish();
        if (waitForListeners)
        {
            try
            {
                await delivered.WaitAsync(ListenerWaitLimit).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Debug.WriteLine("A StatusChanged listener is taking its time");
            }
        }
    }

    // From the moment a stop is asked for, nothing waits for a device any more: the commands ahead of it give up
    // their openings, and so reach the stop. New openings are allowed again once the last stop waiting has run.
    private void AbortOpenings()
    {
        Interlocked.Increment(ref _stopsWaiting);
        _ = Volatile.Read(ref _abortOpenings).CancelAsync();
    }

    private async Task StopCoreAsync()
    {
        if (Interlocked.Decrement(ref _stopsWaiting) == 0 && !IsDisposed)
        {
            Volatile.Write(ref _abortOpenings, new CancellationTokenSource());
        }

        if (_started)
        {
            _tick.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _started = false;
            _paused = false;
            CancelRetry();
            await EndCaptureAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a call into the device layer, which may never come back, off this thread and for a limited time. Past the
    /// limit, or on a stop, the engine goes on without it, and <paramref name="onLate"/> takes care of a late result.
    /// While one is stuck the same call is not tried again, so retries do not use up threads.
    /// </summary>
    private async Task<(bool Finished, T? Result)> CallBoundedAsync<T>(string name, Func<Task<T>> call, Action<T>? onLate = null)
    {
        var abort = Volatile.Read(ref _abortOpenings).Token;
        lock (_stuckCalls)
        {
            if (abort.IsCancellationRequested || _stuckCalls.Contains(name))
            {
                return (false, default);
            }
        }

        var running = Task.Run(call);
        try
        {
            return (true, await running.WaitAsync(_openLimit, abort).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            Debug.WriteLine($"Gave up waiting for {name}");
            lock (_stuckCalls)
            {
                _stuckCalls.Add(name);
            }

            _ = running.ContinueWith(
                finished =>
                {
                    lock (_stuckCalls)
                    {
                        _stuckCalls.Remove(name);
                    }

                    if (finished.IsCompletedSuccessfully)
                    {
                        onLate?.Invoke(finished.Result);
                    }
                    else
                    {
                        Debug.WriteLine($"{name} failed late: {finished.Exception?.GetBaseException().Message}");
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return (false, default);
        }
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

        if (!IsLive(_system) || (_options.MicEnabled && !IsLive(_mic)))
        {
            ArmRetry(MaxTicksBetweenRetries);
        }
        else
        {
            CancelRetry();
        }

        SettleTimeline();
    }

    private static bool IsLive(OpenSource? open) => open is { Dead: false };

    // With nothing to capture from, time stops on the timeline, as in a pause.
    // An application that is not running is different: the capture is on and hears silence, so time goes on.
    private void SettleTimeline()
    {
        if (!IsLive(_system) && !IsLive(_mic) && !_appMissing)
        {
            FreezeTimeline();
        }
        else
        {
            _timeline.Resume();
        }
    }

    // One shot, and nothing runs until it fires, so a device that keeps failing costs no CPU.
    private void ArmRetry(int ticks) => _retry.Change(_tickInterval * ticks, Timeout.InfiniteTimeSpan);

    private void CancelRetry() => _retry.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private string? WantedApp => string.IsNullOrWhiteSpace(_options.SourceApp) ? null : _options.SourceApp;

    private async Task RefreshAppSourceAsync(string app)
    {
        var open = _system;
        var (known, target) = await FindAppAsync(app).ConfigureAwait(false);
        if (!known)
        {
            if (open is { Dead: true })
            {
                await CloseFlowAsync(AudioFlow.System).ConfigureAwait(false);
            }

            return;
        }

        // Same process, same capture: another instance starting later does not pull it away.
        var id = "app:" + app;
        if (target is not null && IsLive(open) && open!.TargetProcessId == target && string.Equals(open.DeviceId, id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await CloseFlowAsync(AudioFlow.System).ConfigureAwait(false);
        NoteAppPresence(target);
        if (target is not null)
        {
            _system = await TryOpenOnceAsync(new CaptureRequest(AudioFlow.System, id, false, target, app), exclusionWanted: false).ConfigureAwait(false);
        }
    }

    // The track exists from now on, so a clip made while the application is away holds its silence, and its length.
    private void NoteAppPresence(int? target)
    {
        _appMissing = target is null;
        if (_appMissing)
        {
            EnsureTrack(AudioFlow.System);
        }
    }

    private async Task<(bool Known, int? Target)> FindAppAsync(string app)
    {
        try
        {
            return await CallBoundedAsync<int?>(
                "process list",
                () => Task.FromResult(SourceAppFinder.FindCaptureTarget(_processes.Take(), app))).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Could not list the processes: {exception.Message}");
            return (false, null);
        }
    }

    private async Task RefreshSourceAsync(AudioFlow flow)
    {
        if (flow == AudioFlow.System)
        {
            if (WantedApp is { } app)
            {
                await RefreshAppSourceAsync(app).ConfigureAwait(false);
                return;
            }

            _appMissing = false;
        }

        var open = flow == AudioFlow.System ? _system : _mic;
        var wanted = flow == AudioFlow.System || _options.MicEnabled;
        var requestedId = flow == AudioFlow.System ? _options.SystemDeviceId : _options.MicDeviceId;
        var (known, deviceId) = wanted ? await ResolveDeviceAsync(flow, requestedId).ConfigureAwait(false) : (true, null);

        // No answer says nothing about the device: a capture that works keeps working.
        if (!known)
        {
            if (open is { Dead: true })
            {
                await CloseFlowAsync(flow).ConfigureAwait(false);
            }

            return;
        }

        if (IsLive(open) && open!.DeviceId == deviceId && open.ExclusionWanted == WantsExclusion(flow, requestedId))
        {
            return;
        }

        await CloseFlowAsync(flow).ConfigureAwait(false);
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

    private async Task<(bool Known, string? DeviceId)> ResolveDeviceAsync(AudioFlow flow, string? requestedId)
    {
        try
        {
            return await CallBoundedAsync(
                $"resolve {flow}",
                () => Task.FromResult(_factory.ResolveDevice(flow, requestedId))).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            // A device that cannot be looked up is a state like any other, and the retries go on.
            Debug.WriteLine($"Could not look up the {flow} device: {exception.Message}");
            return (false, null);
        }
    }

    private async Task<OpenSource?> TryOpenAsync(AudioFlow flow, string deviceId, string? requestedId)
    {
        // A failure to open may come from the device being swapped, so the exclusion gets another try next time.
        var wanted = WantsExclusion(flow, requestedId);
        var exclude = wanted && !_exclusionDiedEarly;
        var opened = await TryOpenOnceAsync(new CaptureRequest(flow, deviceId, exclude), wanted).ConfigureAwait(false);
        if (opened is null && exclude)
        {
            opened = await TryOpenOnceAsync(new CaptureRequest(flow, deviceId, false), wanted).ConfigureAwait(false);
        }

        return opened;
    }

    // No specific output chosen means leaving this app's sounds out, even if the chosen one is also the default device.
    private static bool WantsExclusion(AudioFlow flow, string? requestedId) => flow == AudioFlow.System && requestedId is null;

    private async Task<OpenSource?> TryOpenOnceAsync(CaptureRequest request, bool exclusionWanted)
    {
        var flow = request.Flow;
        ICaptureSource? source = null;
        try
        {
            var call = $"open {flow} exclude={request.ExcludeOwnProcess} app={request.TargetProcessId is not null}";
            (_, source) = await CallBoundedAsync(call, () => _factory.OpenAsync(request), DisposeQuietly).ConfigureAwait(false);
            if (source is null)
            {
                return null;
            }

            var pipeline = new CapturePipeline(source.Format, EnsureTrack(flow), _timeline);
            var open = new OpenSource(source, pipeline, request.DeviceId, request.ExcludeOwnProcess, exclusionWanted, _timeline.NowTicks, request.TargetProcessId);
            source.PacketAvailable += pipeline.OnPacket;
            source.Stopped += exception => OnSourceStopped(open, exception);

            var started = source;
            var (finished, _) = await CallBoundedAsync($"start {flow}", () =>
            {
                started.Start();
                return Task.FromResult(true);
            }).ConfigureAwait(false);
            if (!finished)
            {
                open.Closing = true;
                source.PacketAvailable -= pipeline.OnPacket;
                _ = Task.Run(() => DisposeQuietly(started));
                return null;
            }

            return open;
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Could not open {flow} capture: {exception.Message}");
            DisposeQuietly(source);
            return null;
        }
    }

    private static void DisposeQuietly(ICaptureSource? source)
    {
        try
        {
            source?.Dispose();
        }
        catch (Exception exception) when (IsCaptureFailure(exception))
        {
            Debug.WriteLine($"Capture did not close cleanly: {exception.Message}");
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
        if (open.Closing)
        {
            return;
        }

        open.Dead = true;
        var reopenNow = _timeline.NowTicks - open.OpenedAtTicks >= StableAfterTicks;
        if (exception is not null)
        {
            Debug.WriteLine($"Capture stopped: {exception.Message}");

            // The exclusion started but never delivered anything: it does not work here, so use the classic loopback.
            // That is a different capture and not the same one again, and it can happen only once per start.
            if (open.ExcludesOwnProcess && open.Pipeline.Packets == 0)
            {
                reopenNow |= !_exclusionDiedEarly;
                _exclusionDiedEarly = true;
            }
        }

        if (!IsDisposed)
        {
            _ = RunQuietlyAsync(() => OnSourceDeathAsync(open, reopenNow));
        }
    }

    // A source that dies straight after opening would otherwise be reopened as fast as the machine can go, so it
    // waits, longer each time. Status says so meanwhile: the source no longer counts as capturing.
    private async Task OnSourceDeathAsync(OpenSource open, bool reopenNow)
    {
        // A refresh got there first, or the engine closed it.
        if (open != _system && open != _mic)
        {
            return;
        }

        if (reopenNow)
        {
            _quickDeaths = 0;
            await RefreshSourcesAsync().ConfigureAwait(false);
            return;
        }

        var flow = open == _system ? AudioFlow.System : AudioFlow.Mic;
        await CloseFlowAsync(flow).ConfigureAwait(false);

        // An application that closed is not a failure: time goes on while it is away.
        if (flow == AudioFlow.System && WantedApp is { } app)
        {
            var (known, target) = await FindAppAsync(app).ConfigureAwait(false);
            if (known)
            {
                NoteAppPresence(target);
            }
        }

        SettleTimeline();
        ArmRetry(Math.Min(++_quickDeaths, MaxTicksBetweenRetries));
    }

    private void QueueRefresh()
    {
        // One waiting refresh does the work of any number of them: they must not pile up behind a slow device.
        if (IsDisposed || Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        _ = RunQuietlyAsync(() =>
        {
            Volatile.Write(ref _refreshQueued, 0);
            return RefreshSourcesAsync();
        });
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

    private void OnTick() => RequestPublish();

    // Stops the sources and lines both tracks up on the frame where time stopped.
    private async Task EndCaptureAsync()
    {
        // Both stop delivering as soon as this is called, so a device slow to close adds no silence to the buffer.
        _appMissing = false;
        var closing = Task.WhenAll(CloseFlowAsync(AudioFlow.System), CloseFlowAsync(AudioFlow.Mic));
        FreezeTimeline();
        await closing.ConfigureAwait(false);
    }

    private void FreezeTimeline()
    {
        _timeline.Freeze();
        var frame = _timeline.NowFrames;
        _systemTrack?.PadTo(frame);
        _micTrack?.PadTo(frame);
    }

    private async Task CloseFlowAsync(AudioFlow flow)
    {
        ref var open = ref flow == AudioFlow.System ? ref _system : ref _mic;
        var closing = open;
        open = null;
        if (closing is null)
        {
            return;
        }

        closing.Closing = true;
        closing.Source.PacketAvailable -= closing.Pipeline.OnPacket;
        var closed = Task.Run(() =>
        {
            try
            {
                closing.Source.Stop();
            }
            catch (Exception exception) when (IsCaptureFailure(exception))
            {
                Debug.WriteLine($"Capture did not stop cleanly: {exception.Message}");
            }

            DisposeQuietly(closing.Source);
        });

        try
        {
            await closed.WaitAsync(CloseLimit).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // It has stopped delivering, and it closes in the background if it ever comes back.
            Debug.WriteLine($"Gave up waiting for the {flow} capture to close");
        }
    }

    private AudioEngineStatus BuildStatus()
    {
        var system = Volatile.Read(ref _systemTrack);
        var mic = Volatile.Read(ref _micTrack);
        var openSystem = IsLive(_system) ? _system : null;
        var openMic = IsLive(_mic) ? _mic : null;
        var appMissing = _appMissing;
        var activity = !_started ? CaptureActivity.Stopped
            : _paused ? CaptureActivity.Paused
            : openSystem is null && openMic is null && !appMissing ? CaptureActivity.NoDevice
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
            (system?.MemoryBytes ?? 0) + (mic?.MemoryBytes ?? 0),
            WantedApp,
            appMissing);
    }

    // Asks for the status to be sent. One thread at a time sends, and it reads the status only when its turn comes:
    // whatever the order of the requests, handlers never overlap and the last event is always the last state.
    // The task ends once a status at least as new as this request has been through the handlers.
    private Task RequestPublish()
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_publishGate)
        {
            _publishWanted = true;
            _publishWaiters.Add(delivered);
            if (_publishRunning)
            {
                return delivered.Task;
            }

            _publishRunning = true;
        }

        ThreadPool.QueueUserWorkItem(_ => PublishWhileWanted());
        return delivered.Task;
    }

    private void PublishWhileWanted()
    {
        while (true)
        {
            List<TaskCompletionSource> waiters;
            lock (_publishGate)
            {
                if (!_publishWanted)
                {
                    _publishRunning = false;
                    return;
                }

                _publishWanted = false;
                waiters = _publishWaiters;
                _publishWaiters = [];
            }

            Publish();
            foreach (var waiter in waiters)
            {
                waiter.TrySetResult();
            }
        }
    }

    private void Publish()
    {
        try
        {
            var status = BuildStatus();
            if (status == _lastPublished)
            {
                return;
            }

            // Once the buffer is full this stops firing, and only real changes get through.
            _lastPublished = status;
            foreach (var listener in StatusChanged?.GetInvocationList() ?? [])
            {
                Notify((Action<AudioEngineStatus>)listener, status);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"Could not send the status: {exception}");
        }
    }

    // A broken listener must not take the capture down with it, nor deprive the other listeners of the status.
    private static void Notify(Action<AudioEngineStatus> listener, AudioEngineStatus status)
    {
        try
        {
            listener(status);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"StatusChanged handler failed: {exception}");
        }
    }

    private static int FramesOf(TimeSpan duration)
    {
        return (int)Math.Min(int.MaxValue / 4, Math.Round(duration.TotalSeconds * InternalFormat.SampleRate));
    }

    private static bool IsCaptureFailure(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException);
}
