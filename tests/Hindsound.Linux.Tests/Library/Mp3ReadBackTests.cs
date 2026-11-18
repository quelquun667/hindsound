using System.Text;
using Hindsound.Linux.Library;
using Hindsound.Linux.Tests.Export;

namespace Hindsound.Linux.Tests.Library;

/// <summary>The read back of the proof's MP3, which the check of the packages runs in the trimmed program.</summary>
public sealed class Mp3ReadBackTests : IDisposable
{
    private readonly TestFolder _folder = new();
    private readonly LinuxClipDecoder _decoder = new();
    private readonly List<string> _log = [];

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void An_mp3_that_reads_back_as_the_sine_written_passes()
    {
        // The fixture is 0.6 s of 997 Hz at 0.5.
        File.WriteAllBytes(_folder.Combine("sine.mp3"), ClipFixtures.SineStereo48);

        Assert.Equal(0, Mp3ReadBack.Check(Folder, _decoder, _log.Add, seconds: 0.6, hz: 997, amplitude: 0.5));
        Assert.Contains(_log, line => line.StartsWith("prove-mp3: read back 0.6", StringComparison.Ordinal));
    }

    [Fact]
    public void An_mp3_of_another_length_or_pitch_than_the_proof_s_fails()
    {
        File.WriteAllBytes(_folder.Combine("sine.mp3"), ClipFixtures.SineStereo48);

        Assert.Equal(Mp3ReadBack.ExitDoesNotReadBack, Mp3ReadBack.Check(Folder, _decoder, _log.Add));
        Assert.Equal(Mp3ReadBack.ExitDoesNotReadBack, Mp3ReadBack.Check(Folder, _decoder, _log.Add, seconds: 0.6, hz: 440, amplitude: 0.5));
        Assert.Equal(Mp3ReadBack.ExitDoesNotReadBack, Mp3ReadBack.Check(Folder, _decoder, _log.Add, seconds: 1, hz: 997, amplitude: 0.5));
    }

    [Fact]
    public void No_mp3_or_one_that_does_not_decode_fails_and_says_why()
    {
        Assert.Equal(Mp3ReadBack.ExitDoesNotReadBack, Mp3ReadBack.Check(Folder, _decoder, _log.Add));
        Assert.Contains("prove-mp3: no MP3 to read back", _log);

        File.WriteAllBytes(_folder.Combine("notes.mp3"), Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Not a sound. ", 400))));
        Assert.Equal(Mp3ReadBack.ExitDoesNotReadBack, Mp3ReadBack.Check(Folder, _decoder, _log.Add));
        Assert.Contains(_log, line => line.StartsWith("prove-mp3: read back failed: Hindsound.Library.ClipDecodeException", StringComparison.Ordinal));
    }

    private string Folder => Path.GetDirectoryName(_folder.Combine("x"))!;
}
