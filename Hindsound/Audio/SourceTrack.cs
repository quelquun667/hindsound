namespace Hindsound.Audio;

/// <summary>
/// One source's past audio laid out on the shared timeline: a <see cref="RingBuffer"/> plus where it starts.
/// Writers take a short lock so that an old and a new capture can overlap during a device change. Readers take
/// no lock, so a snapshot never holds up the audio thread.
/// </summary>
public sealed class SourceTrack
{
    /// <summary>
    /// Kept on top of the requested length: reading the oldest second during a full-length snapshot stays out of
    /// the writer's way, and two sources ending a few frames apart can both serve the whole window.
    /// </summary>
    public const int SlackFrames = InternalFormat.SampleRate;

    private readonly Lock _writeGate = new();
    private readonly long _originFrame;
    private RingBuffer _ring;

    /// <param name="channels">Samples per frame.</param>
    /// <param name="capacityFrames">Frames a caller can ask back.</param>
    /// <param name="originFrame">Timeline frame this track starts at. Before its first packet, the time since is silence.</param>
    public SourceTrack(int channels, int capacityFrames, long originFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        Channels = channels;
        _originFrame = originFrame;
        _ring = new RingBuffer(checked((capacityFrames + SlackFrames) * channels));
    }

    public int Channels { get; }

    /// <summary>Memory held by the samples.</summary>
    public long MemoryBytes => Volatile.Read(ref _ring).MemoryBytes;

    /// <summary>Timeline frame just after the last frame written.</summary>
    public long EndFrame => _originFrame + Volatile.Read(ref _ring).Position / Channels;

    /// <summary>Timeline frame of the oldest frame still held.</summary>
    public long OldestFrame => _originFrame + Volatile.Read(ref _ring).OldestPosition / Channels;

    /// <summary>Places a packet of whole frames at <paramref name="startFrame"/>. Allocates nothing.</summary>
    public void Append(long startFrame, ReadOnlySpan<float> samples, bool exact)
    {
        lock (_writeGate)
        {
            var ring = _ring;
            var frames = samples.Length / Channels;
            var writeEnd = _originFrame + ring.Position / Channels;
            var placement = SilencePadder.Place(writeEnd, startFrame, frames, exact);

            ring.WriteSilence(placement.PadFrames * Channels);
            var kept = samples[(placement.SkipFrames * Channels)..];
            ring.Write(kept);
            if (placement.RepeatLastFrame)
            {
                ring.Write(kept[^Channels..]);
            }
        }
    }

    /// <summary>Writes frames straight after the previous ones. For the rest of a packet too large for one conversion.</summary>
    public void AppendContinuation(ReadOnlySpan<float> samples)
    {
        lock (_writeGate)
        {
            _ring.Write(samples);
        }
    }

    /// <summary>Writes zeros up to <paramref name="frame"/>. Does nothing if the track already reaches it.</summary>
    public void PadTo(long frame)
    {
        lock (_writeGate)
        {
            var ring = _ring;
            ring.WriteSilence((frame - (_originFrame + ring.Position / Channels)) * Channels);
        }
    }

    /// <summary>Copies frames from <paramref name="startFrame"/> on, without taking any lock. Anything not held comes out as zeros.</summary>
    /// <returns>The number of real samples delivered.</returns>
    public int CopyFrames(long startFrame, Span<float> destination)
    {
        return Volatile.Read(ref _ring).CopyTo((startFrame - _originFrame) * Channels, destination);
    }

    /// <summary>Copies a window like <see cref="CopyFrames"/>, and says what it could not deliver.</summary>
    /// <returns>
    /// How many frames at the start of the window were audio once and are gone: overtaken by the writer, or dropped
    /// by a resize. Frames from before this track began were never audio, and do not count.
    /// </returns>
    public int CopyWindow(long startFrame, Span<float> destination)
    {
        var position = (startFrame - _originFrame) * Channels;
        Volatile.Read(ref _ring).CopyTo(position, destination, out var firstHeld);
        return (int)(Math.Max(0, firstHeld - Math.Max(0, position)) / Channels);
    }

    /// <summary>
    /// Changes how much is kept, keeping the newest audio. The copy runs outside the lock and only the few frames
    /// written during it are moved under the lock, so the audio thread is not held up.
    /// </summary>
    public void Resize(int capacityFrames)
    {
        var capacity = checked((capacityFrames + SlackFrames) * Channels);
        var old = Volatile.Read(ref _ring);
        if (capacity == old.Capacity)
        {
            return;
        }

        var end = old.Position;
        var start = Math.Max(old.OldestPosition, end - capacity);
        var next = new RingBuffer(capacity, start);
        var chunk = new float[SlackFrames * Channels];
        Transfer(old, next, end, chunk);

        lock (_writeGate)
        {
            Transfer(old, next, old.Position, chunk);
            Volatile.Write(ref _ring, next);
        }
    }

    private static void Transfer(RingBuffer from, RingBuffer to, long end, float[] chunk)
    {
        while (to.Position < end)
        {
            var slice = chunk.AsSpan(0, (int)Math.Min(chunk.Length, end - to.Position));
            from.CopyTo(to.Position, slice);
            to.Write(slice);
        }
    }
}
