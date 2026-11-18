using Hindsound.Library;
using NLayer;

namespace Hindsound.Linux.Library;

/// <summary>
/// An mp3 read through NLayer's frame decoder (managed code, MIT). The frames are found and fed one at a time here, not
/// through NLayer's file reader: that way every frame gives exactly its share of samples (a frame NLayer can't decode gives
/// silence, not a gap that would move the rest), a seek starts the decoder at a frame of this code's choosing, and where the
/// sound starts and ends is decided here.
/// </summary>
/// <remarks>
/// The timeline is that of the decoder: every frame of the file in order, the tag frame included (it decodes as silence), from
/// the first sample it outputs. With a LAME tag, the sound starts after the tag frame, the encoder delay and the delay of the
/// decoder, and lasts the frames the tag counts, as on Windows. Without one only the delay of the decoder is left out, and
/// the length is that of the frames found.
/// </remarks>
internal sealed class ClipDecoderMp3 : IClipDecoderSource
{
    /// <summary>The most bytes of main data a frame can take from the ones before it.</summary>
    private const int ReservoirBytes = 511;

    private readonly FileStream _file;
    private readonly List<long> _offsets;
    private readonly MpegFrameDecoder _decoder = new();
    private readonly ClipDecoderMp3Frame _frame = new();
    private readonly byte[] _bytes = new byte[ClipDecoderMp3Frame.MaxLength];
    private readonly float[] _pcm = new float[2304];
    private readonly int _samplesPerFrame;

    /// <summary>The frames a seek starts before its target: enough for the reservoir of bits to be full again, and one for the overlap of the transform.</summary>
    private readonly int _warmUp;

    /// <summary>On the timeline of the decoder, in frames: the first one of the clip.</summary>
    private readonly long _origin;

    private int _next;
    private int _pcmAt, _pcmLength;

    /// <summary>Frames still to drop at the start of what the next frame decodes to.</summary>
    private long _drop;

    /// <summary>On the timeline of the decoder: the frame the next read gives.</summary>
    private long _at;
    private bool _ended, _disposed;

    private ClipDecoderMp3(FileStream file, List<long> offsets, ClipDecoderMp3Header first, long origin, long frames)
    {
        _file = file;
        _offsets = offsets;
        _samplesPerFrame = first.SamplesPerFrame;
        _origin = origin;
        Frames = frames;
        Channels = first.Channels;
        Rate = first.SampleRate;
        _warmUp = Math.Max(4, (ReservoirBytes + Math.Max(1, first.Length - 40) - 1) / Math.Max(1, first.Length - 40) + 2);
        Seek(0);
    }

    public int Channels { get; }

    public int Rate { get; }

    public long Frames { get; }

    /// <summary>Takes the file, and closes it if it can't be read.</summary>
    public static ClipDecoderMp3 Open(FileStream file)
    {
        try
        {
            var start = ClipDecoderMp3Tag.FirstFrameOffset(file);
            var offsets = ClipDecoderMp3Frames.Walk(file, start, out var first);
            if (offsets.Count == 0) throw new ClipDecodeException("the file holds no mp3");

            long origin, frames;
            if (ClipDecoderMp3Tag.Read(file, offsets[0]) is { } tag && tag.SamplesPerFrame == first.SamplesPerFrame)
            {
                origin = tag.SamplesPerFrame + tag.Delay + ClipDecoderMp3Tag.DecoderDelay;
                frames = tag.Frames;
            }
            else
            {
                // Every frame found, less the delay of the decoder.
                origin = ClipDecoderMp3Tag.DecoderDelay;
                frames = (long)offsets.Count * first.SamplesPerFrame - origin;
            }

            if (frames <= 0) throw new ClipDecodeException("the mp3 holds no sound");
            return new ClipDecoderMp3(file, offsets, first, origin, frames);
        }
        catch (Exception ex) when (ex is not ClipDecodeException)
        {
            file.Dispose();
            throw new ClipDecodeException("the mp3 can't be read", ex);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public void Seek(long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ended = frame >= Frames;
        _pcmAt = _pcmLength = 0;
        if (_ended) return;

        // The decoder starts cold at the frame chosen, and needs those before the one wanted to be right.
        var target = _origin + Math.Max(0, frame);
        _next = (int)Math.Min(Math.Max(0, target / _samplesPerFrame - _warmUp), _offsets.Count - 1);
        _drop = target - (long)_next * _samplesPerFrame;
        _at = target;
        _decoder.Reset();
    }

    public int Read(Span<float> interleaved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wanted = (int)Math.Min(interleaved.Length / Channels, _origin + Frames - _at);
        var written = 0;
        while (written < wanted && !_ended)
        {
            if (_pcmAt == _pcmLength && !Decode())
            {
                _ended = true;
                break;
            }

            var count = Math.Min(_pcmLength - _pcmAt, (wanted - written) * Channels);
            _pcm.AsSpan(_pcmAt, count).CopyTo(interleaved[(written * Channels)..]);
            _pcmAt += count;
            written += count / Channels;
        }

        _at += written;
        return written * Channels;
    }

    // Decodes the next frame of the file into the samples to give, less the ones a seek drops. False at the end of the file.
    private bool Decode()
    {
        if (_next >= _offsets.Count) return false;
        var position = _offsets[_next];
        _file.Position = position;
        ClipDecoderMp3Header header;
        try
        {
            if (_file.Read(_bytes.AsSpan(0, 4)) != 4
                || !ClipDecoderMp3Header.TryParse((uint)(_bytes[0] << 24 | _bytes[1] << 16 | _bytes[2] << 8 | _bytes[3]), out header)
                || _file.Read(_bytes.AsSpan(4, header.Length - 4)) != header.Length - 4)
                throw new ClipDecodeException("the mp3 changed while it was read");
        }
        catch (IOException ex)
        {
            throw new ClipDecodeException("the mp3 can't be read", ex);
        }

        _next++;
        _frame.Load(header, _bytes.AsSpan(0, header.Length));
        var made = 0;
        try
        {
            made = _decoder.DecodeFrame(_frame, _pcm.AsSpan());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Data that doesn't decode, from damage or from a decoder that starts cold: the frame is silent, and the decoder
            // starts again at the next one.
            _decoder.Reset();
        }

        // A frame whose bits are not all there (the reservoir is empty after a seek) gives nothing: silence keeps the timeline.
        var length = _samplesPerFrame * Channels;
        if (made != length) _pcm.AsSpan(0, length).Clear();
        _pcmLength = length;
        var drop = (int)Math.Min(_drop, _samplesPerFrame);
        _drop -= drop;
        _pcmAt = drop * Channels;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _file.Dispose();
    }
}
