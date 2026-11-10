using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Linux.Audio;

/// <summary>
/// One connection to the sound server: a pa_threaded_mainloop and its context. libpulse calls back on the loop's own
/// thread, holding the loop's lock; every other thread takes that lock around each call (<see cref="Locked"/>). The waits
/// are on .NET events the callbacks set, so each one has a time limit, which pa_threaded_mainloop_wait has not.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class PulseConnection : IDisposable
{
    /// <summary>How long the server gets to answer, connecting or listing, before it counts as missing.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    /// <summary>The name the server shows for this app's connections.</summary>
    public const string ClientName = "Hindsound";

    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr _loop;
    private IntPtr _context;
    private GCHandle _self;
    private bool _started;
    private int _disposed;

    // Written on the loop's thread only.
    private bool _wasReady;
    private string? _failure;
    private Action<uint>? _onEvent;

    private PulseConnection()
    {
    }

    /// <summary>Raised on the loop's thread when a connection that was ready ends: the server stopped or went away.</summary>
    public event Action? Lost;

    /// <summary>The pa_context, for calls made under <see cref="Locked"/>.</summary>
    public IntPtr Context => _context;

    /// <summary>Whether the caller runs on the loop's thread, where the lock is already held and must not be taken.</summary>
    public bool OnLoopThread => _loop != IntPtr.Zero && PulseNative.ThreadedMainloopInThread(_loop) != 0;

    /// <summary>Connects to the server of the session, or throws: no libpulse, no server, or no answer in time.</summary>
    public static PulseConnection Open()
    {
        var connection = new PulseConnection();
        try
        {
            connection.Connect();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>What the server has right now, read on a connection of its own that is closed before the answer is returned.</summary>
    public static PulseServerState ReadServer()
    {
        var query = new ServerQuery();
        var handle = GCHandle.Alloc(query);
        try
        {
            using (var connection = Open())
            {
                var userData = GCHandle.ToIntPtr(handle);
                var context = connection._context;
                connection.Locked(() => IssueQueries(context, userData, query));
                if (!query.Done.Wait(Patience))
                {
                    throw new TimeoutException("The sound server did not list its devices in time.");
                }
            }

            // The loop has stopped with the connection: no callback can reach the query any more.
            return query.Result();
        }
        finally
        {
            handle.Free();
            query.Done.Dispose();
        }
    }

    /// <summary>Runs <paramref name="action"/> holding the loop's lock. Never from the loop's thread.</summary>
    public void Locked(Action action)
    {
        PulseNative.ThreadedMainloopLock(_loop);
        try
        {
            action();
        }
        finally
        {
            PulseNative.ThreadedMainloopUnlock(_loop);
        }
    }

    /// <summary>
    /// Asks the server for its events of outputs, inputs and itself. <paramref name="onEvent"/> gets each event's type, on the
    /// loop's thread.
    /// </summary>
    public void Subscribe(Action<uint> onEvent)
    {
        var userData = GCHandle.ToIntPtr(_self);
        Locked(() =>
        {
            _onEvent = onEvent;
            PulseNative.ContextSetSubscribeCallback(_context, SubscribeCallback(), userData);
            var operation = PulseNative.ContextSubscribe(_context, PulseFormats.SubscriptionMask, IntPtr.Zero, IntPtr.Zero);
            if (operation == IntPtr.Zero)
            {
                throw new IOException("The sound server refused to report its changes: " + PulseNative.ErrorOf(_context));
            }

            PulseNative.Release(operation);
        });
    }

    public void Dispose()
    {
        // Stopping the loop from its own thread would wait for itself.
        if (OnLoopThread)
        {
            _ = Task.Run(Dispose);
            return;
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_context != IntPtr.Zero)
        {
            var context = _context;
            void Close()
            {
                PulseNative.ContextSetStateCallback(context, IntPtr.Zero, IntPtr.Zero);
                PulseNative.ContextSetSubscribeCallback(context, IntPtr.Zero, IntPtr.Zero);
                PulseNative.ContextDisconnect(context);
                PulseNative.ContextUnref(context);
            }

            if (_started)
            {
                Locked(Close);
            }
            else
            {
                Close();
            }

            _context = IntPtr.Zero;
        }

        if (_loop != IntPtr.Zero)
        {
            if (_started)
            {
                PulseNative.ThreadedMainloopStop(_loop);
            }

            PulseNative.ThreadedMainloopFree(_loop);
            _loop = IntPtr.Zero;
        }

        // Only now: the loop is gone, so no callback can still come with this handle.
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private void Connect()
    {
        _loop = PulseNative.ThreadedMainloopNew();
        if (_loop == IntPtr.Zero)
        {
            throw new IOException("libpulse could not make its loop.");
        }

        _context = PulseNative.ContextNew(PulseNative.ThreadedMainloopGetApi(_loop), ClientName);
        if (_context == IntPtr.Zero)
        {
            throw new IOException("libpulse could not make a context.");
        }

        _self = GCHandle.Alloc(this);
        PulseNative.ContextSetStateCallback(_context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnState, GCHandle.ToIntPtr(_self));
        if (PulseNative.ContextConnect(_context, IntPtr.Zero, PulseNative.NoAutospawn, IntPtr.Zero) < 0)
        {
            throw new IOException("No sound server: " + PulseNative.ErrorOf(_context));
        }

        if (PulseNative.ThreadedMainloopStart(_loop) < 0)
        {
            throw new IOException("libpulse could not start its loop.");
        }

        _started = true;
        if (!_ready.Task.Wait(Patience))
        {
            throw new TimeoutException("The sound server did not answer in time.");
        }

        if (!_ready.Task.Result)
        {
            throw new IOException("The sound server refused the connection: " + (Volatile.Read(ref _failure) ?? "unknown error"));
        }
    }

    private static IntPtr SubscribeCallback() => (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, uint, IntPtr, void>)&OnSubscribe;

    private static void IssueQueries(IntPtr context, IntPtr userData, ServerQuery query)
    {
        var operations = new[]
        {
            PulseNative.ContextGetServerInfo(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnServerInfo, userData),
            PulseNative.ContextGetSinkInfoList(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&OnSinkInfo, userData),
            PulseNative.ContextGetSourceInfoList(context, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, IntPtr, void>)&OnSourceInfo, userData),
        };

        foreach (var operation in operations)
        {
            // A call the server refused will never answer: it counts as answered, and failed.
            if (operation == IntPtr.Zero)
            {
                query.PartDone(false);
            }

            PulseNative.Release(operation);
        }
    }

    // The callbacks below run on the loop's thread, called by C: nothing may be thrown out of them.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(IntPtr context, IntPtr userData)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is not PulseConnection self)
            {
                return;
            }

            var state = PulseNative.ContextGetState(context);
            if (state == PulseNative.ContextReady)
            {
                self._wasReady = true;
                self._ready.TrySetResult(true);
            }
            else if (state is PulseNative.ContextFailed or PulseNative.ContextTerminated)
            {
                Volatile.Write(ref self._failure, self._failure ?? PulseNative.ErrorOf(context));
                self._ready.TrySetResult(false);
                if (self._wasReady)
                {
                    self._wasReady = false;
                    self.Lost?.Invoke();
                }
            }
        }
        catch (Exception)
        {
            // The state stays as it was; a wait on it ends with its time limit.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSubscribe(IntPtr context, uint eventType, uint index, IntPtr userData)
    {
        try
        {
            (GCHandle.FromIntPtr(userData).Target as PulseConnection)?._onEvent?.Invoke(eventType);
        }
        catch (Exception)
        {
            // One event missed; the engine's own periodic check still finds a new device.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnServerInfo(IntPtr context, IntPtr info, IntPtr userData)
    {
        try
        {
            var query = (ServerQuery)GCHandle.FromIntPtr(userData).Target!;
            if (info == IntPtr.Zero)
            {
                query.PartDone(false);
                return;
            }

            var server = (PaServerInfo*)info;
            query.DefaultSink = PulseNative.Text(server->DefaultSinkName);
            query.DefaultSource = PulseNative.Text(server->DefaultSourceName);
            query.PartDone(true);
        }
        catch (Exception)
        {
            // The query then ends with its time limit.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSinkInfo(IntPtr context, IntPtr info, int endOfList, IntPtr userData)
    {
        try
        {
            var query = (ServerQuery)GCHandle.FromIntPtr(userData).Target!;
            if (endOfList != 0)
            {
                query.PartDone(endOfList > 0);
                return;
            }

            var sink = (PaSinkInfo*)info;
            if (info != IntPtr.Zero && PulseNative.Text(sink->Name) is { } name)
            {
                query.Sinks.Add(new PulseSink(name, PulseServerState.ShownName(name, PulseNative.Text(sink->Description)), PulseNative.Text(sink->MonitorSourceName)));
            }
        }
        catch (Exception)
        {
            // The query then ends with its time limit.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSourceInfo(IntPtr context, IntPtr info, int endOfList, IntPtr userData)
    {
        try
        {
            var query = (ServerQuery)GCHandle.FromIntPtr(userData).Target!;
            if (endOfList != 0)
            {
                query.PartDone(endOfList > 0);
                return;
            }

            var source = (PaSourceInfo*)info;
            if (info != IntPtr.Zero && PulseNative.Text(source->Name) is { } name)
            {
                query.Sources.Add(new PulseSource(name, PulseServerState.ShownName(name, PulseNative.Text(source->Description)), source->IsMonitor));
            }
        }
        catch (Exception)
        {
            // The query then ends with its time limit.
        }
    }

    /// <summary>The three answers of <see cref="ReadServer"/>, filled on the loop's thread.</summary>
    private sealed class ServerQuery
    {
        private int _left = 3;
        private volatile bool _failed;

        public ManualResetEventSlim Done { get; } = new(false);

        public string? DefaultSink { get; set; }

        public string? DefaultSource { get; set; }

        public List<PulseSink> Sinks { get; } = [];

        public List<PulseSource> Sources { get; } = [];

        public void PartDone(bool succeeded)
        {
            if (!succeeded)
            {
                _failed = true;
            }

            if (Interlocked.Decrement(ref _left) == 0)
            {
                Done.Set();
            }
        }

        public PulseServerState Result() => _failed
            ? throw new IOException("The sound server did not list its devices.")
            : new PulseServerState(DefaultSink, DefaultSource, [.. Sinks], [.. Sources]);
    }
}
