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
    private GCHandle _self;
    private Thread? _thread;
    private int _started;
    private int _ended;
    private int _disposed;

    // On the loop's thread, or under its lock.
    private readonly Dictionary<PulseMonitorKey, Input> _inputs = [];
    private readonly HashSet<Input> _closing = [];
    private StreamLook? _look;
    private bool _lookAgain;

    // DIAG: lines for the CI, to a file named by HINDSOUND_APP_TRACE.
    private static readonly string? DiagPath = Environment.GetEnvironmentVariable("HINDSOUND_APP_TRACE");
    private static readonly Stopwatch DiagClock = Stopwatch.StartNew();

    internal static void Diag(string line)
    {
        if (DiagPath is null)
        {
            return;
        }

        lock (DiagClock)
        {
            File.AppendAllText(DiagPath, $"{DiagClock.ElapsedMilliseconds,7} {line}\n");
        }
    }

    private static readonly bool DiagKeep = Environment.GetEnvironmentVariable("HINDSOUND_APP_KEEP") == "1";
    private static readonly bool DiagKick = Environment.GetEnvironmentVariable("HINDSOUND_APP_KICK") == "1";
    private static readonly float[] DiagSilence = new float[4800];
    private readonly Dictionary<uint, IntPtr> _keepers = [];
    private Dictionary<uint, string> _sinkNames = [];

    // DIAG: a plain record stream on the monitor, low latency, whose data is dropped.
    private void Keep(Dictionary<uint, string> monitors)
    {
        foreach (var (sink, monitor) in monitors)
        {
            if (_keepers.ContainsKey(sink))
            {
                continue;
            }

            var spec = PulseFormats.SampleSpecFor(AudioFlow.System);
            var stream = PulseNative.StreamNew(_connection.Context, "diag keep", &spec, IntPtr.Zero);
            PulseNative.StreamSetReadCallback(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, void>)&OnKeepRead, IntPtr.Zero);
            var attributes = PulseFormats.BufferAttrFor(AudioFlow.System);
            var result = PulseNative.StreamConnectRecord(stream, monitor, &attributes, PulseAppRules.RecordFlags);
            Diag($"keep {monitor}: {result}");
            _keepers[sink] = stream;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnKeepRead(IntPtr stream, nuint length, IntPtr userData)
    {
        IntPtr data;
        nuint size;
        while (PulseNative.StreamPeek(stream, &data, &size) >= 0 && size > 0)
        {
            PulseNative.StreamDrop(stream);
        }
    }

    // DIAG: a short silence played on the output, which makes the server render it again from now.
    private void Kick(uint sink)
    {
        if (!_sinkNames.TryGetValue(sink, out var name))
        {
            Diag($"kick {sink}: no name");
            return;
        }

        var spec = PulseFormats.SampleSpecFor(AudioFlow.System);
        var stream = PulseNative.StreamNew(_connection.Context, "diag kick", &spec, IntPtr.Zero);
        PulseNative.StreamSetStateCallback(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnKickState, IntPtr.Zero);
        var attributes = new PaBufferAttr { MaxLength = uint.MaxValue, TargetLength = 4800 * 8, PreBuffer = 0, MinRequest = uint.MaxValue, FragmentSize = uint.MaxValue };
        var result = PulseNative.StreamConnectPlayback(stream, name, &attributes, 0, IntPtr.Zero, IntPtr.Zero);
        Diag($"kick {name}: {result}");
        _ = Task.Delay(600).ContinueWith(_ => _connection.Locked(() =>
        {
            PulseNative.StreamSetStateCallback(stream, IntPtr.Zero, IntPtr.Zero);
            if (PulseNative.StreamGetState(stream) == PulseNative.StreamReady)
            {
                PulseNative.StreamDisconnect(stream);
            }

            PulseNative.StreamUnref(stream);
        }));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnKickState(IntPtr stream, IntPtr userData)
    {
        if (PulseNative.StreamGetState(stream) == PulseNative.StreamReady)
        {
            fixed (float* data = DiagSilence)
            {
                var result = PulseNative.StreamWrite(stream, data, (nuint)(DiagSilence.Length * sizeof(float)), IntPtr.Zero, 0, 0);
                Diag($"kick written: {result}");
            }
        }
    }

    private PulseAppCaptureSource(PulseConnection connection, int target, string? name, IProcessSnapshot processes, Action<string> log)
    {
        _connection = connection;
        _target = target;
        _processes = processes;
        _log = log;
        DeviceName = name;
        Format = PulseFormats.WaveFormatFor(AudioFlow.System);
        _channels = Format.Channels;
        _mixer = new PulseAppMixer(_channels, InternalFormat.SampleRate * PrimeMilliseconds / 1000, InternalFormat.SampleRate * MaxMilliseconds / 1000);
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
    /// </summary>
    public static PulseAppCaptureSource Open(int targetProcessId, string? name, IProcessSnapshot processes, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(log);
        var connection = PulseConnection.Open();
        var source = new PulseAppCaptureSource(connection, targetProcessId, name, processes, log);
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
        _connection.Locked(() =>
        {
            foreach (var input in _inputs.Values.Concat(_closing).ToList())
            {
                Release(input, now: true);
            }

            foreach (var keeper in _keepers.Values)
            {
                PulseNative.StreamSetReadCallback(keeper, IntPtr.Zero, IntPtr.Zero);
                PulseNative.StreamDisconnect(keeper);
                PulseNative.StreamUnref(keeper);
            }

            _inputs.Clear();
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
                    if ((given + frames) / InternalFormat.SampleRate != given / InternalFormat.SampleRate)
                    {
                        Diag($"mix {given + frames}: {_mixer.DiagCounters()}");
                    }

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
        var look = new StreamLook(Looked);
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

    private void Follow(StreamLook look)
    {
        _sinkNames = look.Names;
        if (DiagKeep)
        {
            Keep(look.Monitors);
        }

        var processes = _processes.Take();
        var mine = PulseAppRules.StreamsOf(look.Streams, processes, _target, Environment.ProcessId);
        var (open, close) = PulseAppRules.Changes(_inputs.Keys, mine, look.Monitors);
        foreach (var key in close)
        {
            Release(_inputs[key]);
            _inputs.Remove(key);
        }

        // A recording that failed keeps its place until its stream ends, without its stream.
        foreach (var input in _inputs.Values)
        {
            if (input.Dead)
            {
                Release(input);
            }
        }

        foreach (var (key, monitor) in open)
        {
            _inputs[key] = OpenInput(key, monitor);
        }
    }

    private Input OpenInput(PulseMonitorKey key, string monitor)
    {
        var input = new Input(this, key);
        var spec = PulseFormats.SampleSpecFor(AudioFlow.System);
        var context = _connection.Context;
        var stream = PulseNative.StreamNew(context, StreamName, &spec, IntPtr.Zero);
        if (stream == IntPtr.Zero)
        {
            input.Dead = true;
            _log("a stream of the application could not be recorded: " + PulseNative.ErrorOf(context));
            return input;
        }

        input.Stream = stream;
        input.Handle = GCHandle.Alloc(input);
        var userData = GCHandle.ToIntPtr(input.Handle);
        PulseNative.StreamSetStateCallback(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnState, userData);
        PulseNative.StreamSetReadCallback(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, void>)&OnRead, userData);
        _mixer.Add(key);
        var attributes = PulseFormats.BufferAttrFor(AudioFlow.System);
        Diag($"open sink input {key.SinkInput} on {monitor}, fragsize {attributes.FragmentSize}");
        if (PulseNative.StreamSetMonitorStream(stream, key.SinkInput) < 0
            || PulseNative.StreamConnectRecord(stream, monitor, &attributes, PulseAppRules.RecordFlags) < 0)
        {
            _log("a stream of the application could not be recorded: " + PulseNative.ErrorOf(context));
            Release(input);
            input.Dead = true;
        }

        return input;
    }

    // On the loop's thread or under its lock. The callbacks are taken off before the stream is let go, so none comes after. A
    // stream still being created cannot be disconnected yet, and the context would keep it recording for nobody: it closes
    // itself once it is ready (OnState), unless <paramref name="now"/> says the connection closes anyway.
    private void Release(Input input, bool now = false)
    {
        _mixer.Remove(input.Key);
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
            var granted = state == PulseNative.StreamReady ? PulseNative.StreamGetBufferAttr(stream) : null;
            Diag($"stream {input.Key.SinkInput} state {state} closing {input.Closing}" + (granted is null ? "" : $" maxlength {granted->MaxLength} fragsize {granted->FragmentSize}"));
            if (DiagKick && state == PulseNative.StreamReady && !input.Closing)
            {
                input.Owner.Kick(input.Key.Sink);
            }

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

            if (state is PulseNative.StreamFailed or PulseNative.StreamTerminated && !input.Dead)
            {
                // PulseAudio ends it when the application's stream ends or moves: the next look lets it go, or records it again.
                input.Dead = true;
                input.Owner._mixer.Remove(input.Key);
                input.Owner.Look();
            }
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
                look.Names[sink->Index] = PulseNative.Text(sink->Name) ?? "";
            }
        }
        catch (Exception)
        {
            // An output missed: its streams are not recorded until the next look.
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
        private int _left = 2;

        public StreamLook(Action<StreamLook> done)
        {
            _done = done;
            _handle = GCHandle.Alloc(this);
        }

        public bool Failed { get; private set; }

        public Dictionary<uint, string> Monitors { get; } = [];

        public Dictionary<uint, string> Names { get; } = [];

        public List<PulsePlaybackStream> Streams { get; } = [];

        // Under the loop's lock.
        public void Issue(IntPtr context)
        {
            var userData = GCHandle.ToIntPtr(_handle);
            var operations = new[]
            {
                PulseNative.ContextGetSinkInfoList(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&OnSink, userData),
                PulseNative.ContextGetSinkInputInfoList(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&OnSinkInput, userData),
            };

            foreach (var operation in operations)
            {
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

        public bool Dead { get; set; }

        /// <summary>Let go while it was being created: it closes itself once the server answers.</summary>
        public bool Closing { get; set; }

        // DIAG
        private long _diagBytes;
        private int _diagChunks;
        private int _diagHoles;
        private int _diagMin = int.MaxValue;
        private int _diagMax;
        private long _diagSince = -1;
        private long _diagTotal;

        public void ReadAll(IntPtr stream)
        {
            var mixer = Owner._mixer;
            var now = DiagClock.ElapsedMilliseconds;
            if (_diagSince < 0)
            {
                Diag($"first data {Key.SinkInput}");
                _diagSince = now;
            }
            else if (now - _diagSince >= 1000)
            {
                Diag($"read {Key.SinkInput}: {_diagBytes / (Owner._channels * sizeof(float))} frames in {now - _diagSince} ms, total {_diagTotal / (Owner._channels * sizeof(float))}, {_diagChunks} chunks of {_diagMin}..{_diagMax} bytes, {_diagHoles} holes, readable {PulseNative.StreamReadableSize(stream)}");
                _diagSince = now;
                _diagBytes = 0;
                _diagChunks = 0;
                _diagHoles = 0;
                _diagMin = int.MaxValue;
                _diagMax = 0;
            }

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
                _diagBytes += bytes;
                _diagTotal += bytes;
                _diagChunks++;
                _diagMin = Math.Min(_diagMin, bytes);
                _diagMax = Math.Max(_diagMax, bytes);
                if (data == IntPtr.Zero)
                {
                    _diagHoles++;
                }

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
    }
}
