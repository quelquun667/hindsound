using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Linux.Export;

namespace Hindsound.Linux.Tests.Export;

/// <summary>
/// The MP3 of the Linux app with the real libmp3lame.so.0. The CI builds the library and the lame command with
/// tools/release/build-lame-linux.sh and points HINDSOUND_LAME_DIR at the folder that holds them. Without that variable the
/// system's own libmp3lame.so.0 and the lame found on the PATH are used, so that a developer can run the tests with the
/// packages of the distribution (the version check is then left out).
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class LameMp3OnLinuxTests
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

    private static string LameCommand => BuiltDirectory is null ? "lame" : Path.Combine(BuiltDirectory, "lame");

    private static byte[] DecodeWithLame(string mp3, string wav)
    {
        var start = new ProcessStartInfo(LameCommand) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--decode");
        start.ArgumentList.Add(mp3);
        start.ArgumentList.Add(wav);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("lame did not start.");
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), "lame --decode did not finish in 30 s.");
        Assert.True(process.ExitCode == 0, "lame --decode failed: " + errors.Result + output.Result);
        return File.ReadAllBytes(wav);
    }

    [Theory]
    [InlineData(2, 128)]
    [InlineData(2, 320)]
    [InlineData(1, 192)]
    public void A_second_of_1_kHz_is_an_mp3_that_decodes_to_1_kHz(int channels, int kbps)
    {
        PutLibraryBesideTheProgram();
        using var folder = new TestFolder();
        var mp3 = folder.Combine("tone.mp3");
        var before = PlatformDefaults.Mp3Encoder;
        PlatformDefaults.Mp3Encoder = LameMp3Encoder.Open;
        try
        {
            Encoders.WriteMp3(mp3, TestSignals.Sine(channels, 1000, 0.5, seconds: 1), channels, kbps);
        }
        finally
        {
            PlatformDefaults.Mp3Encoder = before;
        }

        var wav = WavProbe.Read(DecodeWithLame(mp3, folder.Combine("tone.wav")));

        Assert.Equal(48000, wav.SampleRate);
        Assert.Equal(channels, wav.Channels);
        Assert.InRange(wav.Seconds, 0.95, 1.1);
        for (var channel = 0; channel < channels; channel++)
            Assert.InRange(WavProbe.Frequency(wav.Channel(channel), wav.SampleRate), 990, 1010);
        // Constant bitrate: the size follows the bitrate (a second is kbps * 125 bytes, plus the header frame and the last frame).
        Assert.InRange(new FileInfo(mp3).Length, kbps * 125L, kbps * 125L * 1.25);
    }

    [Fact]
    public void The_library_is_taken_from_next_to_the_program_before_the_system()
    {
        PutLibraryBesideTheProgram();
        if (BuiltDirectory is null) return;

        var lame = LameLibrary.Shared;

        Assert.NotNull(lame);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, LameLibrary.SystemName), lame.LoadedFrom);
        Assert.Equal("3.100", lame.Version);
    }

    [Fact]
    public void A_library_missing_everywhere_is_reported_and_the_encoder_refuses()
    {
        var missing = new[] { Path.Combine(Path.GetTempPath(), "hindsound-no-such-dir", "libmp3lame.so.0"), "libmp3lame-hindsound-absent.so.9" };

        var ex = Assert.Throws<DllNotFoundException>(() => LameLibrary.Load(missing));

        Assert.Contains("no such file", ex.Message);
        Assert.Contains("cannot be loaded", ex.Message);
    }

    [Fact]
    public void A_file_that_is_not_a_library_is_skipped()
    {
        using var folder = new TestFolder();
        var notALibrary = folder.Combine("libmp3lame.so.0");
        File.WriteAllText(notALibrary, "not an ELF file");

        var ex = Assert.Throws<DllNotFoundException>(() => LameLibrary.Load([notALibrary]));

        Assert.Contains("cannot be loaded", ex.Message);
    }
}
