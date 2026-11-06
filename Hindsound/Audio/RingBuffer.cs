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
    private readonly long _firstPosition;

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
    public int CopyTo(long position, Span<float> destination)
    {
        var requestedEnd = position + destination.Length;
        var start = Math.Max(position, OldestPosition);
        var end = Math.Min(requestedEnd, Position);
        if (start >= end)
        {
            destination.Clear();
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
            return length;
        }

        var lost = (int)Math.Min(overwrittenUpTo - start, length);
        destination.Slice(offset, lost).Clear();
        return length - lost;
    }

    /// <summary>Copies the last <paramref name="count"/> samples, or fewer if fewer were written.</summary>
    public float[] Snapshot(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var end = Position;
        var start = Math.Max(OldestPosition, end - count);
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
