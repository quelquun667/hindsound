using System.Buffers.Binary;
using System.IO;
using System.Text;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>A WAV cut inside its format chunk gives no duration, and costs the journal nothing: no exception is needed to know it.</summary>
public sealed class AudioFileLengthTruncatedFormatTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    /// <summary>A WAV whose format chunk claims <paramref name="claimed"/> bytes and holds only <paramref name="present"/> of them.</summary>
    private static byte[] CutAt(ushort code, int claimed, int present)
    {
        var wav = new byte[12 + 8 + present];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(wav.Length - 8));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), (uint)claimed);
        var body = wav.AsSpan(20);
        BinaryPrimitives.WriteUInt16LittleEndian(body, code);
        BinaryPrimitives.WriteUInt16LittleEndian(body[2..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body[4..], 1000);
        return wav;
    }

    private void AssertNoDurationAndNothingTold(byte[] wav)
    {
        var path = _root.Combine("cut.wav");
        File.WriteAllBytes(path, wav);
        var journal = new List<string>();

        Assert.Null(AudioFileLength.Read(path, journal.Add));
        Assert.Empty(journal);
    }

    [Fact]
    public void A_format_chunk_cut_short_gives_no_duration_and_tells_nothing()
    {
        // 16 bytes claimed, 10 present: the byte rate is not in the file.
        AssertNoDurationAndNothingTold(CutAt(code: 1, claimed: 16, present: 10));
    }

    [Fact]
    public void An_extensible_format_chunk_cut_before_its_sub_format_gives_no_duration_and_tells_nothing()
    {
        // 40 bytes claimed, 20 present: the sub-format is not in the file.
        AssertNoDurationAndNothingTold(CutAt(code: 0xFFFE, claimed: 40, present: 20));
    }
}
