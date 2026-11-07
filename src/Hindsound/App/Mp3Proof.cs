using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.App;

/// <summary>
/// <c>--prove-mp3 folder</c>: one second of a sine, written as an MP3 by <see cref="ClipSaver"/>, the path of a real clip, and
/// the app leaves. No capture, no microphone, no clipboard, no sound. In a trimmed exe NAudio.Lame once failed to load, and
/// every clip then came out as WAV without a word (Hindsound.csproj): the release workflow runs this on the installed exe.
/// </summary>
internal static class Mp3Proof
{
    /// <summary>The MP3 encoder failed, and the clip came out as WAV.</summary>
    public const int ExitFellBackToWav = 4;

    private const int ExitFailed = 3;

    public static int Write(string folder, Journal journal) => Write(folder, journal, openMp3: null);

    /// <param name="openMp3">For the tests of the fallback. Default: LAME, as for a real clip.</param>
    internal static int Write(string folder, Journal journal, Func<string, int, int, IAudioFileWriter>? openMp3)
    {
        try
        {
            var fullFolder = Path.GetFullPath(folder);

            // The rescue folder inside it: a folder that cannot be written fails here, and nothing reaches %LocalAppData%.
            var saver = new ClipSaver(Path.Combine(fullFolder, "rescued"), openMp3: openMp3);
            var options = new ExportOptions { Format = ExportFormat.Mp3, TrackMode = TrackMode.Mix, OutputFolder = fullFolder };
            var result = saver.Save(new AudioSnapshot(Sine(), []), options);

            journal.Write($"prove-mp3: {string.Join(", ", result.Paths)}, {result.SizeBytes} bytes, rescued {result.Rescued}, fell back to WAV {result.FellBackToWav}");
            if (result.FellBackToWav) return ExitFellBackToWav;
            return result.Rescued ? ExitFailed : 0;
        }
        catch (Exception ex)
        {
            journal.Write("prove-mp3 failed: " + ex);
            return ExitFailed;
        }
    }

    /// <summary>440 Hz at -12 dBFS, on both channels of the PC audio track.</summary>
    private static float[] Sine()
    {
        var samples = new float[InternalFormat.SampleRate * InternalFormat.SystemChannels];
        for (var i = 0; i < InternalFormat.SampleRate; i++)
        {
            var value = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * i / InternalFormat.SampleRate));
            for (var c = 0; c < InternalFormat.SystemChannels; c++) samples[i * InternalFormat.SystemChannels + c] = value;
        }

        return samples;
    }
}
