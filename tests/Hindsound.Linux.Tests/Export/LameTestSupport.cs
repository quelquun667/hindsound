using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Linux.Tests.Export;

/// <summary>A folder of its own under the temporary one, removed when the test is done.</summary>
internal sealed class TestFolder : IDisposable
{
    private readonly string _path = Directory.CreateTempSubdirectory("hindsound-lame-").FullName;

    public string Combine(string name) => Path.Combine(_path, name);

    public void Dispose()
    {
        try { Directory.Delete(_path, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

/// <summary>Where the DLL of Windows is, for the tests that run the binding against it.</summary>
internal static class WindowsLame
{
    /// <summary>The 64-bit libmp3lame.dll of the NAudio.Lame package, or null off Windows.</summary>
    public static string? Find()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var beside = Path.Combine(AppContext.BaseDirectory, "libmp3lame.64.dll");
        if (File.Exists(beside))
            return beside;

        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var found = Directory.Exists(Path.Combine(packages, "naudio.lame"))
            ? Directory.GetFiles(Path.Combine(packages, "naudio.lame"), "libmp3lame.64.dll", SearchOption.AllDirectories)
            : [];
        return found.Length > 0
            ? found[0]
            : throw new FileNotFoundException("libmp3lame.64.dll of the NAudio.Lame package is neither beside the tests nor in " + packages);
    }
}

internal static class TestSignals
{
    /// <summary>
    /// A few seconds of something with more in it than one tone: three partials, a slow tremolo and a different mix on each
    /// side, so that joint stereo and the psychoacoustic model have something to decide. Interleaved if stereo.
    /// </summary>
    public static float[] Music(int channels, double seconds)
    {
        var frames = (int)(seconds * InternalFormat.SampleRate);
        var samples = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / InternalFormat.SampleRate;
            var tremolo = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 3 * t);
            var left = tremolo * (0.35 * Math.Sin(2 * Math.PI * 220 * t) + 0.2 * Math.Sin(2 * Math.PI * 1760 * t));
            var right = tremolo * (0.35 * Math.Sin(2 * Math.PI * 220 * t) + 0.2 * Math.Sin(2 * Math.PI * 3520 * t + 0.5) + 0.1 * Math.Sin(2 * Math.PI * 7000 * t));
            samples[i * channels] = (float)left;
            if (channels == 2)
                samples[i * channels + 1] = (float)right;
        }

        return samples;
    }

    /// <summary>A pure tone, one second or so, interleaved if stereo.</summary>
    public static float[] Sine(int channels, double hertz, double amplitude, double seconds)
    {
        var frames = (int)(seconds * InternalFormat.SampleRate);
        var samples = new float[frames * channels];
        for (var i = 0; i < frames; i++)
            for (var c = 0; c < channels; c++)
                samples[i * channels + c] = (float)(amplitude * Math.Sin(2 * Math.PI * hertz * i / InternalFormat.SampleRate));
        return samples;
    }

    /// <summary>The 16-bit little-endian bytes the encoder takes.</summary>
    public static byte[] Pcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Encoders.ToPcm16(samples, bytes);
        return bytes;
    }
}
