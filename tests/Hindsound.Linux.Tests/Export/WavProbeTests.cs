using System.Buffers.Binary;
using Hindsound.Export;

namespace Hindsound.Linux.Tests.Export;

/// <summary>The WAV reader and the pitch measure that the tests of the MP3 on Linux rely on; they run here, on a known signal.</summary>
public sealed class WavProbeTests
{
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 440)]
    public void A_wav_written_by_the_app_is_read_back_and_its_pitch_measured(int channels, int hertz)
    {
        using var folder = new TestFolder();
        var path = folder.Combine("tone.wav");
        Encoders.WriteWav(path, TestSignals.Sine(channels, hertz, 0.5, seconds: 1), channels);

        var wav = WavProbe.Read(File.ReadAllBytes(path));

        Assert.Equal(48000, wav.SampleRate);
        Assert.Equal(channels, wav.Channels);
        Assert.Equal(1.0, wav.Seconds, 3);
        Assert.InRange(WavProbe.Frequency(wav.Channel(0), wav.SampleRate), hertz - 5, hertz + 5);
    }

    [Fact]
    public void A_data_chunk_that_claims_more_than_the_file_holds_is_cut_to_the_file()
    {
        var file = new byte[44 + 8];
        "RIFF"u8.CopyTo(file);
        "WAVE"u8.CopyTo(file.AsSpan(8));
        "fmt "u8.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(22), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), 48000);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(34), 16);
        "data"u8.CopyTo(file.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40), uint.MaxValue);

        var wav = WavProbe.Read(file);

        Assert.Equal(2, wav.Frames); // 8 bytes of samples: two stereo frames
    }

    [Fact]
    public void Something_that_is_not_a_wav_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => WavProbe.Read(new byte[100]));
    }
}
