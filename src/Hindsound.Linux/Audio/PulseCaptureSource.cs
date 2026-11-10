using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Linux.Audio;

/// <summary>
/// One record stream of the sound server, on a connection of its own, so that a server going away ends this capture by
/// itself. Packets come on the loop's thread, which is the audio thread the engine expects. The stream is corked until
/// <see cref="Start"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class PulseCaptureSource : ICaptureSource
{
    private readonly PulseConnection _connection;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _blockAlign;
    private GCHandle _self;
    private IntPtr _stream;
    private int _ended;

    // Written on the loop's thread only.
    private byte[] _hole = [];
    private long _position;
    private string? _failure;

    private PulseCaptureSource(PulseConnection connection, AudioFlow flow, string? deviceName)
    {
        _connection = connection;
        Format = PulseFormats.WaveFormatFor(flow);
        _blockAlign = Format.BlockAlign;
        DeviceName = deviceName;
    }

    public WaveFormat Format { get; }

    public string? DeviceName { get; }

    public event CapturePacketHandler? PacketAvailable;

    public event Action<Exception?>? Stopped;

    /// <summary>Opens the stream on <paramref name="sourceName"/>, corked, or throws if the server refuses it.</summary>
    /// <param name="flow">Which side, for the format.</param>
    /// <param name="sourceName">The source to record: a monitor for the PC's sound, an input for the microphone.</param>
    /// <param name="streamName">The name the desktop's sound settings show for this stream.</param>
    /// <param name="deviceName">The name to show the user.</param>
    public static PulseCaptureSource Open(AudioFlow flow, string sourceName, string streamName, string? deviceName)
    {
        var connection = PulseConnection.Open();
        var source = new PulseCaptureSource(connection, flow, deviceName);
        try
        {
            source.Connect(flow, sourceName, streamName);
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _ended) != 0, this);
        _connection.Locked(() => PulseNative.Release(PulseNative.StreamCork(_stream, 0, IntPtr.Zero, IntPtr.Zero)));
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
        // The loop's lock cannot be taken from its own thread, nor the loop stopped there.
        if (_connection.OnLoopThread)
        {
            _ = Task.Run(Dispose);
            return;
        }

        End();
        var stream = Interlocked.Exchange(ref _stream, IntPtr.Zero);
        if (stream != IntPtr.Zero)
        {
            _connection.Locked(() => PulseNative.StreamUnref(stream));
        }

        _connection.Dispose();

        // The loop is gone with the connection: no callback can still come with this handle.
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private void Connect(AudioFlow flow, string sourceName, string streamName)
    {
        _self = GCHandle.Alloc(this);
        var userData = GCHandle.ToIntPtr(_self);
        _connection.Locked(() =>
        {
            var spec = PulseFormats.SampleSpecFor(flow);
            var context = _connection.Context;
            _stream = PulseNative.StreamNew(context, streamName, &spec, IntPtr.Zero);
            if (_stream == IntPtr.Zero)
            {
                throw new IOException("The sound server refused a stream: " + PulseNative.ErrorOf(context));
            }

            PulseNative.StreamSetStateCallback(_stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnState, userData);
            PulseNative.StreamSetReadCallback(_stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, void>)&OnRead, userData);
            var attributes = PulseFormats.BufferAttrFor(flow);
            if (PulseNative.StreamConnectRecord(_stream, sourceName, &attributes, PulseFormats.RecordFlags) < 0)
            {
                throw new IOException($"The sound server refused to record {sourceName}: " + PulseNative.ErrorOf(context));
            }
        });

        if (!_ready.Task.Wait(PulseConnection.Patience))
        {
            throw new TimeoutException($"The sound server did not open {sourceName} in time.");
        }

        if (!_ready.Task.Result)
        {
            throw new IOException($"The sound server could not record {sourceName}: " + (Volatile.Read(ref _failure) ?? "unknown error"));
        }
    }

    // Disconnects the stream once. True for the call that did it. The stream itself is released by Dispose only: a listener
    // may stop the source from inside a packet, and the read that delivered it still drops it afterwards.
    private bool End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return false;
        }

        var stream = Volatile.Read(ref _stream);
        if (stream == IntPtr.Zero)
        {
            return true;
        }

        void Close()
        {
            PulseNative.StreamSetReadCallback(stream, IntPtr.Zero, IntPtr.Zero);
            PulseNative.StreamSetStateCallback(stream, IntPtr.Zero, IntPtr.Zero);
            PulseNative.StreamDisconnect(stream);
        }

        // From a callback, the loop's lock is already held.
        if (_connection.OnLoopThread)
        {
            Close();
        }
        else
        {
            _connection.Locked(Close);
        }

        return true;
    }

    // Off the loop's thread: a listener may close this source, and the connection cannot be closed from its own loop.
    private void RaiseStopped(Exception? exception) => _ = Task.Run(() => Stopped?.Invoke(exception));

    // The stream failed by itself after it was running: its device went away, or the server did.
    private void Fail(Exception exception)
    {
        if (End())
        {
            RaiseStopped(exception);
        }
    }

    private void ReadAll(IntPtr stream)
    {
        while (true)
        {
            IntPtr data;
            nuint length;
            if (PulseNative.StreamPeek(stream, &data, &length) < 0)
            {
                Fail(new IOException("The sound server stopped giving the recorded audio."));
                return;
            }

            // An empty buffer: nothing to read, and nothing to drop.
            if (length == 0)
            {
                return;
            }

            var bytes = (int)length;
            var hole = data == IntPtr.Zero;
            ReadOnlySpan<byte> packet;
            if (hole)
            {
                // Rare, and the only allocation here: the server has no data for this stretch.
                if (_hole.Length < bytes)
                {
                    _hole = new byte[bytes];
                }

                packet = _hole.AsSpan(0, bytes);
            }
            else
            {
                packet = new ReadOnlySpan<byte>((void*)data, bytes);
            }

            var flags = PulseFormats.FlagsOf(hole);
            try
            {
                PacketAvailable?.Invoke(packet, flags, _position, 0);
            }
            finally
            {
                // Dropped even after a hole: that is what moves the read index past it.
                PulseNative.StreamDrop(stream);
            }

            _position += bytes / _blockAlign;
            if (Volatile.Read(ref _ended) != 0)
            {
                return;
            }
        }
    }

    // The callbacks below run on the loop's thread, called by C: nothing may be thrown out of them.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(IntPtr stream, IntPtr userData)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is not PulseCaptureSource self)
            {
                return;
            }

            var state = PulseNative.StreamGetState(stream);
            if (state == PulseNative.StreamReady)
            {
                self._ready.TrySetResult(true);
            }
            else if (state is PulseNative.StreamFailed or PulseNative.StreamTerminated)
            {
                var reason = PulseNative.ErrorOf(self._connection.Context);
                Volatile.Write(ref self._failure, reason);

                // Before it was ready, the opening throws; after, the capture ends and says why.
                if (!self._ready.TrySetResult(false))
                {
                    self.Fail(new IOException("The capture ended: " + reason));
                }
            }
        }
        catch (Exception)
        {
            // The state stays as it was; an opening waiting for it ends with its time limit.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRead(IntPtr stream, nuint length, IntPtr userData)
    {
        PulseCaptureSource? self = null;
        try
        {
            self = GCHandle.FromIntPtr(userData).Target as PulseCaptureSource;
            self?.ReadAll(stream);
        }
        catch (Exception exception)
        {
            // A listener that throws ends the capture, as on Windows, where it ends the capture thread.
            try
            {
                self?.Fail(exception);
            }
            catch (Exception)
            {
                // Nothing more can be done from here.
            }
        }
    }
}
