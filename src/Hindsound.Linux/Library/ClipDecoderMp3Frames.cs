using NLayer;

namespace Hindsound.Linux.Library;

/// <summary>The four bytes that open a frame of an mp3 of layer III, read: what the frame holds, and how long it is.</summary>
internal readonly struct ClipDecoderMp3Header
{
    // Kilobits per second by version (MPEG-1, then MPEG-2 and 2.5) and index, for layer III.
    private static readonly int[] Mpeg1Rates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Rates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

    private ClipDecoderMp3Header(uint bits, MpegVersion version, int rate, int bitRate, int length)
    {
        Bits = bits;
        Version = version;
        SampleRate = rate;
        BitRate = bitRate;
        Length = length;
    }

    /// <summary>The header as it is in the file, most significant byte first.</summary>
    public uint Bits { get; }

    public MpegVersion Version { get; }

    public int SampleRate { get; }

    /// <summary>Bits per second.</summary>
    public int BitRate { get; }

    /// <summary>Bytes of the whole frame, header included.</summary>
    public int Length { get; }

    public int SampleRateIndex => (int)(Bits >> 10) & 3;

    public bool HasCrc => (Bits & 0x10000) == 0;

    public MpegChannelMode ChannelMode => (MpegChannelMode)((Bits >> 6) & 3);

    public int ChannelModeExtension => (int)(Bits >> 4) & 3;

    public bool IsCopyrighted => (Bits & 8) != 0;

    public int BitRateIndex => (int)(Bits >> 12) & 15;

    public int Channels => ChannelMode == MpegChannelMode.Mono ? 1 : 2;

    /// <summary>1152 for MPEG-1, 576 for MPEG-2 and 2.5.</summary>
    public int SamplesPerFrame => Version == MpegVersion.Version1 ? 1152 : 576;

    /// <summary>
    /// True if the same stream goes on: the version, the rate and the number of channels don't change in the middle of a
    /// file the app writes, and the decoder is made for one.
    /// </summary>
    public bool SameStream(in ClipDecoderMp3Header other) =>
        Version == other.Version && SampleRateIndex == other.SampleRateIndex && Channels == other.Channels;

    /// <summary>False for anything but a frame of layer III with a bitrate of its own (the free format is not read).</summary>
    public static bool TryParse(uint bits, out ClipDecoderMp3Header header)
    {
        header = default;
        if ((bits & 0xFFE00000) != 0xFFE00000) return false;
        var versionBits = (int)(bits >> 19) & 3;
        if (versionBits == 1 || ((bits >> 17) & 3) != 1) return false;
        var bitRateIndex = (int)(bits >> 12) & 15;
        var rateIndex = (int)(bits >> 10) & 3;
        if (bitRateIndex is 0 or 15 || rateIndex == 3) return false;

        var version = versionBits switch { 3 => MpegVersion.Version1, 2 => MpegVersion.Version2, _ => MpegVersion.Version25 };
        var rate = (rateIndex switch { 0 => 44100, 1 => 48000, _ => 32000 }) >> (versionBits == 3 ? 0 : versionBits == 2 ? 1 : 2);
        var bitRate = (version == MpegVersion.Version1 ? Mpeg1Rates : Mpeg2Rates)[bitRateIndex] * 1000;
        var padding = (int)(bits >> 9) & 1;
        var length = (version == MpegVersion.Version1 ? 144 : 72) * bitRate / rate + padding;
        header = new ClipDecoderMp3Header(bits, version, rate, bitRate, length);
        return true;
    }
}

/// <summary>
/// One frame of an mp3, in the form NLayer's decoder takes: the bytes of the frame, read bit by bit from after its header (and
/// its CRC). Made once and loaded with each frame in turn.
/// </summary>
internal sealed class ClipDecoderMp3Frame : IMpegFrame
{
    private readonly byte[] _bytes = new byte[2048];
    private ClipDecoderMp3Header _header;
    private int _length, _readOffset, _bitsRead;
    private ulong _bitBucket;

    public int SampleRate => _header.SampleRate;

    public int SampleRateIndex => _header.SampleRateIndex;

    public int FrameLength => _length;

    public int BitRate => _header.BitRate;

    public MpegVersion Version => _header.Version;

    public MpegLayer Layer => MpegLayer.LayerIII;

