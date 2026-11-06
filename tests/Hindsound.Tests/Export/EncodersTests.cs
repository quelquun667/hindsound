using Hindsound.Export;
using NAudio.Wave;

namespace Hindsound.Tests.Export;

public class EncodersTests
{
    // ---------------------------------------------------------------- float to int16

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 32767)]
    [InlineData(-1f, -32767)]
    [InlineData(0.5f, 16384)]       // 16383.5 rounds away from zero
    [InlineData(-0.5f, -16384)]
    [InlineData(1.5f, 32767)]       // clipped, not wrapped
    [InlineData(-3f, -32767)]
    [InlineData(float.PositiveInfinity, 32767)]
    [InlineData(float.NegativeInfinity, -32767)]
    [InlineData(float.NaN, 0)]
    [InlineData(1e-6f, 0)]          // under half a step
    public void Float_to_int16_scales_rounds_and_clips(float input, int expected)
    {
        Assert.Equal((short)expected, Encoders.ToInt16(input));
    }

    [Fact]
    public void Pcm_bytes_are_little_endian()
    {
        var bytes = new byte[4];

        Encoders.ToPcm16([1f, -1f], bytes);

        Assert.Equal(new byte[] { 0xFF, 0x7F, 0x01, 0x80 }, bytes); // 32767 and -32767
    }

    // ---------------------------------------------------------------- WAV

    [Fact]
    public void Wav_is_read_back_with_the_same_rate_channels_duration_and_samples()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("a.wav");
        var audio = Signals.Sine(440, 0.7, 1.5);
        audio[10] = 1.4f; // beyond full scale: must be clipped in the file

        Encoders.WriteWav(path, audio, 2);

        var raw = Signals.ReadWavRaw(path);
        using (var reader = new WaveFileReader(path))
        {
            Assert.Equal(48000, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
            Assert.Equal(TimeSpan.FromSeconds(1.5), reader.TotalTime);
        }

        Assert.Equal(audio.Length, raw.Length);
        for (var i = 0; i < audio.Length; i++)
        {
            // Reference computed apart: round(clamp(x) * 32767).
            var expected = (short)Math.Round(Math.Clamp((double)audio[i], -1, 1) * 32767, MidpointRounding.AwayFromZero);
            Assert.Equal(expected, raw[i]);
        }

        Assert.Equal(32767, raw[10]);
    }

    [Fact]
    public void Mono_wav_has_one_channel()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("mono.wav");

        Encoders.WriteWav(path, Signals.Sine(440, 0.5, 1, channels: 1), 1);

        var (samples, format, duration) = Signals.ReadWav(path);
        Assert.Equal(1, format.Channels);
        Assert.Equal(48000, samples.Length);
        Assert.Equal(TimeSpan.FromSeconds(1), duration);
    }

    [Fact]
    public void A_file_that_exists_is_never_overwritten()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("a.wav");
        File.WriteAllText(path, "mine");

        Assert.ThrowsAny<IOException>(() => Encoders.WriteWav(path, new float[2], 2));
        Assert.Equal("mine", File.ReadAllText(path));
    }

    // ---------------------------------------------------------------- MP3

    [Fact]
    public void Mp3_starts_with_a_filled_lame_header_not_an_empty_frame()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("a.mp3");

        Encoders.WriteMp3(path, Signals.Sine(440, 0.5, 3), 2, 192);

        var head = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 400);
        Assert.Contains("Info", head); // CBR file: the header frame is tagged "Info"
        Assert.Contains("LAME", head);
        using var reader = new Mp3FileReader(path);
        // 3.048 s before (the empty placeholder frame counted as audio); one encoder delay frame remains.
        Assert.InRange(reader.TotalTime.TotalSeconds, 3.0, 3.03);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(320)]
    public void Mp3_written_with_lame_is_decoded_with_the_right_duration_bitrate_and_level(int kbps)
    {
        using var folder = new TempFolder();
        var path = folder.Combine("a.mp3");
        const double amplitude = 0.5;

        Encoders.WriteMp3(path, Signals.Sine(440, amplitude, 3), 2, kbps);

        using var reader = new Mp3FileReader(path);
        Assert.Equal(48000, reader.WaveFormat.SampleRate);
        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal(kbps * 1000, reader.Mp3WaveFormat.AverageBytesPerSecond * 8);
        Assert.InRange(reader.TotalTime.TotalSeconds, 2.95, 3.1);

        // The decoded signal is the sine that went in: same RMS within 10 % (0.9 dB). Measured: 0.95 at 128 kbps, 0.97 at 192.
        // A lossy encoder gives up a little energy on a pure tone; a wrong level or a wrong signal would be far off.
        var pcm = new byte[(int)reader.Length];
        var read = 0;
        int n;
        while (read < pcm.Length && (n = reader.Read(pcm, read, pcm.Length - read)) > 0) read += n;
        var sumSquares = 0.0;
        var count = read / 2;
        for (var i = 0; i < count; i++)
        {
            var v = BitConverter.ToInt16(pcm, 2 * i) / 32768.0;
            sumSquares += v * v;
        }

        var rms = Math.Sqrt(sumSquares / count);
        Assert.InRange(rms / (amplitude / Math.Sqrt(2)), 0.90, 1.05);

        // Constant bitrate: the size follows the rate (3 s), within 5 %.
        Assert.InRange(new FileInfo(path).Length / (kbps * 1000 / 8.0 * 3), 0.95, 1.05);
    }

    [Fact]
    public void Mono_mp3_is_decoded_as_mono()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("mono.mp3");

        Encoders.WriteMp3(path, Signals.Sine(440, 0.5, 2, channels: 1), 1, 128);

        using var reader = new Mp3FileReader(path);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.InRange(reader.TotalTime.TotalSeconds, 1.95, 2.1);
    }

    [Fact]
    public void An_unsupported_bitrate_is_refused_before_anything_is_written()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("a.mp3");

        Assert.Throws<ArgumentOutOfRangeException>(() => Encoders.WriteMp3(path, new float[2], 2, 256));
        Assert.False(File.Exists(path));
    }
}
