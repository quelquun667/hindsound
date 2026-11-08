using System.Buffers.Binary;
using System.IO;
using System.Text;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Tests.Export;
using NAudio.Lame;
using NAudio.Wave;

namespace Hindsound.Tests.Library;

/// <summary>A file made by another program carries tags, and the size in its Info frame counts the sound alone.</summary>
public sealed class AudioFileLengthTagsTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    // A real encoding, with tags. A picture (the data only has to start like a JPEG) makes LAME write an ID3v2 tag at the
    // start; without one it writes an ID3v1 tag at the end.
    private string WriteTagged(string name, bool picture)
    {
        var path = _root.Combine(name);
        var pcm = new byte[Signals.Rate * 2 * 2 * 3];
        Encoders.ToPcm16(Signals.Sine(440, 0.3, 3, 2), pcm);
        var tag = new ID3TagData { Title = "Fake title", Artist = "Fake artist" };
        if (picture)
        {
            tag.AlbumArt = new byte[300_000];
            tag.AlbumArt[0] = 0xFF;
            tag.AlbumArt[1] = 0xD8;
            tag.AlbumArt[2] = 0xFF;
            tag.AlbumArt[3] = 0xE0;
        }

        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using (var writer = new LameMP3FileWriter(file, new WaveFormat(Signals.Rate, 16, 2), 128, tag))
        {
            writer.Write(pcm, 0, pcm.Length);
        }

        return path;
    }

    [Fact]
    public void A_real_MP3_with_a_picture_has_the_size_of_its_sound_in_the_Info_frame()
    {
        // The fact the check rests on: the Info frame of LAME does not count the ID3v2 tag.
        var bytes = File.ReadAllBytes(WriteTagged("a.mp3", picture: true));
        var tagEnd = 10 + ((bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9]);
        Assert.True(tagEnd > 300_000);
        var info = bytes.AsSpan().IndexOf("Info"u8);

        Assert.Equal((uint)(bytes.Length - tagEnd), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(info + 12)));
    }

    [Theory]
    [InlineData(true)] // an ID3v2 tag at the start
    [InlineData(false)] // an ID3v1 tag at the end
    public void A_real_MP3_with_tags_is_known(bool picture)
    {
        Assert.InRange(AudioFileLength.Read(WriteTagged("a.mp3", picture))!.Value.TotalSeconds, 2.95, 3.1);
    }

    [Theory]
    [InlineData(40_000)]
    [InlineData(20_000)]
    [InlineData(1_000)]
    public void A_real_MP3_with_a_picture_cut_short_has_no_known_duration(int cut)
    {
        var path = WriteTagged("a.mp3", picture: true);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length - cut)]);

        // The sound is about 48 000 bytes, the picture 300 000: the file stays far longer than the size in the tag.
        Assert.Null(AudioFileLength.Read(path));
    }

    [Fact]
    public void A_cut_file_behind_a_long_ID3v2_tag_is_unknown_by_hand()
    {
        byte[] header = [0xFF, 0xFB, 0x94, 0x00];
        var mp3 = new byte[384 * 100];
        for (var i = 0; i < 100; i++) header.CopyTo(mp3, i * 384);
        Encoding.ASCII.GetBytes("Info").CopyTo(mp3, 36);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(40), 3);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(44), 100);
        BinaryPrimitives.WriteUInt32BigEndian(mp3.AsSpan(48), (uint)mp3.Length);

        var id3 = new byte[10 + 300_000];
        Encoding.ASCII.GetBytes("ID3").CopyTo(id3, 0);
        id3[3] = 3;
        id3[7] = 300_000 >> 14 & 0x7F;
        id3[8] = 300_000 >> 7 & 0x7F;
        id3[9] = 300_000 & 0x7F;
        var whole = id3.Concat(mp3).ToArray();

        var good = _root.Combine("good.mp3");
        File.WriteAllBytes(good, whole);
        Assert.Equal(2.4, AudioFileLength.Read(good)!.Value.TotalSeconds, 3);

        var cut = _root.Combine("cut.mp3");
        File.WriteAllBytes(cut, whole[..(whole.Length - 5_000)]);
        Assert.Null(AudioFileLength.Read(cut));
    }
}
