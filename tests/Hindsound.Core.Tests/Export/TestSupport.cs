using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Tests.Export;

/// <summary>A folder under the system temp folder, removed with everything in it when the test ends.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hindsound-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* a file still open: nothing more to do in a test */ }
    }
}

public static class Signals
{
    public const int Rate = InternalFormat.SampleRate;

    public static double DbToLinear(double db) => Math.Pow(10, db / 20);

    public static double LinearToDb(double linear) => 20 * Math.Log10(linear);

    /// <summary>Interleaved sine, the same on every channel.</summary>
    public static float[] Sine(double hz, double amplitude, double seconds, int channels = 2)
    {
        var frames = (int)Math.Round(seconds * Rate);
        var data = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
            for (var c = 0; c < channels; c++) data[i * channels + c] = value;
        }

        return data;
    }

    public static float[] Constant(float value, int frames, int channels = 2)
    {
        var data = new float[frames * channels];
        Array.Fill(data, value);
        return data;
    }

    public static float Peak(IEnumerable<float> samples) => samples.Select(Math.Abs).DefaultIfEmpty(0).Max();

    public static double PeakDb(IEnumerable<float> samples) => LinearToDb(Peak(samples));

    /// <summary>Reads a 16-bit WAV file back as floats in [-1, 1], with its format.</summary>
    public static (float[] Samples, WaveFormat Format, TimeSpan Duration) ReadWav(string path)
    {
        using var reader = new WaveFileReader(path);
        var bytes = new byte[reader.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = reader.Read(bytes, read, bytes.Length - read);
            if (n == 0) break;
            read += n;
        }

        var samples = new float[read / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, 2 * i) / 32767f;
        return (samples, reader.WaveFormat, reader.TotalTime);
    }

    /// <summary>Reads a 16-bit WAV file back as raw integers.</summary>
    public static short[] ReadWavRaw(string path)
    {
        using var reader = new WaveFileReader(path);
        var bytes = new byte[reader.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = reader.Read(bytes, read, bytes.Length - read);
            if (n == 0) break;
            read += n;
        }

        var samples = new short[read / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, 2 * i);
        return samples;
    }
}
