using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The capture of one application and its children, as the process loopback of Windows does it: one record stream on each
/// playback stream of the application (pa_stream_set_monitor_stream, on the monitor of the output it plays on), mixed into one
/// stream that never stops. The application's streams are followed from the server's events: a stream that starts is recorded,
/// one that ends is closed. PulseAudio ends a record stream whose playback stream ended, PipeWire keeps it on silence and says
/// nothing, so the capture never waits to be told and closes it itself.
/// <para>
/// PulseAudio gives a record stream on a playback stream its sound only once the output has played it, and an output that no
/// client asks to be quick renders up to two seconds ahead: a short sound would be rendered, its stream ended, and its record
/// stream ended with it before it was played, so nothing of it would be heard. Each output therefore holds one plain record
/// stream on its monitor that asks for a short delay, and whose sound is dropped: the output then renders a few milliseconds
/// ahead. What it had rendered before is played first, so for about two seconds after the capture is opened a sound of the
/// application may still be missed: that is the server's, and the capture does not wait for it. PipeWire has no need of the
/// pacers, and would keep each output awake for them, which costs a laptop its battery: there the capture opens none. It knows
/// PipeWire by the name its server gives itself, "PulseAudio (on PipeWire x.y.z)", asked once when it opens; an output's driver
/// would say it too, but only through each output, while the name is one answer that never changes for a connection.
/// </para>
/// <para>
/// The mix runs on a thread of its own, which is the audio thread the engine expects: it gives the engine the frames its clock
/// asks for, silence when the application plays nothing, so the time of the clip goes on as with the whole PC. Everything on
/// the server is done on one connection of its own; its loop's thread only writes into the mixer.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class PulseAppCaptureSource : ICaptureSource
{
    /// <summary>The name the desktop's sound settings show for each stream recording the application.</summary>
    public const string StreamName = "Sound of the chosen application";

    /// <summary>How often the mix gives the engine what it has.</summary>
    public const int TickMilliseconds = 10;

    /// <summary>How much a stream holds before it counts, and at most: about two packets, and a quarter of a second.</summary>
    public const int PrimeMilliseconds = 2 * PulseFormats.FragmentMilliseconds;

    public const int MaxMilliseconds = 250;

    /// <summary>The name of the stream on each output that keeps its delay short.</summary>
    public const string PaceName = "Short delay for the chosen application";

    private const int MaxChunkFrames = InternalFormat.SampleRate / 10;

    // PA_STREAM_CREATING.
    private const int StreamCreating = 1;

    private readonly PulseConnection _connection;
    private readonly int _target;
    private readonly IProcessSnapshot _processes;
    private readonly Action<string> _log;
    private readonly int _channels;
    private readonly PulseAppMixer _mixer;
    private readonly TaskCompletionSource<bool> _firstLook = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Timer _retry;
    private readonly TimeSpan _steady;
    private GCHandle _self;
    private Thread? _thread;
    private int _started;
    private int _ended;
    private int _disposed;

    // On the loop's thread, or under its lock.
    private readonly Dictionary<PulseMonitorKey, Input> _inputs = [];
    private readonly HashSet<Input> _closing = [];
    private readonly Dictionary<uint, Input> _pacers = [];
    private readonly Dictionary<uint, (int? ProcessId, string? Binary, bool Mine)> _owners = [];
    private StreamLook? _look;
    private bool _lookAgain;
    private bool _serverKnown;
    private bool _paced = true;

    internal PulseAppCaptureSource(PulseConnection connection, int target, string? name, IProcessSnapshot processes, Action<string> log, TimeSpan steady)
    {
        _connection = connection;
        _steady = steady;
        _target = target;
        _processes = processes;
        _log = log;
        DeviceName = name;
        Format = PulseFormats.WaveFormatFor(AudioFlow.System);
        _channels = Format.Channels;
        _mixer = new PulseAppMixer(_channels, InternalFormat.SampleRate * PrimeMilliseconds / 1000, InternalFormat.SampleRate * MaxMilliseconds / 1000);
        _retry = new Timer(_ => Retry());
    }

    public WaveFormat Format { get; }

    public string? DeviceName { get; }

    /// <summary>The streams of the application being recorded now.</summary>
    public int RecordedStreams => _mixer.Count;

    /// <summary>The record streams this capture holds on the server, those still closing included.</summary>
    public int HeldStreams
    {
        get
        {
            var held = 0;
            _connection.Locked(() => held = _inputs.Values.Count(input => input.Stream != IntPtr.Zero) + _closing.Count);
            return held;
        }
    }

    public event CapturePacketHandler? PacketAvailable;

    public event Action<Exception?>? Stopped;

    /// <summary>
    /// Opens the capture of <paramref name="targetProcessId"/> and its children, or throws if the server cannot be reached or
    /// does not list its streams. An application that plays nothing yet is no failure: it is recorded once it does.
    /// <paramref name="steady"/> stands in for <see cref="PulseAppRules.SteadyMilliseconds"/>, for the tests.
    /// </summary>
    public static PulseAppCaptureSource Open(int targetProcessId, string? name, IProcessSnapshot processes, Action<string> log, TimeSpan? steady = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(log);
        var connection = PulseConnection.Open();
        var source = new PulseAppCaptureSource(connection, targetProcessId, name, processes, log, steady ?? TimeSpan.FromMilliseconds(PulseAppRules.SteadyMilliseconds));
        try
        {
            source.Begin();
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>The playback streams of the server now, read on a connection of its own that is closed before they are returned.</summary>
    public static IReadOnlyList<PulsePlaybackStream> ReadStreams()
    {
        using var done = new ManualResetEventSlim(false);
        var look = new StreamLook(_ => done.Set());
        try
        {
            using (var connection = PulseConnection.Open())
            {
                connection.Locked(() => look.Issue(connection.Context));
                if (!done.Wait(PulseConnection.Patience))
                {
                    throw new TimeoutException("The sound server did not list its playback streams in time.");
                }
            }

            // The loop has stopped with the connection: no callback can reach the look any more.
            return look.Failed ? throw new IOException("The sound server did not list its playback streams.") : [.. look.Streams];
        }
        finally
        {
            look.Free();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _ended) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Hindsound app capture", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        if (End())
        {
            RaiseStopped(null);
        }
    }

    public void Dispose()
    {
        // The loop's lock cannot be taken from its own thread, nor the mix's thread waited for from itself.
        if (_connection.OnLoopThread || Thread.CurrentThread == _thread)
        {
            _ = Task.Run(Dispose);
            return;
        }

        End();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _thread?.Join(TimeSpan.FromSeconds(2));

        // A try again under way ends before the connection: it would take the lock of a loop that is gone.
        using (var idle = new ManualResetEvent(false))
        {
            if (_retry.Dispose(idle))
            {
                idle.WaitOne(PulseConnection.Patience);
            }
        }

        _connection.Locked(() =>
        {
            foreach (var input in _inputs.Values.Concat(_pacers.Values).Concat(_closing).ToList())
            {
                Release(input, now: true);
            }

            _inputs.Clear();
            _pacers.Clear();
        });
        _connection.Dispose();

        // The loop is gone with the connection: no callback can still come with these handles.
        _look?.Free();
        _look = null;
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private void Begin()
    {
        _self = GCHandle.Alloc(this);
        _connection.Lost += OnLost;

        // Told first, then read: a stream that starts in between is in the list, or comes as an event.
        _connection.Subscribe(OnEvent, PulseAppRules.SubscriptionMask);
        _connection.Locked(Look);
        if (!_firstLook.Task.Wait(PulseConnection.Patience))
        {
            throw new TimeoutException("The sound server did not list its playback streams in time.");
        }

        if (!_firstLook.Task.Result)
        {
            throw new IOException("The sound server did not list its playback streams.");
        }
    }

    // True for the call that ended the capture. The streams stay open until Dispose, the mix's thread stops.
    private bool End() => Interlocked.Exchange(ref _ended, 1) == 0;

    // Off the loop's thread and the mix's: a listener may close this source, and neither can close it from itself.
    private void RaiseStopped(Exception? exception) => _ = Task.Run(() => Stopped?.Invoke(exception));

    private void Fail(Exception exception)
    {
        if (End())
        {
            RaiseStopped(exception);
        }
    }

    // The mix's thread: the frames the clock asks for since the start, in packets of at most a tenth of a second.
    private void Run()
    {
        try
        {
            var buffer = new float[MaxChunkFrames * _channels];
            var clock = Stopwatch.StartNew();
            long given = 0;
            while (Volatile.Read(ref _ended) == 0)
            {
                Thread.Sleep(TickMilliseconds);
                var due = (long)((Int128)clock.ElapsedTicks * InternalFormat.SampleRate / Stopwatch.Frequency) - given;
                while (due > 0 && Volatile.Read(ref _ended) == 0)
                {
                    var frames = (int)Math.Min(due, MaxChunkFrames);
                    var samples = buffer.AsSpan(0, frames * _channels);
                    var heard = _mixer.Mix(samples);
                    var flags = heard ? PulseFormats.FlagsOf(hole: false) : PulseFormats.FlagsOf(hole: true);
                    PacketAvailable?.Invoke(MemoryMarshal.AsBytes(samples), flags, given, 0);
                    given += frames;
                    due -= frames;
                }
            }
        }
        catch (Exception exception)
        {
            // A listener that throws ends the capture, as on Windows, where it ends the capture thread.
            Fail(exception);
        }
    }

    private void OnLost() => Fail(new IOException("The sound server went away."));

    // On a thread of the pool, once, when a recording that failed is due to be tried again: one more look does it.
    private void Retry()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _connection.Locked(Look);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log("a recording of the application could not be tried again: " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    // How long a recording that failed still waits before it is tried again; null once it has had all its tries.
    private static TimeSpan? WaitOf(Input input)
    {
        if (input.Tries >= PulseAppRules.MaxRetries)
        {
            return null;
        }

        var wait = TimeSpan.FromMilliseconds(PulseAppRules.RetryMilliseconds << input.Tries) - Stopwatch.GetElapsedTime(input.DeadAt);
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private void OnEvent(uint eventType)
    {
        if (PulseAppRules.IsStreamChange(eventType))
        {
            Look();
        }
    }

    // On the loop's thread or under its lock: reads the outputs and the playback streams, then changes what is recorded. One
    // reading at a time; events during it ask for one more after it, so a burst of them costs two.
    private void Look()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_look is not null)
        {
            _lookAgain = true;
            return;
        }

        _lookAgain = false;
        var look = new StreamLook(Looked, askServer: !_serverKnown);
        _look = look;
        look.Issue(_connection.Context);
    }

    private void Looked(StreamLook look)
    {
        if (!ReferenceEquals(_look, look))
        {
            return;
        }

        _look = null;
        look.Free();
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!look.Failed)
        {
            try
            {
                Follow(look);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _log("the streams of the application could not be followed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        _firstLook.TrySetResult(!look.Failed);
        if (_lookAgain)
        {
            Look();
        }
    }

    /// <summary>Whether a server that gives itself this name is PipeWire, through pipewire-pulse.</summary>
    internal static bool IsPipeWire(string? serverName) =>
        serverName is not null && serverName.StartsWith("PulseAudio (on PipeWire ", StringComparison.Ordinal);

    private void Follow(StreamLook look)
    {
        if (look.AskedServer && !_serverKnown)
        {
            // A server that did not say its name is taken for PulseAudio: its outputs are kept quick, as they must be there.
            _serverKnown = true;
            _paced = !IsPipeWire(look.ServerName);
            _log($"sound server: {look.ServerName ?? "(no name)"}, " + (_paced ? "each output kept quick" : "no output kept quick"));
        }

        if (_paced)
        {
            Pace(look.Monitors);
        }

        var mine = Mine(look.Streams);
        var (open, close) = PulseAppRules.Changes(_inputs.Keys, mine, look.Monitors);
        foreach (var key in close)
        {
            Release(_inputs[key]);
            _inputs.Remove(key);
        }

        // A recording that failed keeps its place until its stream ends, without its stream, and is tried again a few times
        // while its stream lives, later each time; one the server had ready for long counts its tries from zero again.
        foreach (var (key, input) in _inputs.ToList())
        {
            if (input.Dead)
            {
                Release(input);
                var wait = WaitOf(input);
                if (wait is null && input.WasReady && !input.GivenUp)
                {
                    input.GivenUp = true;
                    _log($"a recording of the application ended while its stream plays (try {input.Tries + 1} of {PulseAppRules.MaxRetries + 1}), not recorded again");
                }
                else if (wait == TimeSpan.Zero)
                {
                    // PipeWire starts the recording of a stream it moves off an output that went away, then ends it as the
                    // move settles, saying nothing: told here, as it is tried again.
                    if (input.WasReady)
                    {
                        _log($"a recording of the application ended while its stream plays (try {input.Tries + 1} of {PulseAppRules.MaxRetries + 1}), recorded again");
                    }

                    _inputs[key] = OpenInput(key, look.Monitors[key.Sink], tries: input.Tries + 1);
                }
            }
        }

        foreach (var (key, monitor) in open)
        {
            _inputs[key] = OpenInput(key, monitor);
        }

        // One look again when the next try is due: once, never in a loop.
        TimeSpan? next = null;
        foreach (var input in _inputs.Values.Concat(_pacers.Values))
        {
            if (input.Dead && WaitOf(input) is { } wait && (next is null || wait < next))
            {
                next = wait;
            }
        }

        if (next is { } due)
        {
            _retry.Change(due + TimeSpan.FromMilliseconds(20), Timeout.InfiniteTimeSpan);
        }
    }

    // The application's streams among those of a look. /proc is read again only when a stream is new, or now says another
    // program plays it: a stream's process and its place in the tree of the application do not change while it lives, so a
    // change of its volume, of its properties or of its output costs no reading.
    private List<PulsePlaybackStream> Mine(List<PulsePlaybackStream> streams)
    {
        var known = streams.TrueForAll(stream => _owners.TryGetValue(stream.Index, out var seen)
            && seen.ProcessId == stream.ProcessId
            && string.Equals(seen.Binary, stream.Binary, StringComparison.Ordinal));
        if (!known)
        {
            var mine = PulseAppRules.StreamsOf(streams, _processes.Take(), _target, Environment.ProcessId).Select(stream => stream.Index).ToHashSet();
            _owners.Clear();
            foreach (var stream in streams)
            {
                _owners[stream.Index] = (stream.ProcessId, stream.Binary, mine.Contains(stream.Index));
            }
        }

        return streams.FindAll(stream => _owners[stream.Index].Mine);
    }

    // One pacer on each output; it goes with its output. One that failed is tried again a few times, later each time, so that
    // an output that refuses it costs a few tries, not one per look.
    private void Pace(Dictionary<uint, string> monitors)
    {
        foreach (var sink in _pacers.Keys.Where(sink => !monitors.ContainsKey(sink)).ToList())
        {
            Release(_pacers[sink]);
            _pacers.Remove(sink);
        }

        foreach (var (sink, monitor) in monitors)
        {
            if (!_pacers.TryGetValue(sink, out var pacer))
            {
                _pacers[sink] = OpenInput(new PulseMonitorKey(uint.MaxValue, sink), monitor, pace: true);
            }
            else if (pacer.Dead && WaitOf(pacer) is null && pacer.WasReady && !pacer.GivenUp)
            {
                pacer.GivenUp = true;
                _log($"the short delay of an output ended while its output is there (try {pacer.Tries + 1} of {PulseAppRules.MaxRetries + 1}), not kept again");
            }
            else if (pacer.Dead && WaitOf(pacer) == TimeSpan.Zero)
            {
                if (pacer.WasReady)
                {
                    _log($"the short delay of an output ended while its output is there (try {pacer.Tries + 1} of {PulseAppRules.MaxRetries + 1}), kept again");
                }

                Release(pacer);
                _pacers[sink] = OpenInput(pacer.Key, monitor, pace: true, tries: pacer.Tries + 1);
            }
        }
    }

    private Input OpenInput(PulseMonitorKey key, string monitor, bool pace = false, int tries = 0)
    {
        var input = new Input(this, key) { Pace = pace, Tries = tries };
        var spec = PulseFormats.SampleSpecFor(AudioFlow.System);
        var context = _connection.Context;
        var stream = PulseNative.StreamNew(context, pace ? PaceName : StreamName, &spec, IntPtr.Zero);
        if (stream == IntPtr.Zero)
        {
            input.Die();
            LogFailure(input, PulseNative.ErrorOf(context));
            return input;
        }

        input.Stream = stream;
        input.Handle = GCHandle.Alloc(input);
        var userData = GCHandle.ToIntPtr(input.Handle);
        PulseNative.StreamSetStateCallback(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnState, userData);
        PulseNative.StreamSetReadCallback(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, void>)&OnRead, userData);
        var attributes = PulseFormats.BufferAttrFor(AudioFlow.System);
        if ((!pace && PulseNative.StreamSetMonitorStream(stream, key.SinkInput) < 0)
            || PulseNative.StreamConnectRecord(stream, monitor, &attributes, PulseAppRules.RecordFlags) < 0)
        {
            LogFailure(input, PulseNative.ErrorOf(context));
            Release(input);
            input.Die();
        }
        else if (!pace)
        {
            // Under the loop's lock: no packet can come before it is in the mix.
            _mixer.Add(key);
        }

        return input;
    }

    // On the loop's thread or under its lock. The callbacks are taken off before the stream is let go, so none comes after. A
    // stream still being created cannot be disconnected yet, and the context would keep it recording for nobody: it closes
    // itself once it is ready (OnState), unless <paramref name="now"/> says the connection closes anyway.
    private void Release(Input input, bool now = false)
    {
        Unmix(input);
        var stream = input.Stream;
        if (stream != IntPtr.Zero && !now && PulseNative.StreamGetState(stream) == StreamCreating)
        {
            input.Closing = true;
            _closing.Add(input);
            return;
        }

        input.Stream = IntPtr.Zero;
        _closing.Remove(input);
        if (stream != IntPtr.Zero)
        {
            PulseNative.StreamSetReadCallback(stream, IntPtr.Zero, IntPtr.Zero);
            PulseNative.StreamSetStateCallback(stream, IntPtr.Zero, IntPtr.Zero);
            if (PulseNative.StreamGetState(stream) == PulseNative.StreamReady)
            {
                PulseNative.StreamDisconnect(stream);
            }

            PulseNative.StreamUnref(stream);
        }

        if (input.Handle.IsAllocated)
        {
            input.Handle.Free();
        }
    }

    private void LogFailure(Input input, string error)
    {
        var what = input.Pace ? "the short delay of an output could not be kept" : "a stream of the application could not be recorded";
        var next = input.Tries < PulseAppRules.MaxRetries ? $", tried again in {PulseAppRules.RetryMilliseconds << input.Tries} ms" : ", not tried again";
        _log($"{what} (try {input.Tries + 1} of {PulseAppRules.MaxRetries + 1}{next}): {error}");
    }

    // Takes a recording out of the mix, unless a newer recording of the same stream holds its place there now: one let go
    // while it was being created ends later, and must not take the sound of the one opened after it.
    private void Unmix(Input input)
    {
        if (!input.Pace && (!_inputs.TryGetValue(input.Key, out var current) || ReferenceEquals(current, input)))
        {
            _mixer.Remove(input.Key);
        }
    }

    // The callbacks below run on the loop's thread, called by C: nothing may be thrown out of them.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(IntPtr stream, IntPtr userData)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is not Input input)
            {
                return;
            }

            var state = PulseNative.StreamGetState(stream);
            if (input.Closing)
            {
                if (state == PulseNative.StreamReady)
                {
                    PulseNative.StreamDisconnect(stream);
                }
                else if (state is PulseNative.StreamFailed or PulseNative.StreamTerminated)
                {
                    input.Owner.Release(input, now: true);
                }

                return;
            }

            if (state == PulseNative.StreamReady)
            {
                input.WasReady = true;
                input.ReadyAt = Stopwatch.GetTimestamp();
                return;
            }

            if (state is not (PulseNative.StreamFailed or PulseNative.StreamTerminated) || input.Dead)
            {
                return;
            }

            // PulseAudio ends a recording when the application's stream ends or moves, and a pacer when its output goes away,
            // which is no failure: the next look lets it go, or records the stream on its new output. One that never started
            // failed, and is told; the next look tries it again later while its stream or its output lives.
            var owner = input.Owner;
            if (!input.WasReady)
            {
                owner.LogFailure(input, PulseNative.ErrorOf(owner._connection.Context));
            }

            input.Die();
            input.Tries = PulseAppRules.TriesAfterEnd(input.Tries, null, owner._steady);
            owner.Unmix(input);
            owner.Look();
        }
        catch (Exception)
        {
            // The stream stays as it was; the next look sees whether its playback stream is still there.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRead(IntPtr stream, nuint length, IntPtr userData)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is Input input)
            {
                input.ReadAll(stream);
            }
        }
        catch (Exception)
        {
            // A packet lost: the mix reads silence for it.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSink(IntPtr context, IntPtr info, int endOfList, IntPtr userData)
    {
        StreamLook? look = null;
        try
        {
            look = (StreamLook)GCHandle.FromIntPtr(userData).Target!;
            if (endOfList != 0)
            {
                look.PartDone(endOfList > 0);
                return;
            }

            var sink = (PaSinkInfo*)info;
            if (info != IntPtr.Zero && PulseNative.Text(sink->MonitorSourceName) is { } monitor)
            {
                look.Monitors[sink->Index] = monitor;
            }
        }
        catch (Exception)
        {
            // An output missed: its streams are not recorded until the next look.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnServer(IntPtr context, IntPtr info, IntPtr userData)
    {
        StreamLook? look = null;
        try
        {
            look = (StreamLook)GCHandle.FromIntPtr(userData).Target!;
            if (info != IntPtr.Zero)
            {
                look.ServerName = PulseNative.Text(((PaServerInfo*)info)->ServerName);
            }
        }
        catch (Exception)
        {
            // No name: the server is taken for PulseAudio.
        }

        try
        {
            // A name missing fails nothing: the look still has its streams.
            look?.PartDone(true);
        }
        catch (Exception)
        {
            // The look's end is the source's to handle; nothing may leave a callback.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSinkInput(IntPtr context, IntPtr info, int endOfList, IntPtr userData)
    {
        try
        {
            var look = (StreamLook)GCHandle.FromIntPtr(userData).Target!;
            if (endOfList != 0)
            {
                look.PartDone(endOfList > 0);
                return;
            }

            if (info == IntPtr.Zero)
            {
                return;
            }

            var input = (PaSinkInputInfo*)info;
            var properties = input->Proplist;
            string? Property(string key) => properties == IntPtr.Zero ? null : PulseNative.Text(PulseNative.ProplistGets(properties, key));
            int? processId = int.TryParse(Property("application.process.id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
            look.Streams.Add(new PulsePlaybackStream(input->Index, input->Sink, processId, Property("application.process.binary"), Property("application.name")));
        }
        catch (Exception)
        {
            // A stream missed: it is recorded at the next look.
        }
    }

    /// <summary>One reading of the outputs and the playback streams, filled on the loop's thread.</summary>
    private sealed class StreamLook
    {
        private readonly Action<StreamLook> _done;
        private GCHandle _handle;
        private int _left;

        /// <param name="askServer">Whether to ask the server's name too.</param>
        public StreamLook(Action<StreamLook> done, bool askServer = false)
        {
            _done = done;
            AskedServer = askServer;
            _left = askServer ? 3 : 2;
            _handle = GCHandle.Alloc(this);
        }

        public bool Failed { get; private set; }

        public bool AskedServer { get; }

        /// <summary>The name the server gives itself, if it was asked and said it.</summary>
        public string? ServerName { get; set; }

        public Dictionary<uint, string> Monitors { get; } = [];

        public List<PulsePlaybackStream> Streams { get; } = [];

        // Under the loop's lock.
        public void Issue(IntPtr context)
        {
            var userData = GCHandle.ToIntPtr(_handle);
            var operations = new[]
            {
                PulseNative.ContextGetSinkInfoList(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&OnSink, userData),
                PulseNative.ContextGetSinkInputInfoList(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&OnSinkInput, userData),
                AskedServer ? PulseNative.ContextGetServerInfo(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnServer, userData) : -1,
            };

            foreach (var operation in operations)
            {
                // -1: not asked.
                if (operation == -1)
                {
                    continue;
                }

                // A call the server refused will never answer: it counts as answered, and failed.
                if (operation == IntPtr.Zero)
                {
                    PartDone(false);
                }

                PulseNative.Release(operation);
            }
        }

        public void PartDone(bool succeeded)
        {
            Failed |= !succeeded;
            if (--_left == 0)
            {
                _done(this);
            }
        }

        // Once no callback can come: the look answered, or its connection is closed.
        public void Free()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }
    }

    /// <summary>One record stream, on one playback stream of the application.</summary>
    private sealed class Input(PulseAppCaptureSource owner, PulseMonitorKey key)
    {
        public PulseAppCaptureSource Owner { get; } = owner;

        public PulseMonitorKey Key { get; } = key;

        public IntPtr Stream { get; set; }

        public GCHandle Handle;

        public bool Dead { get; private set; }

        /// <summary>When it failed, by <see cref="Stopwatch.GetTimestamp"/>.</summary>
        public long DeadAt { get; private set; }

        /// <summary>
        /// How many times it was tried before this one; none again once it ends after it held long enough
        /// (<see cref="PulseAppRules.TriesAfterEnd"/>).
        /// </summary>
        public int Tries { get; set; }

        /// <summary>Whether the server started it: one that ends after that has not failed to start.</summary>
        public bool WasReady { get; set; }

        /// <summary>When the server started it, by <see cref="Stopwatch.GetTimestamp"/>.</summary>
        public long ReadyAt { get; set; }

        /// <summary>Ended after it started, with all its tries had: told once.</summary>
        public bool GivenUp { get; set; }

        /// <summary>Let go while it was being created: it closes itself once the server answers.</summary>
        public bool Closing { get; set; }

        /// <summary>The stream that keeps an output's delay short: its sound is dropped.</summary>
        public bool Pace { get; init; }

        public void Die()
        {
            Dead = true;
            DeadAt = Stopwatch.GetTimestamp();
        }

        public void ReadAll(IntPtr stream)
        {
            if (Pace)
            {
                Drain(stream);
                return;
            }

            var mixer = Owner._mixer;
            var frameBytes = Owner._channels * sizeof(float);
            while (true)
            {
                IntPtr data;
                nuint length;
                if (PulseNative.StreamPeek(stream, &data, &length) < 0 || length == 0)
                {
                    return;
                }

                var bytes = (int)length;
                if (data == IntPtr.Zero)
                {
                    // A hole: the server has no data for this stretch.
                    mixer.WriteSilence(Key, bytes / frameBytes);
                }
                else
                {
                    mixer.Write(Key, new ReadOnlySpan<float>((void*)data, bytes / sizeof(float)));
                }

                PulseNative.StreamDrop(stream);
            }
        }

        private static void Drain(IntPtr stream)
        {
            while (true)
            {
                IntPtr data;
                nuint length;
                if (PulseNative.StreamPeek(stream, &data, &length) < 0 || length == 0)
                {
                    break;
                }

                PulseNative.StreamDrop(stream);
            }
        }
    }
}
