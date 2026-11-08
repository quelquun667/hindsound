using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Hindsound.Tests")]

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

    /// <summary>From <see cref="PeakOfFrames"/>: part of the range was audio once and is gone, so no peak can be told.</summary>
    public const int PeakGone = -1;

    // A clock correction is spread over this many frames, 10 ms. Done in one frame, on a 1 kHz tone at -6 dBFS, it is a
    // click at -33.2 dBFS above 8 kHz, 1.4 times a second at 30 ppm. Spread out, it measures -84.9 dBFS.
    private const int FadeFrames = 480;

    private readonly Lock _writeGate = new();
    private readonly long _originFrame;
    private readonly float[] _fade;
    private RingBuffer _ring;

    /// <param name="channels">Samples per frame.</param>
    /// <param name="capacityFrames">Frames a caller can ask back.</param>
    /// <param name="originFrame">Timeline frame this track starts at. Before its first packet, the time since is silence.</param>
    public SourceTrack(int channels, int capacityFrames, long originFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        Channels = channels;
        _originFrame = originFrame;
        _fade = new float[(FadeFrames + 1) * channels];
        _ring = new RingBuffer(checked((capacityFrames + SlackFrames) * channels));

    }

    public int Channels { get; }

    /// <summary>What writers wait on. For a test that proves readers do not.</summary>
    internal Lock WriteGate => _writeGate;

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
            if (placement.RepeatLastFrame)
            {
                WriteOneFrameLonger(ring, samples);
            }
            else if (placement.SkipFrames == 1)
            {
                WriteOneFrameShorter(ring, samples);
            }
            else
            {
                ring.Write(samples[(placement.SkipFrames * Channels)..]);
            }
        }
    }

    // Slides from each frame to the one before it over the fade, so the packet ends one frame later with no jump.
    private void WriteOneFrameLonger(RingBuffer ring, ReadOnlySpan<float> samples)
    {
        var channels = Channels;
        var fade = Math.Min(samples.Length / channels, FadeFrames);
        for (var frame = 0; frame < fade; frame++)
        {
            var weight = (float)frame / fade;
            var previous = Math.Max(frame - 1, 0) * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                var current = samples[frame * channels + channel];
                _fade[frame * channels + channel] = current + (samples[previous + channel] - current) * weight;
            }
        }

        samples.Slice((fade - 1) * channels, channels).CopyTo(_fade.AsSpan(fade * channels));
        ring.Write(_fade.AsSpan(0, (fade + 1) * channels));
        ring.Write(samples[(fade * channels)..]);
    }

    // Slides from each frame to the one after it over the fade, so the packet ends one frame earlier with no jump.
    private void WriteOneFrameShorter(RingBuffer ring, ReadOnlySpan<float> samples)
    {
        var channels = Channels;
        var fade = Math.Min(samples.Length / channels - 1, FadeFrames);
        for (var frame = 0; frame < fade; frame++)
        {
            var weight = (float)(frame + 1) / fade;
            for (var channel = 0; channel < channels; channel++)
            {
                var current = samples[frame * channels + channel];
                _fade[frame * channels + channel] = current + (samples[(frame + 1) * channels + channel] - current) * weight;
            }
        }

        ring.Write(_fade.AsSpan(0, fade * channels));
        ring.Write(samples[((fade + 1) * channels)..]);
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
    /// The peak of a range of frames, every channel together, without taking any lock. Allocates nothing.
    /// Frames from before this track began or not written yet count for nothing.
    /// </summary>
    /// <returns>The peak in stored units, 0 to 32768, or <see cref="PeakGone"/>.</returns>
    public int PeakOfFrames(long startFrame, int frames)
    {
        var position = (startFrame - _originFrame) * Channels;
        var peak = Volatile.Read(ref _ring).PeakOf(position, frames * Channels, out var firstHeld);
        return firstHeld > Math.Max(0, position) ? PeakGone : peak;
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
            from.CopyTo(to.Position, slice, out var firstHeld);

            // The writer got there first, which it does at once on a full buffer: that audio is gone, not silent.
            if (firstHeld > to.Position)
            {
                to.RestartAt(firstHeld);
                continue;
            }

            to.Write(slice);
        }
    }
}
