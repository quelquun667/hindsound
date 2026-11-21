using Hindsound.Audio;
using Hindsound.Library;

namespace Hindsound.Unix.Library;

/// <summary>
/// One open file for the app: the frames of an <see cref="IClipDecoderSource"/>, mixed down to stereo if it has more than two
/// channels, then resampled to <see cref="InternalFormat.SampleRate"/> if it is at another rate. Never holds more than one
/// chunk of the file in memory.
/// </summary>
internal sealed class ClipDecoderStream : IDecodedStream
{
    /// <summary>Native frames mixed and resampled at a time.</summary>
    private const int ChunkFrames = 4096;

    /// <summary>Zero frames fed at a time at the end, to bring out what the resampler still holds.</summary>
    private const int FlushFrames = 64;

    private readonly IClipDecoderSource _source;
    private readonly int _nativeChannels;
    private readonly int _nativeRate;
    private readonly float[] _native;
    private readonly float[] _mixed;
    private readonly float[] _resampled;

    /// <summary>
    /// When resampling, a seek starts this many native frames before its target, so the filter has the real frames behind it
    /// rather than silence; the <see cref="_prerollOut"/> frames made from them are dropped. A whole number of periods of
    /// both rates, so the first frame kept falls exactly on the target.
    /// </summary>
    private readonly int _preroll, _prerollOut;

    private Resampler? _resampler;
    private float[] _ready = new float[ChunkFrames * 2];
    private int _readyAt, _readyCount;
    private bool _ended, _disposed;

    /// <summary>Frames still to drop at the start of what the resampler makes.</summary>
    private int _dropOut;

    /// <summary>Native frames handed on, and frames made from them, since the start or the last seek.</summary>
    private long _framesIn, _framesOut;

    public ClipDecoderStream(IClipDecoderSource source)
    {
        _source = source;
        _nativeChannels = source.Channels;
        _nativeRate = source.Rate;
        Channels = _nativeChannels == 1 ? 1 : 2;
        Duration = TimeSpan.FromTicks(source.Frames * TimeSpan.TicksPerSecond / _nativeRate);
        _native = new float[ChunkFrames * _nativeChannels];
        _mixed = new float[ChunkFrames * Channels];
        _resampler = NewResampler();
        _resampled = _resampler is null ? [] : new float[_resampler.MaxOutputFrames * Channels];
        if (_resampler is not null)
        {
            var period = _nativeRate / (int)GreatestCommonDivisor(_nativeRate, InternalFormat.SampleRate);
            _preroll = period * (int)Math.Ceiling(64.0 / period);
            _prerollOut = (int)((long)_preroll * InternalFormat.SampleRate / _nativeRate);
        }

        source.Seek(0);
    }

    public int Channels { get; }

    public TimeSpan Duration { get; }

    private static long GreatestCommonDivisor(long a, long b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);

    private Resampler? NewResampler() =>
        _nativeRate == InternalFormat.SampleRate ? null : new Resampler(_nativeRate, InternalFormat.SampleRate, Channels, ChunkFrames);

    public void Seek(TimeSpan at)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = (long)Math.Round(Math.Clamp(at.TotalSeconds, 0, 1e9) * _nativeRate);
        var from = target;
        _dropOut = 0;
        if (_resampler is not null && target - _preroll >= 0)
        {
            from = target - _preroll;
            _dropOut = _prerollOut;
        }

        _readyAt = _readyCount = 0;
        _framesIn = _framesOut = 0;
        _resampler = NewResampler();

        // Past the end of the file there is nothing to read, which is not an error.
        _ended = from >= _source.Frames;
        if (!_ended) _source.Seek(from);
    }

    public int Read(Span<float> interleaved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wanted = interleaved.Length / Channels * Channels;
        var written = 0;
        while (written < wanted)
        {
            if (_readyCount == 0)
            {
                if (_ended) break;
                Fill();
                continue;
            }

            var count = Math.Min(_readyCount, wanted - written);
            _ready.AsSpan(_readyAt, count).CopyTo(interleaved[written..]);
            _readyAt += count;
            _readyCount -= count;
            written += count;
        }

        return written;
    }

    // Reads the next chunk of the file into the ready floats. Called only when they are all read.
    private void Fill()
    {
        _readyAt = 0;
        var frames = _source.Read(_native) / _nativeChannels;
        if (frames == 0)
        {
            Flush();
            _ended = true;
            return;
        }

        Push(_native.AsSpan(0, frames * _nativeChannels));
    }

    // Mixes, resamples and queues native frames.
    private void Push(ReadOnlySpan<float> native)
    {
        var frames = native.Length / _nativeChannels;
        var mixed = _mixed.AsSpan(0, frames * Channels);
        MixDown(native, _nativeChannels, mixed);
        _framesIn += frames;
        Resample(mixed);
    }

    private void Resample(ReadOnlySpan<float> mixed)
    {
        if (_resampler is null)
        {
            Append(mixed);
            _framesOut += mixed.Length / Channels;
            return;
        }

        var produced = _resampler.Process(mixed, _resampled);
        _framesOut += produced;
        var dropped = Math.Min(_dropOut, produced);
        _dropOut -= dropped;
        Append(_resampled.AsSpan(dropped * Channels, (produced - dropped) * Channels));
    }

    // At the end, the resampler still holds the last frames: zeros push them out, and what goes past the length of the file at
    // the new rate is cut. The frames dropped after a seek are counted on both sides, so the cut stays right.
    private void Flush()
    {
        if (_resampler is null) return;
        var expected = (_framesIn * InternalFormat.SampleRate + _nativeRate - 1) / _nativeRate;
        Span<float> zeros = stackalloc float[FlushFrames * 2];
        zeros = zeros[..(FlushFrames * Channels)];
        zeros.Clear();
        for (var round = 0; _framesOut < expected && round < 1024; round++) Resample(zeros);

        var extra = (int)Math.Min(_framesOut - expected, _readyCount / Channels);
        if (extra <= 0) return;
        _readyCount -= extra * Channels;
        _framesOut -= extra;
    }

    private void Append(ReadOnlySpan<float> floats)
    {
        var end = _readyAt + _readyCount;
        if (end + floats.Length > _ready.Length)
        {
            var grown = new float[Math.Max(_ready.Length * 2, end + floats.Length)];
            _ready.AsSpan(0, end).CopyTo(grown);
            _ready = grown;
        }

        floats.CopyTo(_ready.AsSpan(end));
        _readyCount += floats.Length;
    }

    /// <summary>
    /// One channel stays one; two stay two; more become two: the first two are the left and the right, every other one goes
    /// to both at half its level, and the sum is scaled so that full scale everywhere stays full scale.
    /// </summary>
    internal static void MixDown(ReadOnlySpan<float> native, int nativeChannels, Span<float> output)
    {
        if (nativeChannels <= 2)
        {
            native.CopyTo(output);
            return;
        }

        var scale = 1f / (1f + 0.5f * (nativeChannels - 2));
        var frames = native.Length / nativeChannels;
        for (var f = 0; f < frames; f++)
        {
            var frame = native.Slice(f * nativeChannels, nativeChannels);
            var shared = 0f;
            for (var c = 2; c < nativeChannels; c++) shared += frame[c];
            shared *= 0.5f;
            output[2 * f] = (frame[0] + shared) * scale;
            output[2 * f + 1] = (frame[1] + shared) * scale;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.Dispose();
    }
}
