using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The preview's connection to the sound server, and its playback streams on the default output. Each stream asks its
/// track for samples when the server wants more, on the loop's thread, and drains at the end of the file. The streams
/// carry the app's name and id, and a property that says they are its preview, for the desktop's sound settings and for
/// whoever reads the server's streams.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class PulsePlaybackOutput : IPulsePlaybackOutput
{
    /// <summary>The name the desktop's sound settings show for the preview's stream.</summary>
    public const string StreamName = "Preview";

    /// <summary>The property that marks a stream as the app's preview, and its value.</summary>
    public const string RoleProperty = "hindsound.stream";
    public const string RoleValue = "preview";

    private readonly PulseConnection _connection;
    private readonly string? _sink;
    private volatile bool _lost;

    private PulsePlaybackOutput(PulseConnection connection, string? sink)
    {
        _connection = connection;
        _sink = sink;
        _connection.Lost += () => _lost = true;
    }

    public bool IsLost
    {
        get
        {
            if (_lost)
            {
                return true;
            }

            // The connection may have ended before the event was listened to.
            var state = 0;
            _connection.Locked(() => state = PulseNative.ContextGetState(_connection.Context));
            return state is PulseNative.ContextFailed or PulseNative.ContextTerminated;
        }
    }

    /// <summary>Connects to the server of the session, or throws. The streams play on the default output.</summary>
    public static PulsePlaybackOutput Connect() => new(PulseConnection.Open(), null);

    /// <summary>The same, with the streams on the output <paramref name="sink"/>: a test's own, so it does not move the default.</summary>
    internal static PulsePlaybackOutput Connect(string sink) => new(PulseConnection.Open(), sink);

    public IPulsePlaybackStream Open(int channels, IPulsePlaybackFeed feed) => PulsePlaybackStream.Open(_connection, _sink, channels, feed);

    public void Dispose() => _connection.Dispose();

    private sealed unsafe class PulsePlaybackStream : IPulsePlaybackStream
    {
        private readonly PulseConnection _connection;
        // A second of audio at most per call: the server asks for its target length, a tenth of that.
        private const int MostFramesPerWrite = Hindsound.Audio.InternalFormat.SampleRate;

        // The longest wait for the sink to play what it holds after the drain: past it, the track ends anyway.
        private const int LongestPlayOutMilliseconds = 2000;

        private readonly IPulsePlaybackFeed _feed;
        private readonly int _channels;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GCHandle _self;
        private IntPtr _stream;

        // Read and written holding the loop's lock: on the loop's thread, or under Locked.
        private float[] _buffer = [];
        private IntPtr _drain;
        private bool _draining;

        // After the drain: the server's answer on how long the sink still holds, then the wait for it to play.
        private IntPtr _timing;
        private Timer? _playedOut;

        // Waits given up while their callback may already be on its way to the loop's lock: Close waits for them too.
        private readonly List<Timer> _givenUp = [];

        // At a stop: the server's answer on how far it took the stream, and the stop waiting for it.
        private IntPtr _placing;
        private TaskCompletionSource<bool>? _placed;

        // Where the file is in the stream: its frame _startFrame went at the stream's byte _startIndex, and _written bytes
        // went in all, counted as the server counts its write index.
        private long _startFrame;
        private long _startIndex;
        private long _written;

        // Counts the drains given up: a wait of one given up does not end the track.
        private int _drainTurn;
        private bool _endReached;
        private bool _closed;

        private PulsePlaybackStream(PulseConnection connection, int channels, IPulsePlaybackFeed feed)
        {
            _connection = connection;
            _channels = channels;
            _feed = feed;
        }

        public static PulsePlaybackStream Open(PulseConnection connection, string? sink, int channels, IPulsePlaybackFeed feed)
        {
            if (channels is < 1 or > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), channels, "The preview plays one or two channels.");
            }

            var stream = new PulsePlaybackStream(connection, channels, feed);
            try
            {
                stream.Connect(sink);
                return stream;
            }
            catch
            {
                stream.Close();
                throw;
            }
        }

        public void Start() => Locked(() =>
        {
            ThrowIfGone();
            PulseNative.Release(PulseNative.StreamCork(_stream, 0, IntPtr.Zero, IntPtr.Zero));

            // The end of the file was reached while the stream was corked and its drain has answered already, to no one: drain again, now it plays.
            // A wait left from that answer is given up first, so it cannot end this play early.
            if (_endReached && _drain == IntPtr.Zero)
            {
                CancelDrain();
                StartDrain(_stream);
            }
        });

        public void Pause()
        {
            TaskCompletionSource<bool>? placed = null;
            Locked(() =>
            {
                ThrowIfGone();

                // A drain under way is dropped: Start drains again once the file's end was reached.
                CancelDrain();
                CancelPlacing();
                PulseNative.Release(PulseNative.StreamCork(_stream, 1, IntPtr.Zero, IntPtr.Zero));

                // Then the server says how far it took the stream, asked after the cork so the answer counts it. A stop then a
                // play goes on where the listener stopped hearing, as MCI does, whatever the output did with what it held.
                _placing = PulseNative.StreamUpdateTimingInfo(_stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&OnStopTiming, GCHandle.ToIntPtr(_self));
                if (_placing == IntPtr.Zero)
                {
                    // What the server holds is kept and plays at the next Start, as before.
                    _feed.Note("preview: a stop goes on from what the stream held (no timing, error " + PulseNative.ContextErrno(_connection.Context) + ")");
                    return;
                }

                _placed = placed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            });

            // Outside the loop's lock, which the answer needs. A Start before it would play from the old place for a moment.
            if (placed is not null && !placed.Task.Wait(PulseConnection.Patience))
            {
                Locked(() =>
                {
                    if (_placed == placed)
                    {
                        CancelPlacing();
                        _feed.Note("preview: a stop goes on from what the stream held (no timing in time)");
                    }
                });
            }
        }

        public void Close()
        {
            List<Timer> waits = [];
            if (_stream != IntPtr.Zero)
            {
                Locked(() =>
                {
                    if (_closed)
                    {
                        return;
                    }

                    _closed = true;
                    CancelDrain();
                    CancelPlacing();
                    waits.AddRange(_givenUp);
                    _givenUp.Clear();
                    PulseNative.StreamSetWriteCallback(_stream, IntPtr.Zero, IntPtr.Zero);
                    PulseNative.StreamSetStateCallback(_stream, IntPtr.Zero, IntPtr.Zero);
                    PulseNative.StreamDisconnect(_stream);
                    PulseNative.StreamUnref(_stream);
                });
            }

            // A wait for the end to play that had already fired takes the loop's lock, and finds the stream closed: every
            // wait, the current one and those given up by a stop, is waited for outside that lock, so neither the feed nor
            // the connection is touched by one once this returns.
            foreach (var wait in waits)
            {
                using var done = new ManualResetEvent(false);
                if (wait.Dispose(done))
                {
                    done.WaitOne();
                }
            }

            // Under the loop's lock no callback was running, and none is set any more, nor any drain left to answer.
            if (_self.IsAllocated)
            {
                _self.Free();
            }
        }

        private void Locked(Action action)
        {
            if (_connection.OnLoopThread)
            {
                throw new InvalidOperationException("A stream of the preview is driven from the preview's thread only.");
            }

            _connection.Locked(action);
        }

        private void ThrowIfGone()
        {
            if (_closed || PulseNative.StreamGetState(_stream) != PulseNative.StreamReady)
            {
                throw new IOException("The stream of the preview has ended.");
            }
        }

        private void Connect(string? sink)
        {
            _self = GCHandle.Alloc(this);
            var userData = GCHandle.ToIntPtr(_self);
            Locked(() =>
            {
                var context = _connection.Context;
                var spec = PulseFormats.PlaybackSpecFor(_channels);
                var properties = PulseNative.ProplistNew();
                if (properties == IntPtr.Zero)
                {
                    throw new IOException("libpulse could not make a property list.");
                }

                try
                {
                    PulseNative.ProplistSets(properties, "application.name", PulseConnection.ClientName);
                    PulseNative.ProplistSets(properties, "application.id", DBusNames.AppId);
                    PulseNative.ProplistSets(properties, "application.icon_name", DBusNames.AppId);
                    PulseNative.ProplistSets(properties, "media.name", StreamName);
                    PulseNative.ProplistSets(properties, RoleProperty, RoleValue);
                    _stream = PulseNative.StreamNewWithProplist(context, StreamName, &spec, IntPtr.Zero, properties);
                }
                finally
                {
                    PulseNative.ProplistFree(properties);
                }

                if (_stream == IntPtr.Zero)
                {
                    throw new IOException("The sound server refused a stream: " + PulseNative.ErrorOf(context));
                }

                PulseNative.StreamSetStateCallback(_stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnState, userData);
                PulseNative.StreamSetWriteCallback(_stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr, void>)&OnWrite, userData);
                var attributes = PulseFormats.PlaybackBufferAttrFor(_channels);
                if (PulseNative.StreamConnectPlayback(_stream, sink, &attributes, PulseFormats.StartCorked, IntPtr.Zero, IntPtr.Zero) < 0)
                {
                    throw new IOException("The sound server refused to play: " + PulseNative.ErrorOf(context));
                }
            });

            if (!_ready.Task.Wait(PulseConnection.Patience))
            {
                throw new TimeoutException("The sound server did not open the preview in time.");
            }

            if (!_ready.Task.Result)
            {
                throw new IOException("The sound server could not play the preview.");
            }
        }

        // Holding the loop's lock.
        private void CancelDrain()
        {
            if (_drain != IntPtr.Zero)
            {
                PulseNative.OperationCancel(_drain);
                PulseNative.OperationUnref(_drain);
                _drain = IntPtr.Zero;
            }

            if (_timing != IntPtr.Zero)
            {
                PulseNative.OperationCancel(_timing);
                PulseNative.OperationUnref(_timing);
                _timing = IntPtr.Zero;
            }

            // Stopped, not disposed: its callback may have fired already and wait for this lock. Close waits for it, and its
            // turn no longer matches, so it does nothing.
            if (_playedOut is not null)
            {
                _playedOut.Change(Timeout.Infinite, Timeout.Infinite);
                _givenUp.Add(_playedOut);
                _playedOut = null;
            }

            _drainTurn++;
            _draining = false;
        }

        // Holding the loop's lock.
        private void CancelPlacing()
        {
            if (_placing != IntPtr.Zero)
            {
                PulseNative.OperationCancel(_placing);
                PulseNative.OperationUnref(_placing);
                _placing = IntPtr.Zero;
            }

            _placed?.TrySetResult(false);
            _placed = null;
        }

        // Holding the loop's lock, once the server has said how far it took the corked stream. What it took and the sink still
        // held is played out by some outputs and dropped by others, without a word: see PulseFormats.HeardFrame. The file
        // goes back to the last frame heard and what the stream holds is flushed, so the next Start refills from there.
        private void PlaceAfterStop(IntPtr stream, bool answered)
        {
            var timing = answered ? PulseNative.StreamGetTimingInfo(stream) : null;
            if (timing is null || timing->ReadIndexCorrupt != 0)
            {
                _feed.Note(answered
                    ? "preview: a stop goes on from what the stream held (no read index)"
                    : "preview: a stop goes on from what the stream held (no timing, error " + PulseNative.ContextErrno(_connection.Context) + ")");
                return;
            }

            var readIndex = timing->ReadIndex;
            var frame = PulseFormats.HeardFrame(_startFrame, _startIndex, _written, readIndex, timing->SinkUsec, Hindsound.Audio.InternalFormat.SampleRate, _channels);

            // The file goes back first: one that can't keeps what the stream holds, as before.
            if (!_feed.Seek(PulseFormats.TimeOfFrame(frame, Hindsound.Audio.InternalFormat.SampleRate)))
            {
                return;
            }

            // The server sets its write index back to its read index: what is written next follows what it took.
            PulseNative.Release(PulseNative.StreamFlush(stream, IntPtr.Zero, IntPtr.Zero));
            _startFrame = frame;
            _startIndex = _written = readIndex;

            // The end of the file is behind the new place, unless all of it was heard: the writes find it again either way.
            _endReached = false;
        }

        // Holding the loop's lock, once the drain has answered. The server answers a drain once it has mixed the last sample
        // into the sink, not once it was heard: the sink still holds up to its latency ahead. A track that said it ended
        // then would be closed at once, and the server takes off a closed stream's sound what the sink has not played yet:
        // the end of the file would never be heard. So the end waits for what the sink holds, which the server says.
        private void AwaitPlayedOut(IntPtr stream)
        {
            _timing = PulseNative.StreamUpdateTimingInfo(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&OnTiming, GCHandle.ToIntPtr(_self));
            if (_timing == IntPtr.Zero)
            {
                // The server can't say: ends now, as the drain said.
                _feed.Drained(true);
            }
        }

        // Holding the loop's lock: ends the track once `microseconds` have played.
        private void EndAfter(ulong microseconds)
        {
            var milliseconds = (long)Math.Min((microseconds + 999) / 1000, (ulong)LongestPlayOutMilliseconds);
            if (milliseconds == 0)
            {
                _feed.Drained(true);
                return;
            }

            var turn = _drainTurn;
            _playedOut = new Timer(_ => PlayedOut(turn), null, milliseconds, Timeout.Infinite);
        }

        // On a thread of the pool.
        private void PlayedOut(int turn)
        {
            try
            {
                _connection.Locked(() =>
                {
                    if (_closed || turn != _drainTurn)
                    {
                        return;
                    }

                    _playedOut?.Dispose();
                    _playedOut = null;
                    _feed.Drained(true);
                });
            }
            catch (Exception)
            {
                // The track then ends with the hold's own deadline.
            }
        }

        // On the loop's thread.
        private void Write(IntPtr stream, nuint length)
        {
            var left = length / (nuint)(_channels * sizeof(float));
            while (left > 0 && !_closed && !_draining)
            {
                var frames = (int)Math.Min(left, (nuint)MostFramesPerWrite);
                left -= (nuint)frames;
                if (!WriteOnce(stream, frames))
                {
                    return;
                }
            }
        }

        // One read of the file and one write to the server. False once the stream has nothing more to write.
        private bool WriteOnce(IntPtr stream, int frames)
        {
            var floats = frames * _channels;
            if (_buffer.Length < floats)
            {
                _buffer = new float[floats];
            }

            var filled = _feed.Fill(_buffer.AsSpan(0, floats));
            if (filled < 0)
            {
                // The file failed: what is still queued is dropped, and the stream waits to be closed.
                PulseNative.StreamSetWriteCallback(stream, IntPtr.Zero, IntPtr.Zero);
                PulseNative.Release(PulseNative.StreamCork(stream, 1, IntPtr.Zero, IntPtr.Zero));
                PulseNative.Release(PulseNative.StreamFlush(stream, IntPtr.Zero, IntPtr.Zero));
                return false;
            }

            if (filled > 0)
            {
                fixed (float* data = _buffer)
                {
                    if (PulseNative.StreamWrite(stream, data, (nuint)(filled * sizeof(float)), IntPtr.Zero, 0, 0) < 0)
                    {
                        PulseNative.StreamSetWriteCallback(stream, IntPtr.Zero, IntPtr.Zero);
                        _feed.Failed("the server refused the samples");
                        return false;
                    }
                }

                _written += (long)filled * sizeof(float);
            }

            // Fill reads until the file ends or the buffer is full: less than asked is the end of the file.
            if (filled == floats)
            {
                return true;
            }

            _endReached = true;
            StartDrain(stream);
            return false;
        }

        // Holding the loop's lock.
        private void StartDrain(IntPtr stream)
        {
            _draining = true;
            _drain = PulseNative.StreamDrain(stream, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&OnDrained, GCHandle.ToIntPtr(_self));
            if (_drain == IntPtr.Zero)
            {
                _draining = false;
                _feed.Drained(false);
            }
        }

        // The callbacks below run on the loop's thread, called by C: nothing may be thrown out of them.

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnState(IntPtr stream, IntPtr userData)
        {
            try
            {
                if (GCHandle.FromIntPtr(userData).Target is not PulsePlaybackStream self || self._closed)
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
                    // Before it was ready, the opening throws; after, the track fails and says so.
                    if (!self._ready.TrySetResult(false))
                    {
                        self._feed.Failed("the sound server ended the stream");
                    }
                }
            }
            catch (Exception)
            {
                // The state stays as it was; an opening waiting for it ends with its time limit.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnWrite(IntPtr stream, nuint length, IntPtr userData)
        {
            try
            {
                (GCHandle.FromIntPtr(userData).Target as PulsePlaybackStream)?.Write(stream, length);
            }
            catch (Exception)
            {
                // The track hears nothing more from this stream; its own failure was already told by its feed.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnDrained(IntPtr stream, int success, IntPtr userData)
        {
            try
            {
                if (GCHandle.FromIntPtr(userData).Target is not PulsePlaybackStream self || self._closed)
                {
                    return;
                }

                if (self._drain != IntPtr.Zero)
                {
                    PulseNative.OperationUnref(self._drain);
                    self._drain = IntPtr.Zero;
                }

                if (success == 0)
                {
                    self._feed.Drained(false);
                    return;
                }

                self.AwaitPlayedOut(stream);
            }
            catch (Exception)
            {
                // The track then ends with the hold's own deadline.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnStopTiming(IntPtr stream, int success, IntPtr userData)
        {
            TaskCompletionSource<bool>? placed = null;
            try
            {
                if (GCHandle.FromIntPtr(userData).Target is not PulsePlaybackStream self || self._closed || self._placing == IntPtr.Zero)
                {
                    return;
                }

                PulseNative.OperationUnref(self._placing);
                self._placing = IntPtr.Zero;
                placed = self._placed;
                self._placed = null;
                self.PlaceAfterStop(stream, success != 0);
            }
            catch (Exception)
            {
                // The stream keeps what it holds, as when the server gives no timing.
            }
            finally
            {
                // The stop waiting for this goes on.
                placed?.TrySetResult(true);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnTiming(IntPtr stream, int success, IntPtr userData)
        {
            try
            {
                if (GCHandle.FromIntPtr(userData).Target is not PulsePlaybackStream self || self._closed || self._timing == IntPtr.Zero)
                {
                    return;
                }

                PulseNative.OperationUnref(self._timing);
                self._timing = IntPtr.Zero;

                // From the last sample written to the speaker: after the drain, what the sink still holds. Unknown: ends now.
                ulong left = 0;
                int negative = 0;
                if (success == 0 || PulseNative.StreamGetLatency(stream, &left, &negative) != 0 || negative != 0)
                {
                    left = 0;
                }

                self.EndAfter(left);
            }
            catch (Exception)
            {
                // The track then ends with the hold's own deadline.
            }
        }
    }
}
