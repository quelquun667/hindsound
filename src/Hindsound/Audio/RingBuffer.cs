using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hindsound.Audio;

/// <summary>
/// A circular buffer addressed by absolute position, meant to be written from the audio thread. Callers deal in
/// floats; the samples are held as 16-bit integers to take half the memory (see <see cref="SampleCodec"/>).
/// <para>
/// One writer at a time, and it never waits for a reader: reading takes no lock at all. A reader
/// copies first and checks afterwards. The writer announces how far it is about to overwrite before
/// touching the memory, so a reader can tell which of the samples it copied may have been replaced
/// meanwhile, and discards exactly those. Holding a little more than callers ever ask for makes that
/// case rare.
/// </para>
/// </summary>
public sealed class RingBuffer
{
    private readonly short[] _data;
    private long _firstPosition;

    // Published after the samples are in place: every position below it can be read.
    private long _position;

    // Published before the samples are written: slots of positions below (_overwriteLimit - capacity) may be gone.
    private long _overwriteLimit;

    /// <param name="capacity">Number of samples kept.</param>
    /// <param name="startPosition">Absolute position of the first sample that will be written.</param>
    public RingBuffer(int capacity, long startPosition = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(startPosition);
        _data = new short[capacity];
        _firstPosition = startPosition;
        _position = startPosition;
        _overwriteLimit = startPosition;
    }

    public int Capacity => _data.Length;

    /// <summary>Memory held by the samples.</summary>
    public long MemoryBytes => (long)_data.Length * sizeof(short);

    /// <summary>Absolute position just after the newest sample.</summary>
    public long Position => Volatile.Read(ref _position);

    /// <summary>Absolute position of the oldest sample still held.</summary>
    public long OldestPosition => Math.Max(_firstPosition, Position - _data.Length);

    /// <summary>
    /// Forgets everything and starts again at <paramref name="position"/>. Only for a buffer no reader can see yet.
    /// </summary>
    internal void RestartAt(long position)
    {
        _firstPosition = position;
        _position = position;
        _overwriteLimit = position;
    }

    /// <summary>Appends samples. Allocates nothing. If there are more than the capacity, only the last ones are kept.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        var end = AnnounceWrite(samples.Length);
        if (samples.Length > _data.Length)
        {
            samples = samples[^_data.Length..];
        }

