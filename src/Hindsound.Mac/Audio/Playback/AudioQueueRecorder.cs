using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Hindsound.Mac.Audio.Playback.AudioQueueNative;

namespace Hindsound.Mac.Audio.Playback;

/// <summary>
/// The ear of <c>--prove-preview</c>: an AudioQueue on the default input (BlackHole 2ch in the CI, a loopback device, so what
/// the default output plays comes back on it), which keeps every frame it hears, 48 kHz stereo floats. Not a part of the app:
/// the capture of the microphone is the capture's own.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class AudioQueueRecorder : IDisposable
{
    public const int Rate = 48000;
    public const int Channels = 2;

    /// <summary>About 21 ms a buffer: the frame count read at a moment is never far behind it.</summary>
    private const int BufferFrames = 1024;

    private const int BufferCount = 4;

    private readonly object _gate = new();
    private readonly List<float> _heard = new(Rate * Channels * 30);
    private GCHandle _self;
    private IntPtr _queue;
    private bool _stopping;
    private int _busy;

    private AudioQueueRecorder()
    {
    }

    /// <summary>Frames heard so far.</summary>
    public long Frames
    {
        get
        {
            lock (_gate)
            {
                return _heard.Count / Channels;
            }
        }
    }

    /// <summary>Opens the default input and starts it. Throws an <see cref="IOException"/> with the status if Core Audio refuses.</summary>
    public static AudioQueueRecorder Start()
    {
        var recorder = new AudioQueueRecorder();
        try
        {
            recorder.Open();
        }
        catch
        {
            recorder.Dispose();
            throw;
        }

        return recorder;
    }

    /// <summary>Every frame heard so far, interleaved.</summary>
    public float[] Heard()
    {
        lock (_gate)
        {
            return [.. _heard];
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            while (_busy > 0)
            {
                Monitor.Wait(_gate);
            }
        }

        var queue = _queue;
        _queue = IntPtr.Zero;
        if (queue != IntPtr.Zero)
        {
            _ = AudioQueueStop(queue, 1);
            _ = AudioQueueDispose(queue, 1);
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private void Open()
    {
        _self = GCHandle.Alloc(this);
        var format = AudioStreamBasicDescription.Float(Rate, Channels);
        IntPtr queue;
        Check(AudioQueueNewInput(&format, &OnInput, GCHandle.ToIntPtr(_self), IntPtr.Zero, IntPtr.Zero, 0, &queue), "AudioQueueNewInput");
        _queue = queue;
        for (var i = 0; i < BufferCount; i++)
        {
            AudioQueueBuffer* buffer;
            Check(AudioQueueAllocateBuffer(queue, BufferFrames * Channels * sizeof(float), &buffer), "AudioQueueAllocateBuffer");
            Check(AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero), "AudioQueueEnqueueBuffer");
        }

        Check(AudioQueueStart(queue, IntPtr.Zero), "AudioQueueStart");
    }

    private void Received(AudioQueueBuffer* buffer)
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _busy++;
            _heard.AddRange(new ReadOnlySpan<float>(buffer->AudioData, (int)(buffer->AudioDataByteSize / sizeof(float))));
        }

        try
        {
            _ = AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero);
        }
        finally
        {
            lock (_gate)
            {
                _busy--;
                Monitor.PulseAll(_gate);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void OnInput(IntPtr user, IntPtr queue, AudioQueueBuffer* buffer, IntPtr time, uint packets, IntPtr descriptions)
    {
        try
        {
            (GCHandle.FromIntPtr(user).Target as AudioQueueRecorder)?.Received(buffer);
        }
        catch (Exception)
        {
            // Called by Core Audio: nothing may be thrown out of it.
        }
    }
}
