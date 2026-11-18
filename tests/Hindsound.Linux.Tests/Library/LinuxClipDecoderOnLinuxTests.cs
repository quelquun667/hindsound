using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Linux.Export;
using Hindsound.Linux.Library;
using Hindsound.Linux.Tests.Export;
using static Hindsound.Linux.Tests.Library.ClipSignals;

namespace Hindsound.Linux.Tests.Library;

/// <summary>
/// The decoder on what the Linux app really writes: the mp3 of the libmp3lame.so.0 it ships (the CI builds the library and
/// points HINDSOUND_LAME_DIR at its folder; without that variable the one of the system is used), at each bitrate the app
/// offers. The files of the other tests were made by LAME 3.100 on Windows: this checks that the Linux build writes a tag the
/// decoder reads the same way.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class LinuxClipDecoderOnLinuxTests
{
    private static readonly string? BuiltDirectory = Environment.GetEnvironmentVariable("HINDSOUND_LAME_DIR") is { Length: > 0 } dir ? dir : null;

    // The app finds its library next to the program: put it there, as the package will.
    private static void PutLibraryBesideTheProgram()
    {
        if (BuiltDirectory is null) return;
        var target = Path.Combine(AppContext.BaseDirectory, LameLibrary.SystemName);
        if (!File.Exists(target))
            File.Copy(Path.Combine(BuiltDirectory, LameLibrary.SystemName), target);
    }

    private static string WriteMp3(TestFolder folder, string name, float[] samples, int channels, int kbps)
    {
        PutLibraryBesideTheProgram();
        var path = folder.Combine(name);
        var before = PlatformDefaults.Mp3Encoder;
        PlatformDefaults.Mp3Encoder = LameMp3Encoder.Open;
        try
        {
            Encoders.WriteMp3(path, samples, channels, kbps);
        }
        finally
        {
            PlatformDefaults.Mp3Encoder = before;
        }

        return path;
    }

    [Theory]
    [InlineData(2, 128)]
    [InlineData(2, 192)]
    [InlineData(2, 320)]
    [InlineData(1, 128)]
    public void A_clip_the_app_wrote_comes_back_with_its_length_and_its_pitch(int channels, int kbps)
    {
        using var folder = new TestFolder();
        var path = WriteMp3(folder, "tone.mp3", Sine(Rate, channels, 3), channels, kbps);

        using var stream = new LinuxClipDecoder().Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(channels, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(3), stream.Duration);
        Assert.Equal(3 * Rate, samples.Length / channels);

        // The zero crossings of the middle second: 997 Hz is 1994 of them, and the level is that of the sine written.
        var crossings = 0;
        var peak = 0f;
        for (var f = Rate; f < 2 * Rate; f++)
        {
            var value = samples[f * channels];
            peak = Math.Max(peak, Math.Abs(value));
            if (Math.Sign(value) != Math.Sign(samples[(f - 1) * channels]) && value != 0) crossings++;
        }

        Assert.InRange(crossings, 1992, 1996);
        Assert.InRange(peak, Amplitude * 0.95, Amplitude * 1.05);
    }

    [Fact]
    public void A_clip_the_app_wrote_starts_and_ends_on_the_frames_that_were_encoded_and_seeks_to_the_frame()
    {
        using var folder = new TestFolder();
        var reference = Chirp(2);
        var path = WriteMp3(folder, "chirp.mp3", reference, 1, 192);

        using var stream = new LinuxClipDecoder().Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(reference.Length, samples.Length);
        Assert.Equal(0, BestLag(samples, reference, from: 3000, to: 20000));
        Assert.Equal(0, BestLag(samples, reference, from: reference.Length - 20000, to: reference.Length - 3000));

        foreach (var at in new[] { 0.3, 1.0123, 1.9 })
        {
            stream.Seek(TimeSpan.FromSeconds(at));
            var after = new float[960];
            Assert.Equal(960, stream.Read(after));
            var from = (int)Math.Round(at * Rate);
            for (var i = 0; i < after.Length; i++) Assert.Equal(samples[from + i], after[i], 1e-4);
        }
    }
}