        var slot = (int)((end - samples.Length) % _data.Length);
        var firstPart = Math.Min(samples.Length, _data.Length - slot);
        SampleCodec.Encode(samples[..firstPart], _data.AsSpan(slot));
        SampleCodec.Encode(samples[firstPart..], _data);
        Volatile.Write(ref _position, end);
    }

    /// <summary>Appends zeros. Allocates nothing, and costs no more than the capacity however long the silence is.</summary>
    public void WriteSilence(long count)
    {
        if (count <= 0)
        {
            return;
        }

        var end = AnnounceWrite(count);
        var length = (int)Math.Min(count, _data.Length);
        var slot = (int)((end - length) % _data.Length);
        var firstPart = Math.Min(length, _data.Length - slot);
        Array.Clear(_data, slot, firstPart);
        Array.Clear(_data, 0, length - firstPart);
        Volatile.Write(ref _position, end);
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the samples from <paramref name="position"/> on, without blocking
    /// the writer. Anything not held, because it is too old, was overwritten during the copy or is not written yet,
    /// comes out as zeros.
    /// </summary>
    /// <returns>The number of real samples delivered.</returns>
    public int CopyTo(long position, Span<float> destination) => CopyTo(position, destination, out _);

    /// <inheritdoc cref="CopyTo(long, Span{float})"/>
    /// <param name="position">Absolute position of the first sample wanted.</param>
    /// <param name="destination">Where the samples go.</param>
    /// <param name="firstHeld">
    /// Where what was held begins, within the range asked for: anything before it is gone, or was never written.
    /// </param>
    public int CopyTo(long position, Span<float> destination, out long firstHeld)
    {
        // One reading of the position: the writer can move on by more than the capacity between two of them.
        var written = Position;
        var requestedEnd = position + destination.Length;
        var start = Math.Max(position, Math.Max(_firstPosition, written - _data.Length));
        var end = Math.Min(requestedEnd, written);
        if (start >= end)
        {
            destination.Clear();
            firstHeld = Math.Min(start, requestedEnd);
            return 0;
        }

        var offset = (int)(start - position);
        var length = (int)(end - start);
        var slot = (int)(start % _data.Length);
        var firstPart = Math.Min(length, _data.Length - slot);
        SampleCodec.Decode(_data.AsSpan(slot, firstPart), destination[offset..]);
        SampleCodec.Decode(_data.AsSpan(0, length - firstPart), destination[(offset + firstPart)..]);

        destination[..offset].Clear();
        destination[(offset + length)..].Clear();

        // The barrier keeps this read after the copy above, so the limit cannot look older than what was copied.
        Thread.MemoryBarrier();
        var overwrittenUpTo = Volatile.Read(ref _overwriteLimit) - _data.Length;
        if (overwrittenUpTo <= start)
        {
            firstHeld = start;
            return length;
        }

        var lost = (int)Math.Min(overwrittenUpTo - start, length);
        destination.Slice(offset, lost).Clear();
        firstHeld = Math.Min(overwrittenUpTo, requestedEnd);
        return length - lost;
    }

    /// <summary>The largest absolute value among <paramref name="count"/> samples from <paramref name="position"/> on, without blocking the writer.</summary>
    public int PeakOf(long position, int count) => PeakOf(position, count, out _);

    /// <summary>
    /// The same bounds and the same check as <see cref="CopyTo(long, Span{float}, out long)"/>, on the stored values:
    /// nothing is decoded, which makes a full reading of the buffer 13 times faster. Allocates nothing.
    /// </summary>
    /// <param name="position">Absolute position of the first sample wanted.</param>
    /// <param name="count">How many samples are wanted.</param>
    /// <param name="firstHeld">
    /// Where what was held begins, within the range asked for: anything before it is gone, or was never written.
    /// </param>
    /// <returns>
    /// The peak in stored units, 0 to 32768, or 0 when nothing is held. Also 0 when the writer overtook the read:
    /// a replaced sample may be the peak and cannot be taken back out, so <paramref name="firstHeld"/> is the only answer.
    /// </returns>
    public int PeakOf(long position, int count, out long firstHeld)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        // One reading of the position: the writer can move on by more than the capacity between two of them.
        var written = Position;
        var requestedEnd = position + count;
        var start = Math.Max(position, Math.Max(_firstPosition, written - _data.Length));
        var end = Math.Min(requestedEnd, written);
        if (start >= end)
        {
            firstHeld = Math.Min(start, requestedEnd);
            return 0;
        }

        var length = (int)(end - start);
        var slot = (int)(start % _data.Length);
        var firstPart = Math.Min(length, _data.Length - slot);
        var peak = Math.Max(PeakOfStored(_data.AsSpan(slot, firstPart)), PeakOfStored(_data.AsSpan(0, length - firstPart)));

        // The barrier keeps this read after the reading above, so the limit cannot look older than what was read.
        Thread.MemoryBarrier();
        var overwrittenUpTo = Volatile.Read(ref _overwriteLimit) - _data.Length;
        if (overwrittenUpTo <= start)
        {
            firstHeld = start;
            return peak;
        }

        firstHeld = Math.Min(overwrittenUpTo, requestedEnd);
        return 0;
    }

    // The two extremes and not the absolute values: the absolute value of -32768 does not fit in 16 bits.
    // Optimized from the first call: code on Vector<T> is left out of a ReadyToRun build, and starting at the JIT's first
    // tier made the opening pass 6 times slower (61 ms against 8), and that is the pass the wave is waited for.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int PeakOfStored(ReadOnlySpan<short> samples)
    {
        var vectors = MemoryMarshal.Cast<short, Vector<short>>(samples);
        var top = Vector<short>.Zero;
        var bottom = Vector<short>.Zero;
        foreach (ref readonly var vector in vectors)
        {
            top = Vector.Max(top, vector);
            bottom = Vector.Min(bottom, vector);
        }

        var highest = 0;
        var lowest = 0;
        for (var i = 0; i < Vector<short>.Count; i++)
        {
            highest = Math.Max(highest, top[i]);
            lowest = Math.Min(lowest, bottom[i]);
        }

        foreach (var sample in samples[(vectors.Length * Vector<short>.Count)..])
        {
            highest = Math.Max(highest, sample);
            lowest = Math.Min(lowest, sample);
        }

        return Math.Max(highest, -lowest);
    }

    /// <summary>Copies the last <paramref name="count"/> samples, or fewer if fewer were written.</summary>
    public float[] Snapshot(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var end = Position;
        // Worked out from that one position. OldestPosition reads a newer one, which can lie beyond this end.
        var start = Math.Max(Math.Max(_firstPosition, end - _data.Length), end - count);
        var result = new float[(int)(end - start)];
        CopyTo(start, result);
        return result;
    }

    private long AnnounceWrite(long count)
    {
        var end = _position + count;
        Volatile.Write(ref _overwriteLimit, end);

        // The barrier keeps the sample writes that follow from becoming visible before the limit.
        Thread.MemoryBarrier();
        return end;
    }
}
