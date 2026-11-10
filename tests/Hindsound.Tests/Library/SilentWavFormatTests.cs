using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>
/// Zeros are silence only in 16-bit signed PCM, the format the probe writes: in 8-bit PCM a zero is full scale and in mu-law
/// it is nearly so, and a player would click. Which format a file has is told by one fmt chunk, before its data.
/// </summary>
public sealed class SilentWavFormatTests
{
    private static byte[] Chunk(string id, byte[] body)
    {
        List<byte> bytes = [.. System.Text.Encoding.ASCII.GetBytes(id), .. BitConverter.GetBytes((uint)body.Length), .. body];
        if (body.Length % 2 == 1)
        {
            bytes.Add(0);
        }

        return [.. bytes];
    }

    private static byte[] Fmt(ushort tag, ushort bits)
    {
        const int channels = 2, rate = 48000;
        var blockAlign = (ushort)(channels * bits / 8);
        return Chunk("fmt ", [.. BitConverter.GetBytes(tag), .. BitConverter.GetBytes((ushort)channels), .. BitConverter.GetBytes(rate),
            .. BitConverter.GetBytes(rate * blockAlign), .. BitConverter.GetBytes(blockAlign), .. BitConverter.GetBytes(bits)]);
    }

    private static byte[] Zeros() => Chunk("data", new byte[4000]);

    private static byte[] Riff(params byte[][] chunks)
    {
        var body = chunks.SelectMany(chunk => chunk).ToArray();
        return [.. "RIFF"u8, .. BitConverter.GetBytes((uint)(4 + body.Length)), .. "WAVE"u8, .. body];
    }

    [Fact]
    public void Zeros_in_16_bit_pcm_are_silence()
    {
        Assert.True(SilentWav.IsPureSilence(Riff(Fmt(1, 16), Zeros())));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(32)]
    public void Zeros_in_pcm_of_another_width_than_16_bits_are_not_taken_for_silence(ushort bits)
    {
        Assert.False(SilentWav.IsPureSilence(Riff(Fmt(1, bits), Zeros())));
    }

    [Fact]
    public void A_second_format_chunk_is_not_silence_whatever_comes_first()
    {
        // mu-law zeros are almost full scale; the last format said must not be the one believed.
        Assert.False(SilentWav.IsPureSilence(Riff(Fmt(7, 8), Zeros(), Fmt(1, 16))));
        Assert.False(SilentWav.IsPureSilence(Riff(Fmt(1, 16), Zeros(), Fmt(1, 16))));
        Assert.False(SilentWav.IsPureSilence(Riff(Fmt(1, 16), Fmt(1, 16), Zeros())));
    }

    [Fact]
    public void Data_before_the_format_is_not_silence()
    {
        Assert.False(SilentWav.IsPureSilence(Riff(Zeros(), Fmt(1, 16))));
    }

    [Fact]
    public void A_format_chunk_too_short_to_tell_the_width_is_not_silence()
    {
        Assert.False(SilentWav.IsPureSilence(Riff(Chunk("fmt ", [1, 0, 2, 0, 0x80, 0xBB, 0, 0, 0, 0, 0, 0]), Zeros())));
    }
}
