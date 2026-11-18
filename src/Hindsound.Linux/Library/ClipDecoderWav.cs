using System.Buffers.Binary;
using Hindsound.Library;

namespace Hindsound.Linux.Library;

/// <summary>
/// A RIFF/WAVE file read in portable code: integer PCM at 8, 16, 24 or 32 bits and float at 32 or 64 bits, any rate and up to
/// 32 channels. The app writes 16-bit PCM; the others are for a clip someone dropped into the folder. A data chunk that claims
/// more than the file holds is cut to the file, as it is after a crash while writing.
/// </summary>
internal sealed class ClipDecoderWav : IClipDecoderSource
{
    private const int ChunkFrames = 4096;

    private readonly FileStream _file;
    private readonly long _dataStart;
    private readonly int _frameBytes, _bytesPerSample;
    private readonly bool _float;
    private readonly byte[] _bytes;
    private long _next;

    private ClipDecoderWav(FileStream file, int channels, int rate, int bytesPerSample, bool isFloat, long dataStart, long dataLength)
    {
        _file = file;
        Channels = channels;
        Rate = rate;
        _bytesPerSample = bytesPerSample;
        _float = isFloat;
        _frameBytes = channels * bytesPerSample;
        _dataStart = dataStart;
        Frames = dataLength / _frameBytes;
        _bytes = new byte[ChunkFrames * _frameBytes];
    }

    public int Channels { get; }

    public int Rate { get; }

    public long Frames { get; }

    /// <summary>True if the file starts like a WAVE file. A look at its first bytes, nothing more.</summary>
    public static bool Looks(ReadOnlySpan<byte> head) =>
        head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WAVE"u8);

    /// <summary>Takes the file, and closes it if it can't be read.</summary>
    public static ClipDecoderWav Open(FileStream file)
    {
        try
        {
            return Parse(file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static ClipDecoderWav Parse(FileStream file)
    {
        Span<byte> head = stackalloc byte[12];
        file.Position = 0;
        if (file.Read(head) < 12 || !Looks(head)) throw new ClipDecodeException("not a wav file");

        int channels = 0, rate = 0, bits = 0, tag = 0;
        var haveFormat = false;
        Span<byte> chunk = stackalloc byte[8];
        Span<byte> format = stackalloc byte[40];
        while (file.Position + 8 <= file.Length)
        {
            if (file.Read(chunk) < 8) break;
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            var body = file.Position;
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16) throw new ClipDecodeException("the wav format is too short");
                var read = file.Read(format[..(int)Math.Min(size, format.Length)]);
                if (read < 16) throw new ClipDecodeException("the wav format is cut short");
                tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                rate = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(format[4..]), int.MaxValue);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);

                // WAVE_FORMAT_EXTENSIBLE: the real tag is the first two bytes of the sub-format GUID.
                if (tag == 0xFFFE && size >= 40 && read >= 26) tag = BinaryPrimitives.ReadUInt16LittleEndian(format[24..]);
                haveFormat = true;
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (!haveFormat) throw new ClipDecodeException("the wav data comes before its format");
                var isFloat = tag == 3;
                if ((tag != 1 && tag != 3) || bits % 8 != 0
                    || !(isFloat ? bits is 32 or 64 : bits is 8 or 16 or 24 or 32))
                    throw new ClipDecodeException("a wav format that is not read");
                if (channels is 0 or > 32 || rate is 0 or > 768_000) throw new ClipDecodeException("the wav has no usable audio format");

                // A size the file can't hold (a writer that stopped before it could fix the header) is cut to the file.
                var length = Math.Min(size, file.Length - body);
                return new ClipDecoderWav(file, channels, rate, bits / 8, isFloat, body, length);
            }

            // Chunks are padded to an even size.
            file.Position = body + size + (size & 1);
        }

        throw new ClipDecodeException("the wav holds no sound");
    }

    public void Seek(long frame)
    {
        _next = Math.Clamp(frame, 0, Frames);
        _file.Position = _dataStart + _next * _frameBytes;
    }

    public int Read(Span<float> interleaved)
    {
        var frames = (int)Math.Min(Math.Min(interleaved.Length / Channels, ChunkFrames), Frames - _next);
        if (frames <= 0) return 0;
        try
        {
            var wanted = frames * _frameBytes;
            var got = 0;
            while (got < wanted)
            {
                var read = _file.Read(_bytes, got, wanted - got);
                if (read == 0) break;
                got += read;
            }

            frames = got / _frameBytes;
        }
        catch (IOException ex)
        {
            throw new ClipDecodeException("the wav can't be read", ex);
        }

        Convert(_bytes.AsSpan(0, frames * _frameBytes), interleaved);
        _next += frames;

        // A file shorter than its header says: the frames it holds are the clip.
        return frames * Channels;
    }

    private void Convert(ReadOnlySpan<byte> bytes, Span<float> floats)
    {
        var count = bytes.Length / _bytesPerSample;
        if (_float)
        {
            for (var i = 0; i < count; i++)
                floats[i] = _bytesPerSample == 4
                    ? BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * 4)..])
                    : (float)BinaryPrimitives.ReadDoubleLittleEndian(bytes[(i * 8)..]);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var at = i * _bytesPerSample;
            floats[i] = _bytesPerSample switch
            {
                1 => (bytes[at] - 128) / 128f,
                2 => BinaryPrimitives.ReadInt16LittleEndian(bytes[at..]) / 32768f,
                3 => ((bytes[at] << 8 | bytes[at + 1] << 16 | bytes[at + 2] << 24) >> 8) / 8388608f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]) / 2147483648f,
            };
        }
    }

    public void Dispose() => _file.Dispose();
}
