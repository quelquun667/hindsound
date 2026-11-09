using System.Buffers.Binary;
using System.IO;
using System.Text;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>A false header gives no duration, never an absurd one, and a real file at either end of the range keeps its own.</summary>
public sealed class AudioFileLengthRateTests : IDisposable
{
    // MPEG 1 layer III, 128 kbps, 48 kHz, stereo: 384 bytes and 24 ms a frame, the Info frame at byte 36.
    private static readonly byte[] Stereo128 = [0xFF, 0xFB, 0x94, 0x00];
    private const int FrameBytes = 384, TagAt = 36;

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private TimeSpan? Read(string name, byte[] bytes)
    {
        var path = _root.Combine(name);
        File.WriteAllBytes(path, bytes);
        return AudioFileLength.Read(path);
    }

    // ---------------------------------------------------------------- MP3: the bitrate the Info frame implies

    [Theory]
    [InlineData(100_000u)] // 0.1 kbps: 40 minutes in a file of 2.4 s
    [InlineData(10_000u)] // 1.3 kbps
    [InlineData(1u)] // 12 Mbps: a file of 2.4 s said to last 24 ms
    [InlineData(4u)] // 3 Mbps
    [InlineData(uint.MaxValue)]
    public void A_false_number_of_frames_gives_no_duration(uint frames)
    {
        var mp3 = Frames(100);
        Tag(mp3, "Info", frames);

        Assert.Null(Read("a.mp3", mp3));
    }

    [Theory]
    [InlineData("Info")]
    [InlineData("Xing")]
    public void A_false_size_gives_no_duration_even_with_a_likely_number_of_frames(string id)
    {
        var mp3 = Frames(100);
        Tag(mp3, id, 100, bytes: FrameBytes + 100);

        Assert.Null(Read("a.mp3", mp3));
    }

    [Theory]
    [InlineData(8.0, true)]
    [InlineData(320.0, true)]
    [InlineData(7.0, false)]
    [InlineData(321.0, false)]
    public void The_bitrate_of_a_tag_may_go_from_8_to_320_kbps(double kbps, bool known)
    {
        // 1 000 frames are 24 s. The size says the bitrate, and the frame of the tag does not count in it.
        var sound = (uint)(kbps * 1000 * 24 / 8);
        var size = sound + FrameBytes;
        var mp3 = Frames((int)(size / FrameBytes) + 1);
        Tag(mp3, "Info", 1000, size);

        var length = Read("a.mp3", mp3);

        Assert.Equal(known, length is not null);
        if (known)
        {
            Assert.Equal(24.0, length!.Value.TotalSeconds, 3);
        }
    }

    [Fact]
    public void Without_a_size_the_file_itself_gives_the_bitrate()
    {
        var mp3 = Frames(100);
        Tag(mp3, "Info", 100);
        Assert.Equal(2.4, Read("known.mp3", mp3)!.Value.TotalSeconds, 3);

        // The same number of frames in a file 50 times shorter: about 1 kbps.
        var cut = Frames(2);
        Tag(cut, "Info", 100);
        Assert.Null(Read("short.mp3", cut));
    }

