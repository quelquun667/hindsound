using System.Buffers.Binary;
using System.IO;
using System.Text;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>
/// The guards of the header reading that a file at 48 kHz without padding never meets: the other sample rates, the fields
/// of the Info frame one by one, the tags that only look like tags, and the sharing of the file with its writer.
/// </summary>
public sealed class AudioFileLengthGuardsTests : IDisposable
{
    // MPEG 1 layer III, 128 kbps, no CRC, stereo. 48 kHz: 384 bytes a frame. 44.1 kHz: 417 bytes, or 418 with the padding bit.
    private static readonly byte[] Stereo128At48 = [0xFF, 0xFB, 0x94, 0x00];
    private static readonly byte[] Stereo128At44 = [0xFF, 0xFB, 0x90, 0x00];
    private static readonly byte[] Stereo128At44Padded = [0xFF, 0xFB, 0x92, 0x00];

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = _root.Combine(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ---------------------------------------------------------------- the frame size

    [Fact]
    public void Frames_at_44_1_kHz_alternate_between_417_and_418_bytes()
    {
        // The padding bit adds a byte to the frame: without it, the header that follows a padded frame is looked for a byte too early.
        var parts = new List<byte[]>();
        for (var i = 0; i < 100; i++)
        {
            parts.Add(Frame(i % 2 == 0 ? Stereo128At44Padded : Stereo128At44, i % 2 == 0 ? 418 : 417));
        }

        var mp3 = parts.SelectMany(p => p).ToArray();

        Assert.Equal(50 * 418 + 50 * 417, mp3.Length);
        Assert.Equal(mp3.Length * 8.0 / 128000, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 4);
    }

    [Fact]
    public void A_header_followed_by_one_of_another_sample_rate_is_not_the_first_frame()
    {
        // 384 bytes that look like a frame at 48 kHz, then the real frames at 44.1 kHz.
        var mp3 = Frame(Stereo128At48, 384).Concat(Frames(Stereo128At44, 417, 100)).ToArray();

        Assert.Equal(100 * 417 * 8.0 / 128000, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 4);
    }

    [Fact]
    public void A_header_followed_by_one_of_another_version_is_not_the_first_frame()
    {
        // MPEG 2 at 24 kHz, 64 kbps: 192 bytes a frame.
        byte[] mpeg2 = [0xFF, 0xF3, 0x84, 0x00];
        var mp3 = Frame(Stereo128At48, 384).Concat(Frames(mpeg2, 192, 100)).ToArray();

        Assert.Equal(100 * 192 * 8.0 / 64000, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 4);
    }

    // ---------------------------------------------------------------- the fields of the Info frame

    [Fact]
    public void A_table_of_contents_without_the_size_flag_is_not_read_as_a_size()
    {
        // Frames and table of contents, no size: what a variable bitrate encoder writes. The table is bytes of 0xFF here,
        // which as a size would be a file longer than the one on disk.
        var mp3 = Frames(Stereo128At48, 384, 100);
        Xing(mp3, 36, 1 | 4, [.. Be(100), .. Enumerable.Repeat((byte)0xFF, 100)]);

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_size_without_the_frames_flag_is_not_read_as_a_number_of_frames()
    {
        var mp3 = Frames(Stereo128At48, 384, 100);
        Xing(mp3, 36, 2, Be((uint)mp3.Length));

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    // ---------------------------------------------------------------- the tags

    [Fact]
    public void A_size_that_is_not_in_7_bit_bytes_is_not_an_ID3v2_tag()
    {
        // The last byte of the size has its top bit set: not a tag. Read as one, its size would end in the middle of a frame.
        var id3 = new byte[10];
        Encoding.ASCII.GetBytes("ID3").CopyTo(id3, 0);
        id3[3] = 3;
        id3[9] = 0x85;
        var mp3 = id3.Concat(Frames(Stereo128At48, 384, 100)).ToArray();

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 4);
    }

    [Fact]
    public void The_footer_of_an_ID3v2_tag_is_skipped_even_when_it_looks_like_a_frame()
    {
        // The footer holds a header at 44.1 kHz and 40 kbps, whose 130 bytes end exactly where the file ends:
        // a frame the search would take, were the footer not skipped. The real frame behind it is 120 bytes at 48 kHz.
        var id3 = new byte[10 + 100];
        Encoding.ASCII.GetBytes("ID3").CopyTo(id3, 0);
        id3[3] = 4;
        id3[5] = 0x10;
        id3[9] = 100;
        byte[] footer = [0xFF, 0xFB, 0x20, 0x00, 0, 0, 0, 0, 0, 0];
        byte[] real = [0xFF, 0xFB, 0x24, 0x00];
        var mp3 = id3.Concat(footer).Concat(Frame(real, 120)).ToArray();

        Assert.Equal(120 * 8.0 / 40000, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(127)]
    public void A_file_shorter_than_an_ID3v1_tag_is_unknown_and_tells_nothing(int length)
    {
        var journal = new Journal(_root.Combine("short.log"));

        Assert.Null(AudioFileLength.Read(WriteBytes("short.mp3", new byte[length]), journal.Write));

        Assert.False(File.Exists(journal.Path));
    }

    // ---------------------------------------------------------------- WAV

    [Fact]
    public void A_RIFF_file_that_is_not_a_WAVE_is_unknown()
    {
        var wav = Wav(Format(1000), Chunk("data", new byte[1000]));
        Assert.Equal(1.0, AudioFileLength.Read(WriteBytes("good.wav", wav))!.Value.TotalSeconds, 3);

        Encoding.ASCII.GetBytes("AVI ").CopyTo(wav, 8);

        Assert.Null(AudioFileLength.Read(WriteBytes("avi.wav", wav)));
    }

    // ---------------------------------------------------------------- sharing

    [Theory]
    [InlineData("a.wav")]
    [InlineData("a.mp3")]
    public void A_file_another_program_is_writing_to_is_read(string name)
    {
        var path = WriteBytes(name, name.EndsWith(".wav", StringComparison.Ordinal)
            ? Wav(Format(1000), Chunk("data", new byte[1000]))
            : Frames(Stereo128At48, 384, 100));

        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.NotNull(AudioFileLength.Read(path));
    }

    [Fact]
    public void A_file_another_program_may_delete_or_rename_is_read()
    {
        // Opened with the right to delete: the look must leave that right to whoever has it, or the file could no longer be removed.
        var path = WriteBytes("a.wav", Wav(Format(1000), Chunk("data", new byte[1000])));

        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.DeleteOnClose);

        Assert.NotNull(AudioFileLength.Read(path));
    }

    // ---------------------------------------------------------------- building files

    private static byte[] Be(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static void Xing(byte[] mp3, int at, uint flags, byte[] fields)
    {
        Encoding.ASCII.GetBytes("Xing").CopyTo(mp3, at);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(at + 4), flags);
        fields.CopyTo(mp3, at + 8);
    }

    private static byte[] Frame(byte[] header, int frameBytes)
    {
        var bytes = new byte[frameBytes];
        header.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Frames(byte[] header, int frameBytes, int count) =>
        Enumerable.Range(0, count).SelectMany(_ => Frame(header, frameBytes)).ToArray();

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

    private static byte[] Format(uint bytesPerSecond)
    {
        var body = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(body, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), bytesPerSecond);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), bytesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(14), 8);
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
