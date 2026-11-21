using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using static Hindsound.Mac.Audio.Playback.AudioQueueNative;

namespace Hindsound.Mac.Audio.Playback;

/// <summary>
/// The output of the preview on macOS: one AudioQueue per track, on the default output, which it follows when it changes.
/// The queue runs inside the app's process: the tap of the PC's sound leaves the app out, so the clip being listened to never
/// ends up in the next clip, with nothing to do here.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class AudioQueueOutput : IMacPlaybackOutput
{
    public IMacPlaybackStream Open(int channels, IMacPlaybackFeed feed) => AudioQueueStream.Open(channels, feed);
}

/// <summary>
/// One AudioQueue: a few buffers filled from the feed on the queue's own thread, started, paused (it keeps what it held, so
/// the next start goes on from what was heard), stopped when the file ends (it plays out what it holds, then says it drained),
/// and disposed. Its state is under one lock, never held while Core Audio or the feed is called; a callback that runs counts
/// itself in, and <see cref="Close"/> waits for it to leave before it disposes the queue.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class AudioQueueStream : IMacPlaybackStream
{
    /// <summary>About 43 ms a buffer at 48 kHz: the sound starts quickly, and three of them ride over a late callback.</summary>
    internal const int BufferFrames = 2048;

    internal const int BufferCount = 3;

    private enum Refilled
    {
        Queued,
        End,
        Failed,
    }

    private readonly object _gate = new();
    private readonly int _channels;
    private readonly IMacPlaybackFeed _feed;
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private GCHandle _self;
    private IntPtr _queue;
    private bool _primed;
    private bool _paused = true;
    private bool _endReached;
    private bool _stopAsked;
    private bool _drained;
    private bool _failed;
    private bool _closing;
    private int _busy;

    private AudioQueueStream(int channels, IMacPlaybackFeed feed)
    {
        _channels = channels;
        _feed = feed;
    }

    /// <summary>Makes the queue and its buffers, stopped. Throws an <see cref="IOException"/> with the status if Core Audio refuses.</summary>
    public static AudioQueueStream Open(int channels, IMacPlaybackFeed feed)
    {
        if (channels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "1 or 2 channels");
        }

        var stream = new AudioQueueStream(channels, feed);
        try
        {
            stream.Create();
        }
        catch
        {
            stream.Release();
            throw;
        }

        return stream;
    }

    public void Start()
    {
        bool drainedAlready;
        bool prime;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            drainedAlready = _drained;
            prime = !_primed;
            _primed = true;
            _paused = false;
        }

        if (drainedAlready)
        {
            _feed.Drained(true);
            return;
        }

        if (prime)
        {
            var queued = 0;
            foreach (var buffer in _buffers)
            {
                var refilled = Refill((AudioQueueBuffer*)buffer);
                if (refilled == Refilled.Failed)
                {
                    lock (_gate)
                    {
                        _failed = true;
                    }

                    throw new IOException("the file failed before it played");
                }

                if (refilled == Refilled.End)
                {
                    lock (_gate)
                    {
                        _endReached = true;
                    }

                    break;
                }

                queued++;
            }

            if (queued == 0)
            {
                // An empty file: played at once.
                lock (_gate)
                {
                    _drained = true;
                }

                _feed.Drained(true);
                return;
            }
        }

        Check(AudioQueueStart(_queue, IntPtr.Zero), "AudioQueueStart");

        // A file shorter than the buffers ended while they were filled: the queue plays them out, then stops.
        bool stopNow;
        lock (_gate)
        {
            stopNow = _endReached && !_stopAsked;
            _stopAsked |= stopNow;
        }

        if (stopNow)
        {
            Check(AudioQueueStop(_queue, 0), "AudioQueueStop");
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _paused = true;
        }

        Check(AudioQueuePause(_queue), "AudioQueuePause");
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            while (_busy > 0)
            {
                Monitor.Wait(_gate);
            }
        }

        Release();
    }

    private void Create()
    {
        _self = GCHandle.Alloc(this);
        var user = GCHandle.ToIntPtr(_self);
        var format = AudioStreamBasicDescription.Float(InternalFormat.SampleRate, _channels);
        IntPtr queue;
        Check(AudioQueueNewOutput(&format, &OnBuffer, user, IntPtr.Zero, IntPtr.Zero, 0, &queue), "AudioQueueNewOutput");
        _queue = queue;
        for (var i = 0; i < BufferCount; i++)
        {
            AudioQueueBuffer* buffer;
            Check(AudioQueueAllocateBuffer(queue, (uint)(BufferFrames * _channels * sizeof(float)), &buffer), "AudioQueueAllocateBuffer");
            _buffers[i] = (IntPtr)buffer;
        }

        Check(AudioQueueAddPropertyListener(queue, IsRunningProperty, &OnRunning, user), "AudioQueueAddPropertyListener");
    }

    // Disposing the queue frees its buffers; once it has returned, no callback comes.
    private void Release()
    {
        var queue = _queue;
        _queue = IntPtr.Zero;
        if (queue != IntPtr.Zero)
        {
            _ = AudioQueueDispose(queue, 1);
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private Refilled Refill(AudioQueueBuffer* buffer)
    {
        var floats = BufferFrames * _channels;
        var written = _feed.Fill(new Span<float>(buffer->AudioData, floats));
        if (written < 0)
        {
            return Refilled.Failed;
        }

        if (written == 0)
        {
            return Refilled.End;
        }

        buffer->AudioDataByteSize = (uint)(written * sizeof(float));
        var status = AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero);
        if (status != 0)
        {
            _feed.Failed("the output took no more sound: status " + FourCharacters(status));
            return Refilled.Failed;
        }

        return Refilled.Queued;
    }

    private bool Enter()
    {
        lock (_gate)
        {
            if (_closing)
            {
                return false;
            }

            _busy++;
            return true;
        }
    }

    private void Leave()
    {
        lock (_gate)
        {
            _busy--;
            Monitor.PulseAll(_gate);
        }
    }

    // On the queue's thread: a buffer has played and comes back to be filled again.
    private void BufferFree(AudioQueueBuffer* buffer)
    {
        if (!Enter())
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (_failed || _endReached)
                {
                    return;
                }
            }

            var refilled = Refill(buffer);
            var stop = false;
            lock (_gate)
            {
                if (refilled == Refilled.End)
                {
                    _endReached = true;
                    stop = !_stopAsked;
                    _stopAsked = true;
                }
                else if (refilled == Refilled.Failed)
                {
                    _failed = true;
                    stop = true;
                }
            }

            // Not at once: the queue plays out what it holds, then says it is no longer running.
            if (stop)
            {
                _ = AudioQueueStop(_queue, 0);
            }
        }
        finally
        {
            Leave();
        }
    }

    // On the queue's thread: the queue started or stopped.
    private void RunningChanged()
    {
        if (!Enter())
        {
            return;
        }

        try
        {
            uint running = 1;
            uint size = sizeof(uint);
            if (AudioQueueGetProperty(_queue, IsRunningProperty, &running, &size) != 0 || running != 0)
            {
                return;
            }

            var drained = false;
            var lost = false;
            lock (_gate)
            {
                if (_failed || _drained)
                {
                    return;
                }

                if (_stopAsked)
                {
                    _drained = drained = true;
                }
                else if (!_paused)
                {
                    _failed = lost = true;
                }
            }

            if (drained)
            {
                _feed.Drained(true);
            }
            else if (lost)
            {
                _feed.Failed("the output stopped by itself");
            }
        }
        finally
        {
            Leave();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnBuffer(IntPtr user, IntPtr queue, AudioQueueBuffer* buffer)
    {
        try
        {
            (GCHandle.FromIntPtr(user).Target as AudioQueueStream)?.BufferFree(buffer);
        }
        catch (Exception)
        {
            // Called by Core Audio: nothing may be thrown out of it.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnRunning(IntPtr user, IntPtr queue, uint property)
    {
        try
        {
            (GCHandle.FromIntPtr(user).Target as AudioQueueStream)?.RunningChanged();
        }
        catch (Exception)
        {
            // Called by Core Audio: nothing may be thrown out of it.
        }
    }
}
