using System.Buffers.Binary;

namespace Hindsound.Unix.Library;

/// <summary>
/// Where the frames of an mp3 really are, from the Xing or Info frame and its LAME tag (LAME and FFmpeg write one): the frame
/// that holds the tag, the number of frames of sound, and the silence the encoder put before them. The same rules as the
/// Windows decoder, so a cut lands on the same frame on both systems.
/// </summary>
/// <param name="SamplesPerFrame">1152 for MPEG-1, 576 for MPEG-2 and 2.5.</param>
/// <param name="Delay">Frames the encoder added before the sound.</param>
/// <param name="Frames">Frames of the sound, without the encoder's delay and padding.</param>
internal readonly record struct ClipDecoderMp3Tag(int SamplesPerFrame, int Delay, long Frames)
{
    /// <summary>
    /// A decoder starts its output with a delay of its own: 528 frames of the filter, and one. The first frame of the sound is
    /// that many frames past the encoder delay.
    /// </summary>
    public const int DecoderDelay = 529;

    /// <summary>The frames of the decoded timeline that come before the first frame of the sound: the tag frame (decoded as silence) and both delays.</summary>
    public long FirstFrame => SamplesPerFrame + Delay + DecoderDelay;

    /// <summary>Where the first frame of the file begins: past an ID3v2 tag if there is one.</summary>
    public static long FirstFrameOffset(Stream file)
    {
        file.Position = 0;
        Span<byte> head = stackalloc byte[10];
        if (file.Read(head) < 10 || head[0] != 'I' || head[1] != 'D' || head[2] != '3') return 0;

        // The size of the tag is in 7-bit bytes, plus the footer if the flag says so.
        var at = 10 + ((head[6] & 0x7F) << 21 | (head[7] & 0x7F) << 14 | (head[8] & 0x7F) << 7 | (head[9] & 0x7F));
        if ((head[5] & 0x10) != 0) at += 10;
        return at;
    }

    /// <summary>Null for an mp3 without the tag, or a tag that doesn't hold together: then the frames are counted instead.</summary>
    public static ClipDecoderMp3Tag? Read(Stream file, long start)
    {
        Span<byte> frame = stackalloc byte[512];
        file.Position = start;
        return file.Read(frame) == frame.Length ? Parse(frame) : null;
    }

    internal static ClipDecoderMp3Tag? Parse(ReadOnlySpan<byte> frame)
    {
        // MPEG audio, layer III.
        if (frame[0] != 0xFF || (frame[1] & 0xE0) != 0xE0 || ((frame[1] >> 1) & 3) != 1) return null;
        var version = (frame[1] >> 3) & 3;
        if (version == 1) return null;
        var rateIndex = (frame[2] >> 2) & 3;
        if (rateIndex == 3) return null;
        var mpeg1 = version == 3;
        var rate = (rateIndex switch { 0 => 44100, 1 => 48000, _ => 32000 }) >> (mpeg1 ? 0 : version == 2 ? 1 : 2);
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
        return new ClipDecoderMp3Tag(samplesPerFrame, delay, length);
    }
}
