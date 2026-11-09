using System.Buffers.Binary;
using System.IO;
using System.Text;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Tests.Export;
using NAudio.Lame;
using NAudio.Wave;

namespace Hindsound.Tests.Library;

public sealed class AudioFileLengthTests : IDisposable
{
    // MPEG 1 layer III, 128 kbps, 48 kHz, no CRC: 384 bytes a frame, 24 ms a frame.
    private static readonly byte[] Stereo128 = [0xFF, 0xFB, 0x94, 0x00];
    private const int Stereo128FrameBytes = 384;

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private string Path_(string name) => _root.Combine(name);

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path_(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ---------------------------------------------------------------- real files, from the real encoders

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_real_WAV_gives_its_exact_duration(int channels)
    {
        var path = Path_("clip.wav");
        Encoders.WriteWav(path, Signals.Sine(440, 0.3, 3, channels), channels);

        Assert.Equal(3.0, AudioFileLength.Read(path)!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(1, 128)]
    [InlineData(2, 128)]
    [InlineData(1, 320)]
    [InlineData(2, 320)]
    public void A_real_MP3_gives_its_duration_within_the_delay_of_the_encoder(int channels, int kbps)
    {
        var path = Path_("clip.mp3");
        Encoders.WriteMp3(path, Signals.Sine(440, 0.3, 3, channels), channels, kbps);

        Assert.InRange(AudioFileLength.Read(path)!.Value.TotalSeconds, 2.95, 3.1);
    }

    [Fact]
    public void A_real_MP3_is_read_from_its_Info_frame()
    {
        var path = Path_("clip.mp3");
        Encoders.WriteMp3(path, Signals.Sine(440, 0.3, 3, 2), 2, 192);
        var bytes = File.ReadAllBytes(path);

        // Twice as many frames in the tag, and the same file size: only the tag can give 6 s.
        var flagsAt = FindInfo(bytes) + 4;
        Assert.Equal(0x1u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(flagsAt)) & 0x1u);
        var frames = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(flagsAt + 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(flagsAt + 4), frames * 2);
        File.WriteAllBytes(path, bytes);

        Assert.InRange(AudioFileLength.Read(path)!.Value.TotalSeconds, 5.9, 6.2);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(100)]
    [InlineData(2)]
    public void A_real_MP3_cut_short_has_no_known_duration(int divisor)
    {
        var path = Path_("clip.mp3");
        Encoders.WriteMp3(path, Signals.Sine(440, 0.3, 3, 2), 2, 128);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / divisor)]);

        Assert.Null(AudioFileLength.Read(path));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void A_real_WAV_cut_short_has_no_known_duration(int divisor)
    {
        var path = Path_("clip.wav");
        Encoders.WriteWav(path, Signals.Sine(440, 0.3, 3, 2), 2);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / divisor)]);

        Assert.Null(AudioFileLength.Read(path));
    }

    [Theory]
    [InlineData(".wav")]
    [InlineData(".mp3")]
    public void Reserved_then_being_written_then_finished(string extension)
    {
        var path = FileNameTemplate.ReserveUniquePaths(_root.Path, "clip", [""], extension)[0];
        Assert.Null(AudioFileLength.Read(path));

        using (var writer = extension == ".wav" ? Encoders.OpenWav(path, 2) : Encoders.OpenMp3(path, 2, 128))
        {
            writer.Write(Signals.Sine(440, 0.3, 1, 2));
            Assert.Null(AudioFileLength.Read(path));
        }

        Assert.InRange(AudioFileLength.Read(path)!.Value.TotalSeconds, 0.95, 1.1);
    }

    [Fact]
    public void An_MP3_whose_writer_never_finished_gives_what_is_on_disk()
    {
        // A write-only stream leaves the Info frame an empty placeholder, as a writer stopped before its end does.
        var path = Path_("clip.mp3");
        var pcm = new byte[Signals.Rate * 2 * 2];
        Encoders.ToPcm16(Signals.Sine(440, 0.3, 1, 2), pcm);
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new LameMP3FileWriter(file, new WaveFormat(Signals.Rate, 16, 2), 128))
        {
            writer.Write(pcm, 0, pcm.Length);
        }

        Assert.InRange(AudioFileLength.Read(path)!.Value.TotalSeconds, 0.95, 1.1);
    }

    // ---------------------------------------------------------------- WAV, by hand

    [Fact]
    public void A_chunk_before_the_format_is_skipped_with_its_padding()
    {
        var wav = Wav(Chunk("JUNK", new byte[7]), Format(1000), Chunk("LIST", new byte[3]), Chunk("data", new byte[3000]));

        Assert.Equal(3.0, AudioFileLength.Read(WriteBytes("a.wav", wav))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void Data_may_come_before_the_format()
    {
        var wav = Wav(Chunk("data", new byte[3000]), Format(1500));

        Assert.Equal(2.0, AudioFileLength.Read(WriteBytes("a.wav", wav))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_WAV_whose_data_says_more_than_the_file_holds_is_unknown()
    {
        var wav = Wav(Format(1000), ChunkClaiming("data", 3001, new byte[3000]));

        Assert.Null(AudioFileLength.Read(WriteBytes("a.wav", wav)));
    }

    [Fact]
    public void A_WAV_filling_the_file_to_the_last_byte_is_known()
    {
        var wav = Wav(Format(1000), ChunkClaiming("data", 3000, new byte[3000]));

        Assert.Equal(3.0, AudioFileLength.Read(WriteBytes("a.wav", wav))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_WAV_without_data_or_without_format_or_with_a_rate_of_zero_is_unknown()
    {
        Assert.Null(AudioFileLength.Read(WriteBytes("a.wav", Wav(Format(1000)))));
        Assert.Null(AudioFileLength.Read(WriteBytes("b.wav", Wav(Chunk("data", new byte[100])))));
        Assert.Null(AudioFileLength.Read(WriteBytes("c.wav", Wav(Format(0), Chunk("data", new byte[100])))));
        Assert.Null(AudioFileLength.Read(WriteBytes("d.wav", Wav(Format(1000), Chunk("data", [])))));
    }

    [Fact]
    public void A_format_chunk_too_short_is_unknown()
    {
        var wav = Wav(Chunk("fmt ", new byte[8]), Chunk("data", new byte[100]));

        Assert.Null(AudioFileLength.Read(WriteBytes("a.wav", wav)));
    }

    [Fact]
    public void A_WAV_of_endless_empty_chunks_ends_the_search()
    {
        var parts = new List<byte[]>();
        for (var i = 0; i < 100; i++) parts.Add(Chunk("JUNK", []));
        parts.Add(Format(1000));
        parts.Add(Chunk("data", new byte[1000]));

        Assert.Null(AudioFileLength.Read(WriteBytes("a.wav", Wav([.. parts]))));
    }

    // ---------------------------------------------------------------- MP3, by hand

    [Fact]
    public void Without_a_tag_the_size_and_the_bitrate_of_the_first_frame_give_the_duration()
    {
        // 2 500 frames of 24 ms.
        Assert.Equal(60.0, AudioFileLength.Read(WriteBytes("a.mp3", Frames(Stereo128, Stereo128FrameBytes, 2500)))!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x94, 0x00 }, 36, 1000, 24.0)] // MPEG 1, stereo, 48 kHz: 1152 samples a frame
    [InlineData(new byte[] { 0xFF, 0xFB, 0x94, 0xC0 }, 21, 1000, 24.0)] // MPEG 1, mono
    [InlineData(new byte[] { 0xFF, 0xFA, 0x94, 0x00 }, 38, 1000, 24.0)] // MPEG 1, stereo, with a CRC
    [InlineData(new byte[] { 0xFF, 0xF3, 0x84, 0x00 }, 21, 1000, 24.0)] // MPEG 2, stereo, 24 kHz: 576 samples a frame
    [InlineData(new byte[] { 0xFF, 0xF3, 0x84, 0xC0 }, 13, 1000, 24.0)] // MPEG 2, mono
    [InlineData(new byte[] { 0xFF, 0xE3, 0x84, 0x00 }, 21, 500, 24.0)] // MPEG 2.5, stereo, 12 kHz
    public void The_Info_frame_sits_behind_the_side_information(byte[] header, int tagAt, uint frames, double seconds)
    {
        var frameBytes = header[1] == 0xE3 ? 384 : header[1] == 0xF3 ? 192 : Stereo128FrameBytes;
        var mp3 = Frames(header, frameBytes, (int)frames);
        Tag(mp3, tagAt, "Info", frames);

        Assert.Equal(seconds, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_Xing_tag_counts_as_an_Info_tag()
    {
        var mp3 = Frames(Stereo128, Stereo128FrameBytes, 100);
        Tag(mp3, 36, "Xing", 100);

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_tag_without_a_frame_count_leaves_the_size_and_the_bitrate()
    {
        var mp3 = Frames(Stereo128, Stereo128FrameBytes, 100);
        Encoding.ASCII.GetBytes("Info").CopyTo(mp3, 36);

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_tag_claiming_more_bytes_than_the_file_holds_means_a_cut_file()
    {
        var mp3 = Frames(Stereo128, Stereo128FrameBytes, 100);
        Tag(mp3, 36, "Info", 100, bytes: (uint)mp3.Length + 1);
        Assert.Null(AudioFileLength.Read(WriteBytes("a.mp3", mp3)));

        Tag(mp3, 36, "Info", 100, bytes: (uint)mp3.Length);
        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("b.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void The_tags_around_the_sound_do_not_count()
    {
        // An ID3v2 tag as long as a whole picture, and an ID3v1 tag at the end.
        var id3 = new byte[10 + 300_000];
        Encoding.ASCII.GetBytes("ID3").CopyTo(id3, 0);
        id3[3] = 3;
        id3[7] = 300_000 >> 14 & 0x7F;
        id3[8] = 300_000 >> 7 & 0x7F;
        id3[9] = 300_000 & 0x7F;
        var end = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(end, 0);
        var mp3 = id3.Concat(Frames(Stereo128, Stereo128FrameBytes, 100)).Concat(end).ToArray();

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void An_ID3v2_tag_with_a_footer_is_ten_bytes_longer()
    {
        var id3 = new byte[10 + 100 + 10];
        Encoding.ASCII.GetBytes("ID3").CopyTo(id3, 0);
        id3[3] = 4;
        id3[5] = 0x10;
        id3[9] = 100;
        var mp3 = id3.Concat(Frames(Stereo128, Stereo128FrameBytes, 100)).ToArray();

        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void Bytes_that_only_look_like_a_header_are_passed_over()
    {
        // Zeros, then a lone header with no frame behind it, then the real frames.
        var junk = new byte[1000];
        Stereo128.CopyTo(junk, 200);
        var mp3 = junk.Concat(Frames(Stereo128, Stereo128FrameBytes, 100)).ToArray();

        Assert.Equal((mp3.Length - 1000) * 8.0 / 128000, AudioFileLength.Read(WriteBytes("a.mp3", mp3))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_single_whole_frame_is_a_file_and_half_a_frame_is_not()
    {
        var one = Frames(Stereo128, Stereo128FrameBytes, 1);
        Assert.Equal(0.024, AudioFileLength.Read(WriteBytes("a.mp3", one))!.Value.TotalSeconds, 3);
        Assert.Null(AudioFileLength.Read(WriteBytes("b.mp3", one[..(Stereo128FrameBytes / 2)])));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x04, 0x00 })] // free format
    [InlineData(new byte[] { 0xFF, 0xFB, 0xF4, 0x00 })] // forbidden bitrate
    [InlineData(new byte[] { 0xFF, 0xFB, 0x9C, 0x00 })] // reserved sample rate
    [InlineData(new byte[] { 0xFF, 0xEB, 0x94, 0x00 })] // reserved version
    [InlineData(new byte[] { 0xFF, 0xFD, 0x94, 0x00 })] // layer II
    public void A_header_that_gives_no_frame_size_is_not_a_frame(byte[] header)
    {
        Assert.Null(AudioFileLength.Read(WriteBytes("a.mp3", Frames(header, Stereo128FrameBytes, 10))));
    }

    // ---------------------------------------------------------------- false files

    [Theory]
    [InlineData("false.wav")]
    [InlineData("false.mp3")]
    public void Anything_else_under_these_names_is_unknown_without_an_exception(string name)
    {
        Assert.Null(AudioFileLength.Read(WriteBytes(name, [])));
        Assert.Null(AudioFileLength.Read(WriteBytes(name, [1])));
        Assert.Null(AudioFileLength.Read(WriteBytes(name, Encoding.UTF8.GetBytes("Not audio. RIFF WAVE ID3 and a lot of nothing."))));
        Assert.Null(AudioFileLength.Read(WriteBytes(name, Enumerable.Repeat((byte)0xFF, 20_000).ToArray())));
        Assert.Null(AudioFileLength.Read(WriteBytes(name, Random(50_000))));
    }

    [Fact]
    public void A_name_of_another_kind_is_unknown_even_with_the_right_content()
    {
        Assert.Null(AudioFileLength.Read(WriteBytes("a.ogg", Wav(Format(1000), Chunk("data", new byte[100])))));
    }

    [Fact]
    public void An_MP3_named_WAV_and_a_WAV_named_MP3_are_unknown()
    {
        Assert.Null(AudioFileLength.Read(WriteBytes("a.wav", Frames(Stereo128, Stereo128FrameBytes, 10))));
        Assert.Null(AudioFileLength.Read(WriteBytes("a.mp3", Wav(Format(1000), Chunk("data", new byte[100])))));
    }

    [Fact]
    public void An_upper_case_extension_is_the_same()
    {
        Assert.Equal(2.4, AudioFileLength.Read(WriteBytes("A.MP3", Frames(Stereo128, Stereo128FrameBytes, 100)))!.Value.TotalSeconds, 3);
        Assert.Equal(1.0, AudioFileLength.Read(WriteBytes("B.WAV", Wav(Format(1000), Chunk("data", new byte[1000]))))!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void A_file_kept_in_the_cloud_is_not_opened()
    {
        var path = WriteBytes("a.wav", Wav(Format(1000), Chunk("data", new byte[1000])));
        Assert.NotNull(AudioFileLength.Read(path));

        File.SetAttributes(path, FileAttributes.Offline);

        Assert.Null(AudioFileLength.Read(path));
    }

    // ---------------------------------------------------------------- privacy

    [Fact]
    public void A_file_gone_or_a_folder_gone_tells_nothing_and_throws_nothing()
    {
        var journal = new Journal(_root.Combine("gone.log"));

        Assert.Null(AudioFileLength.Read(_root.Combine("Collection-Canary-7F3.wav"), journal.Write));
        Assert.Null(AudioFileLength.Read(_root.Combine("Collection-Canary-7F3", "clip.mp3"), journal.Write));
        Assert.Null(AudioFileLength.Read("Z:\\no-such-folder\\Collection-Canary-7F3\\clip.mp3", journal.Write));

        Assert.False(File.Exists(journal.Path));
    }

    [Theory]
    [InlineData("Collection-Canary-7F3.wav")]
    [InlineData("Collection-Canary-7F3.mp3")]
    public void A_locked_file_tells_the_type_of_the_failure_and_never_its_path(string name)
    {
        var path = WriteBytes(name, Wav(Format(1000), Chunk("data", new byte[1000])));
        var journal = new Journal(_root.Combine("locked.log"));

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(AudioFileLength.Read(path, journal.Write));
        }

        var text = File.ReadAllText(journal.Path);
        Assert.Contains("IOException", text);
        Assert.Contains("0x80070020", text); // ERROR_SHARING_VIOLATION
        Assert.DoesNotContain("Canary", text);
        Assert.DoesNotContain(_root.Path, text);
        Assert.DoesNotContain("being used", text); // the message of the exception
    }

    // ---------------------------------------------------------------- building files

    private static int FindInfo(byte[] bytes)
    {
        var at = bytes.AsSpan().IndexOf("Info"u8);
        Assert.True(at >= 0, "no Info frame");
        return at;
    }

    private static byte[] Frames(byte[] header, int frameBytes, int count)
    {
        var bytes = new byte[frameBytes * count];
        for (var i = 0; i < count; i++)
        {
            header.CopyTo(bytes, i * frameBytes);
        }

        return bytes;
    }

    private static void Tag(byte[] mp3, int at, string id, uint frames, uint? bytes = null)
    {
        Encoding.ASCII.GetBytes(id).CopyTo(mp3, at);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(at + 4), bytes is null ? 1u : 3u);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(at + 8), frames);
        if (bytes is { } size)
        {
            BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(at + 12), size);
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
        var padded = body.Length % 2 == 1 ? body.Concat(new byte[1]).ToArray() : body;
        return ChunkClaiming(id, (uint)body.Length, padded);
    }

    private static byte[] ChunkClaiming(string id, uint size, byte[] body)
    {
        var chunk = new byte[8 + body.Length];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), size);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Random(int count)
    {
        var bytes = new byte[count];
        new Random(7).NextBytes(bytes);
        return bytes;
    }
}
