using System.Buffers.Binary;
using System.IO;
using Hindsound.Library;
using static Hindsound.Audio.MediaFoundationNative;

namespace Hindsound.Audio;

/// <summary>
/// Reads an mp3 or a wav back to float samples through Media Foundation (IMFSourceReader). Media Foundation objects are
/// free-threaded: open and read on a worker thread (the thread pool is MTA), never on the window's loop.
/// </summary>
internal sealed class MediaFoundationClipDecoder : IClipDecoder
{
    private static readonly Lock StartLock = new();
    private static bool _started;

    public IDecodedStream Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            Start();
            return MediaFoundationStream.Open(path);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows N without its media pack: no Media Foundation at all.
            throw new ClipDecodeException("Media Foundation is not installed", ex);
        }
    }

    /// <summary>
    /// Starts Media Foundation once for the life of the process, and never shuts it down: a stream may be open on any
    /// thread at any time, and MFShutdown under an open reader would break it.
    /// </summary>
    private static void Start()
    {
        lock (StartLock)
        {
            if (_started) return;
            MediaFoundationStream.Check(MFStartup(MF_VERSION, MFSTARTUP_LITE));
            _started = true;
        }
    }
}

/// <summary>
/// One open file: the first audio stream, decoded to floats at its own rate, mixed down to stereo if it has more than two
/// channels, then resampled to <see cref="InternalFormat.SampleRate"/> if it is at another rate. Never holds more than one
/// decoded sample of the file in memory.
/// </summary>
/// <remarks>
/// Every frame is placed by the time of its sample. The first sample after a start or a seek is the exception: Media
/// Foundation's mp3 decoder drops its first 528 frames but keeps the time of its start and gives it a duration that matches
/// what is left (seen on Windows 11, 17/11/2026), so it is held until the next one comes and placed to end where that one
/// begins. The frames of the file sit on that timeline from <see cref="_origin"/> to
/// <see cref="_end"/>: for an mp3 with a LAME tag, past the tag frame (decoded as silence), the encoder delay and the
/// decoder delay, and short of the padding, so a clip comes back with its own length and its own first frame.
/// </remarks>
internal sealed unsafe class MediaFoundationStream : IDecodedStream
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;

    /// <summary>Native frames mixed and resampled at a time.</summary>
    private const int ChunkFrames = 4096;

    /// <summary>Zero frames fed at a time at the end, to bring out what the resampler still holds.</summary>
    private const int FlushFrames = 64;

    /// <summary>
    /// A seek asks Media Foundation for this much before the frame wanted: its mp3 decoder starts cold after a seek and gives
    /// silence for most of a packet (862 frames seen), and the frames up to the one wanted are dropped anyway.
    /// </summary>
    private const double SeekMarginSeconds = 0.1;

    private readonly int _nativeChannels;
    private readonly int _nativeRate;
    private readonly long _origin, _end;
    private readonly float[] _mixed;
    private readonly float[] _resampled;

    /// <summary>
    /// When resampling, a seek starts this many native frames before its target, so the filter has the real frames behind
    /// it rather than silence; the <see cref="_prerollOut"/> frames made from them are dropped. A whole number of both, so
    /// the first frame kept falls exactly on the target.
    /// </summary>
    private readonly int _preroll, _prerollOut;

    private nint _reader;
    private Resampler? _resampler;
    private float[] _ready = new float[ChunkFrames * 2];
    private int _readyAt, _readyCount;
    private bool _ended;

    /// <summary>On the timeline of the file, in native frames: the first frame to keep, and where the next sample should start.</summary>
    private long _keepFrom, _nextFrame;

    /// <summary>Frames still to drop at the start of what the resampler makes.</summary>
    private int _dropOut;

    /// <summary>The first sample after a start or a seek, held until the next one tells where it ends.</summary>
    private float[] _held = [];
    private int _heldFrames;
    private long _heldTime;
    private bool _holding = true;

    /// <summary>Native frames handed on, and frames made from them, since the start or the last seek.</summary>
    private long _framesIn, _framesOut;

    private MediaFoundationStream(nint reader, int nativeChannels, int nativeRate, TimeSpan duration, Mp3Gapless? gapless)
    {
        _reader = reader;
        _nativeChannels = nativeChannels;
        _nativeRate = nativeRate;
        Channels = nativeChannels == 1 ? 1 : 2;
        _origin = gapless?.FirstFrame ?? 0;
        _end = gapless is { } known ? known.FirstFrame + known.Frames : long.MaxValue;
        Duration = gapless is { } exact ? TimeSpan.FromTicks(exact.Frames * TicksPerSecond / nativeRate) : duration;
        _keepFrom = _origin;
        _mixed = new float[ChunkFrames * Channels];
        _resampler = NewResampler();
        _resampled = _resampler is null ? [] : new float[_resampler.MaxOutputFrames * Channels];
        if (_resampler is not null)
        {
            var period = nativeRate / (int)GreatestCommonDivisor(nativeRate, InternalFormat.SampleRate);
            _preroll = period * (int)Math.Ceiling(64.0 / period);
            _prerollOut = (int)((long)_preroll * InternalFormat.SampleRate / nativeRate);
        }
    }

    public int Channels { get; }

    public TimeSpan Duration { get; }

    /// <summary>A failed call becomes a <see cref="ClipDecodeException"/> that carries the code and nothing of the file.</summary>
    internal static void Check(int hr)
    {
        if (hr < 0) throw new ClipDecodeException($"Media Foundation failed: 0x{hr:X8}");
    }

    private static long GreatestCommonDivisor(long a, long b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);

    internal static MediaFoundationStream Open(string path)
    {
        nint reader;
        Check(MFCreateSourceReaderFromURL(path, 0, &reader));
        try
        {
            Check(SetStreamSelection(reader, MF_SOURCE_READER_ALL_STREAMS, false));
            Check(SetStreamSelection(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, true));

            nint wanted;
            Check(MFCreateMediaType(&wanted));
            try
            {
                Check(SetGuid(wanted, MF_MT_MAJOR_TYPE, MFMediaType_Audio));
                Check(SetGuid(wanted, MF_MT_SUBTYPE, MFAudioFormat_Float));
                Check(SetCurrentMediaType(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, wanted));
            }
            finally
            {
                Release(wanted);
            }

            nint current;
            uint channels, rate;
            Check(GetCurrentMediaType(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, &current));
            try
            {
                Check(GetUInt32(current, MF_MT_AUDIO_NUM_CHANNELS, &channels));
                Check(GetUInt32(current, MF_MT_AUDIO_SAMPLES_PER_SECOND, &rate));
            }
            finally
            {
                Release(current);
            }

            if (channels is 0 or > 32 || rate is 0 or > 768_000) throw new ClipDecodeException("Media Foundation gave no usable audio format");
            return new MediaFoundationStream(reader, (int)channels, (int)rate, ReadDuration(reader), Mp3Gapless.Read(path, (int)rate));
        }
        catch
        {
            Release(reader);
            throw;
        }
    }

    private static TimeSpan ReadDuration(nint reader)
    {
        var value = default(PropVariant64);
        if (GetPresentationAttribute(reader, MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &value) < 0) return TimeSpan.Zero;
        var ticks = value.Type == VT_UI8 ? value.Value : 0;
        PropVariantClear(&value);
        return ticks > 0 ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero;
    }

    private Resampler? NewResampler() =>
        _nativeRate == InternalFormat.SampleRate ? null : new Resampler(_nativeRate, InternalFormat.SampleRate, Channels, ChunkFrames);

    public void Seek(TimeSpan at)
    {
        ObjectDisposedException.ThrowIf(_reader == 0, this);
        var target = _origin + (long)Math.Round(Math.Max(0, at.TotalSeconds) * _nativeRate);
        _keepFrom = target;
        _dropOut = 0;
        if (_resampler is not null && target - _preroll >= 0)
        {
            _keepFrom = target - _preroll;
            _dropOut = _prerollOut;
        }

        var from = Math.Max(0, _keepFrom - (long)(SeekMarginSeconds * _nativeRate));
        var position = new PropVariant64 { Type = VT_I8, Value = from * TicksPerSecond / _nativeRate };
        Check(SetCurrentPosition(_reader, &position));

        _nextFrame = from;
        _holding = true;
        _heldFrames = 0;
        _readyAt = _readyCount = 0;
        _ended = false;
        _framesIn = _framesOut = 0;
        _resampler = NewResampler();
    }

    public int Read(Span<float> interleaved)
    {
        ObjectDisposedException.ThrowIf(_reader == 0, this);
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

    // Reads the next sample of the file into the ready floats. Called only when they are all read.
    private void Fill()
    {
        _readyAt = 0;
        uint flags;
        long time;
        nint sample = 0;
        Check(ReadSample(_reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, &flags, &time, &sample));
        try
        {
            if ((flags & MF_SOURCE_READERF_ERROR) != 0) throw new ClipDecodeException("Media Foundation stopped reading the file");
            if (sample != 0) Take(sample);
        }
        finally
        {
            Release(sample);
        }

        // The last read may bring both a sample and the end, or the end with no sample.
        if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
        {
            if (_holding && _heldFrames > 0) Place(_held.AsSpan(0, _heldFrames * _nativeChannels), FrameAt(_heldTime));
            _holding = false;
            Flush();
            _ended = true;
        }
    }

    private void Take(nint sample)
    {
        nint buffer;
        Check(ConvertToContiguousBuffer(sample, &buffer));
        try
        {
            byte* data;
            uint length;
            Check(Lock(buffer, &data, &length));
            try
            {
                var native = new ReadOnlySpan<float>(data, (int)(length / sizeof(float)));
                native = native[..(native.Length / _nativeChannels * _nativeChannels)];
                long time;
                var timed = GetSampleTime(sample, &time) >= 0;
                if (_holding)
                {
                    if (_heldFrames == 0)
                    {
                        // Copied out, so the buffer is unlocked before the next read.
                        if (_held.Length < native.Length) _held = new float[native.Length];
                        native.CopyTo(_held);
                        _heldFrames = native.Length / _nativeChannels;
                        _heldTime = timed ? time : -1;
                        return;
                    }

                    // The held sample ends where this one begins.
                    var start = timed ? FrameAt(time) : FrameAt(_heldTime) + _heldFrames;
                    _holding = false;
                    Place(_held.AsSpan(0, _heldFrames * _nativeChannels), start - _heldFrames);
                    _heldFrames = 0;
                    Place(native, start);
                    return;
                }

                Place(native, timed ? FrameAt(time) : _nextFrame);
            }
            finally
            {
                Unlock(buffer);
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    // A time of Media Foundation as a frame of the file's timeline. An unknown time (-1) means right after the last frame.
    private long FrameAt(long time) => time < 0 ? _nextFrame : (long)Math.Round((double)time * _nativeRate / TicksPerSecond);

    // Hands on the frames of one sample that fall between the first frame to keep and the end of the file.
    private void Place(ReadOnlySpan<float> native, long first)
    {
        var frames = native.Length / _nativeChannels;
        _nextFrame = first + frames;
        var from = (int)Math.Clamp(_keepFrom - first, 0, frames);
        var to = (int)Math.Clamp(_end - first, 0, frames);
        if (to > from) Push(native.Slice(from * _nativeChannels, (to - from) * _nativeChannels));
    }

    // Mixes, resamples and queues native frames.
    private void Push(ReadOnlySpan<float> native)
    {
        while (!native.IsEmpty)
        {
            var frames = Math.Min(ChunkFrames, native.Length / _nativeChannels);
            if (frames == 0) return;
            var mixed = _mixed.AsSpan(0, frames * Channels);
            MixDown(native[..(frames * _nativeChannels)], _nativeChannels, mixed);
            native = native[(frames * _nativeChannels)..];
            _framesIn += frames;
            Resample(mixed);
        }
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

    // At the end, the resampler still holds the last frames: zeros push them out, and what goes past the length of the
    // file at the new rate is cut. The frames dropped after a seek are counted on both sides, so the cut stays right.
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
        Release(_reader);
        _reader = 0;
    }
}

/// <summary>
/// Where the frames of an mp3 really are, from the Xing or Info frame and its LAME tag (LAME and FFmpeg write one): the
/// first frame of the file and the number of frames, on the timeline Media Foundation decodes, which starts with the tag
/// frame as silence and then the encoder delay and the decoder delay of 529 frames.
/// </summary>
/// <param name="FirstFrame">Frames of the decoded timeline before the first frame of the file.</param>
/// <param name="Frames">Frames of the file.</param>
internal readonly record struct Mp3Gapless(long FirstFrame, long Frames)
{
    private const int DecoderDelay = 529;

    /// <summary>Null for a wav, an mp3 without the tag, or a tag that doesn't hold together: then the decoder's own length is kept.</summary>
    public static Mp3Gapless? Read(string path, int rate)
    {
        try
        {
            if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[10];
            if (file.Read(head) < 10) return null;
            long at = 0;
            if (head[0] == 'I' && head[1] == 'D' && head[2] == '3')
            {
                // An ID3v2 tag before the first frame: its size is in 7-bit bytes, plus the footer if the flag says so.
                at = 10 + ((head[6] & 0x7F) << 21 | (head[7] & 0x7F) << 14 | (head[8] & 0x7F) << 7 | (head[9] & 0x7F));
                if ((head[5] & 0x10) != 0) at += 10;
            }

            Span<byte> frame = stackalloc byte[512];
            file.Position = at;
            return file.Read(frame) == frame.Length ? Parse(frame, rate) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static Mp3Gapless? Parse(ReadOnlySpan<byte> frame, int rate)
    {
        // MPEG audio, layer III.
        if (frame[0] != 0xFF || (frame[1] & 0xE0) != 0xE0 || ((frame[1] >> 1) & 3) != 1) return null;
        var mpeg1 = ((frame[1] >> 3) & 3) == 3;
        var mono = (frame[3] >> 6) == 3;
        var samplesPerFrame = mpeg1 ? 1152 : 576;
        var tag = 4 + (mpeg1 ? (mono ? 17 : 32) : (mono ? 9 : 17));
        if (!frame[tag..].StartsWith("Xing"u8) && !frame[tag..].StartsWith("Info"u8)) return null;

        var flags = BinaryPrimitives.ReadUInt32BigEndian(frame[(tag + 4)..]);
        if ((flags & 1) == 0) return null;
        var at = tag + 8;
        var frames = BinaryPrimitives.ReadUInt32BigEndian(frame[at..]);
        at += 4;
        if ((flags & 2) != 0) at += 4;
        if ((flags & 4) != 0) at += 100;
        if ((flags & 8) != 0) at += 4;
        if (at + 24 > frame.Length || !(frame[at..].StartsWith("LAME"u8) || frame[at..].StartsWith("Lavc"u8) || frame[at..].StartsWith("Lavf"u8))) return null;

        var delay = frame[at + 21] << 4 | frame[at + 22] >> 4;
        var padding = (frame[at + 22] & 0x0F) << 8 | frame[at + 23];
        var length = (long)frames * samplesPerFrame - delay - padding;
        if (frames == 0 || length <= 0 || length > (long)rate * 60 * 60 * 24) return null;
        return new Mp3Gapless(samplesPerFrame + delay + DecoderDelay, length);
    }
}