    public MpegChannelMode ChannelMode => _header.ChannelMode;

    public int ChannelModeExtension => _header.ChannelModeExtension;

    public int SampleCount => _header.SamplesPerFrame;

    public int BitRateIndex => _header.BitRateIndex;

    public bool IsCopyrighted => _header.IsCopyrighted;

    public bool HasCrc => _header.HasCrc;

    /// <summary>The CRC is not checked: a frame that is damaged decodes to noise at worst, and the file is the app's own.</summary>
    public bool IsCorrupted => false;

    /// <summary>The longest frame of layer III is 1441 bytes (320 kbps at 32 kHz, padded).</summary>
    public static int MaxLength => 2048;

    /// <summary>Takes the bytes of a frame, header included, and starts reading at the side information.</summary>
    public void Load(in ClipDecoderMp3Header header, ReadOnlySpan<byte> bytes)
    {
        _header = header;
        _length = bytes.Length;
        bytes.CopyTo(_bytes);
        Reset();
    }

    public void Reset()
    {
        _readOffset = 4 + (_header.HasCrc ? 2 : 0);
        _bitBucket = 0;
        _bitsRead = 0;
    }

    public int ReadBits(int bitCount)
    {
        if (bitCount is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(bitCount));
        while (_bitsRead < bitCount)
        {
            if (_readOffset >= _length) throw new EndOfStreamException();
            _bitBucket = _bitBucket << 8 | _bytes[_readOffset++];
            _bitsRead += 8;
        }

        var value = (int)((_bitBucket >> (_bitsRead - bitCount)) & ((1UL << bitCount) - 1));
        _bitsRead -= bitCount;
        return value;
    }
}

/// <summary>Where the frames of an mp3 are in the file: every one that is whole, in order, the first being the tag frame if the file has one.</summary>
internal static class ClipDecoderMp3Frames
{
    /// <summary>The most bytes looked through to find the next frame after damage, or the first one.</summary>
    private const int ResyncLimit = 1 << 20;

    /// <summary>Walks the file from <paramref name="start"/> and returns the offset of each frame. Empty if there is no mp3 in it.</summary>
    public static List<long> Walk(Stream file, long start, out ClipDecoderMp3Header first)
    {
        var offsets = new List<long>();
        first = default;
        Span<byte> bytes = stackalloc byte[4];
        var length = file.Length;
        var position = Find(file, start, length, null, out first);
        var header = first;
        while (position >= 0)
        {
            // A last frame cut short is not a frame.
            if (position + header.Length > length) break;
            offsets.Add(position);
            position += header.Length;

            file.Position = position;
            if (file.Read(bytes) == 4
                && ClipDecoderMp3Header.TryParse((uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]), out var next)
                && first.SameStream(next))
            {
                header = next;
                continue;
            }

            // Damage, or what follows the sound (a tag at the end): look for the next frame, if there is one.
            position = Find(file, position, length, first, out header);
        }

        return offsets;
    }

    // The first offset from `from` on where a frame sits that is followed by another one, or runs to the end of the file.
    private static long Find(Stream file, long from, long length, ClipDecoderMp3Header? stream, out ClipDecoderMp3Header found)
    {
        found = default;
        var block = new byte[4096 + 4];
        var limit = Math.Min(length, from + ResyncLimit);
        for (var at = from; at + 4 <= limit; at += 4096)
        {
            file.Position = at;
            var read = file.Read(block);
            for (var i = 0; i < 4096 && i + 4 <= read; i++)
            {
                if (block[i] != 0xFF) continue;
                var bits = (uint)(block[i] << 24 | block[i + 1] << 16 | block[i + 2] << 8 | block[i + 3]);
                if (!ClipDecoderMp3Header.TryParse(bits, out var header) || (stream is { } known && !known.SameStream(header))) continue;

                var next = at + i + header.Length;
                if (next + 4 <= length)
                {
                    Span<byte> after = stackalloc byte[4];
                    file.Position = next;
                    if (file.Read(after) != 4
                        || !ClipDecoderMp3Header.TryParse((uint)(after[0] << 24 | after[1] << 16 | after[2] << 8 | after[3]), out var following)
                        || !header.SameStream(following))
                        continue;
                }
                else if (next > length)
                {
                    continue;
                }

                found = header;
                return at + i;
            }
        }

        return -1;
    }
}
