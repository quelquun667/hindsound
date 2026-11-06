using Hindsound.Export;
using NAudio.Wave;

namespace Hindsound.Tests.Export;

public class EncodersMp3Tests
{
    private static float[] Sine(double hz, double amplitude, double seconds, int channels)
    {
        var frames = (int)(seconds * 48000);
        var data = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / 48000));
            for (var c = 0; c < channels; c++) data[i * channels + c] = v;
        }
        return data;
    }

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(320)]
    public void Mp3_written_with_lame_is_read_back_with_the_right_duration_and_bitrate(int kbps)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hindsound-test-{Guid.NewGuid():N}.mp3");
        try
        {
            Encoders.WriteMp3(path, Sine(440, 0.5, 3, 2), 2, kbps);

            using var reader = new Mp3FileReader(path);
            Assert.Equal(48000, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.InRange(reader.TotalTime.TotalSeconds, 2.95, 3.1);
            Assert.Equal(kbps * 1000, reader.Mp3WaveFormat.AverageBytesPerSecond * 8);
        }
        finally { File.Delete(path); }
    }
}