    [Fact]
    public void A_tag_with_no_frame_count_still_goes_by_the_header_of_the_frame()
    {
        var mp3 = Frames(100);
        Encoding.ASCII.GetBytes("Info").CopyTo(mp3, TagAt);

        Assert.Equal(2.4, Read("a.mp3", mp3)!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(0.05, 320, 2)]
    [InlineData(0.05, 320, 1)]
    [InlineData(0.05, 128, 2)]
    [InlineData(0.5, 320, 2)]
    [InlineData(0.5, 192, 1)]
    public void A_very_short_real_MP3_is_known(double seconds, int kbps, int channels)
    {
        var path = _root.Combine("a.mp3");
        Encoders.WriteMp3(path, Signals.Sine(440, 0.3, seconds, channels), channels, kbps);

        Assert.NotNull(AudioFileLength.Read(path));
    }

    // ---------------------------------------------------------------- WAV: the byte rate of PCM and float

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_PCM_or_float_WAV_whose_byte_rate_is_its_rate_times_its_block_is_known(ushort code)
    {
        var wav = Wav(Format(code, sampleRate: 1000, byteRate: 2000, blockAlign: 2), Chunk("data", new byte[6000]));

        Assert.Equal(3.0, Read("a.wav", wav)!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(1, 1000, 2001, 2)] // a byte too many
    [InlineData(1, 1000, 1, 2)] // what would make days out of a short file
    [InlineData(3, 1000, 1, 2)]
    [InlineData(1, 0, 2000, 2)] // no sample rate
    [InlineData(1, 1000, 2000, 0)] // no block
    [InlineData(3, 44100, 88200, 4)]
    public void A_PCM_or_float_WAV_with_another_byte_rate_gives_no_duration(ushort code, uint sampleRate, uint byteRate, ushort blockAlign)
    {
        var wav = Wav(Format(code, sampleRate, byteRate, blockAlign), Chunk("data", new byte[6000]));

        Assert.Null(Read("a.wav", wav));
    }

    [Fact]
    public void The_product_does_not_wrap_around_32_bits()
    {
        // 0x80000001 x 2 is 2 in 32 bits.
        var wav = Wav(Format(1, 0x80000001, 2, 2), Chunk("data", new byte[6000]));

        Assert.Null(Read("a.wav", wav));
    }

    [Theory]
    [InlineData(2)] // ADPCM
    [InlineData(0x11)] // IMA ADPCM
    [InlineData(0x55)] // MP3 in a WAV
    public void A_compressed_WAV_goes_by_its_own_byte_rate(ushort code)
    {
        // 16 kbps of ADPCM at 8 kHz: neither the rate nor the block gives the 2 000 bytes a second.
        var wav = Wav(Format(code, sampleRate: 8000, byteRate: 2000, blockAlign: 256), Chunk("data", new byte[6000]));

        Assert.Equal(3.0, Read("a.wav", wav)!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void An_extensible_WAV_in_PCM_or_float_is_held_to_the_same_rule(ushort subFormat)
    {
        var good = Wav(Extensible(subFormat, sampleRate: 1000, byteRate: 2000, blockAlign: 2), Chunk("data", new byte[6000]));
        Assert.Equal(3.0, Read("good.wav", good)!.Value.TotalSeconds, 3);

        var bad = Wav(Extensible(subFormat, sampleRate: 1000, byteRate: 1, blockAlign: 2), Chunk("data", new byte[6000]));
        Assert.Null(Read("bad.wav", bad));
    }

    [Fact]
    public void An_extensible_WAV_of_a_compressed_format_goes_by_its_own_byte_rate()
    {
        var wav = Wav(Extensible(0x55, sampleRate: 8000, byteRate: 2000, blockAlign: 256), Chunk("data", new byte[6000]));

        Assert.Equal(3.0, Read("a.wav", wav)!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void An_extensible_WAV_with_no_sub_format_gives_no_duration()
    {
        var wav = Wav(Format(0xFFFE, sampleRate: 1000, byteRate: 2000, blockAlign: 2), Chunk("data", new byte[6000]));

        Assert.Null(Read("a.wav", wav));
    }

    [Fact]
    public void A_format_with_data_before_it_ending_the_file_is_read()
    {
        // The last chunk of the file, so what is read of it stops before the size of an extensible format.
        var wav = Wav(Chunk("data", new byte[6000]), Format(1, 1000, 2000, 2));

        Assert.Equal(3.0, Read("a.wav", wav)!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_real_WAV_passes_its_own_guard(int channels)
    {
        var path = _root.Combine("a.wav");
        Encoders.WriteWav(path, Signals.Sine(440, 0.3, 1, channels), channels);

        Assert.Equal(1.0, AudioFileLength.Read(path)!.Value.TotalSeconds, 3);
    }

    // ---------------------------------------------------------------- building files

    private static byte[] Frames(int count)
    {
        var bytes = new byte[FrameBytes * count];
        for (var i = 0; i < count; i++)
        {
            Stereo128.CopyTo(bytes, i * FrameBytes);
        }

        return bytes;
    }

    private static void Tag(byte[] mp3, string id, uint frames, uint? bytes = null)
    {
        Encoding.ASCII.GetBytes(id).CopyTo(mp3, TagAt);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(TagAt + 4), bytes is null ? 1u : 3u);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(TagAt + 8), frames);
        if (bytes is { } size)
        {
            BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(TagAt + 12), size);
        }
    }

    private static byte[] Wav(params byte[][] chunks)
    {
        var body = chunks.SelectMany(c => c).ToArray();
        var wav = new byte[12 + body.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(4 + body.Length));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        body.CopyTo(wav, 12);
        return wav;
    }

    private static byte[] FormatBody(int length, ushort code, uint sampleRate, uint byteRate, ushort blockAlign)
    {
        var body = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(body, code);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(12), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(14), 16);
        return body;
    }

    private static byte[] Format(ushort code, uint sampleRate, uint byteRate, ushort blockAlign) =>
        Chunk("fmt ", FormatBody(16, code, sampleRate, byteRate, blockAlign));

    private static byte[] Extensible(ushort subFormat, uint sampleRate, uint byteRate, ushort blockAlign)
    {
        var body = FormatBody(40, 0xFFFE, sampleRate, byteRate, blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(16), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(24), subFormat);
        return Chunk("fmt ", body);
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        var chunk = new byte[8 + body.Length + body.Length % 2];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }
}
