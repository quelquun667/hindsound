using System.Globalization;
using Hindsound.Audio;
using Hindsound.Library;

namespace Hindsound.Linux.Library;

/// <summary>
/// After <c>--prove-mp3</c> on Linux, the MP3 it wrote is read back by the decoder of the preview and the editor, in the
/// program as it is published. The packages are trimmed: a part of NLayer or of the decoder that the trimmer took away would
/// otherwise only show when a user listens to a clip. The check of the packages runs it on both of them.
/// </summary>
internal static class Mp3ReadBack
{
    /// <summary>The MP3 was written but does not read back as the second of 440 Hz the proof wrote.</summary>
    public const int ExitDoesNotReadBack = 5;

    /// <summary>Reads back the newest MP3 of <paramref name="folder"/>: 0, or <see cref="ExitDoesNotReadBack"/>.</summary>
    public static int Check(string folder, IClipDecoder decoder, Action<string> log) => Check(folder, decoder, log, seconds: 1, hz: 440, amplitude: 0.25);

    /// <param name="seconds">How long the sine is.</param>
    /// <param name="hz">Its pitch.</param>
    /// <param name="amplitude">Its peak, on the first channel.</param>
    internal static int Check(string folder, IClipDecoder decoder, Action<string> log, double seconds, double hz, double amplitude)
    {
        try
        {
            var path = new DirectoryInfo(Path.GetFullPath(folder)).EnumerateFiles("*.mp3").MaxBy(file => file.LastWriteTimeUtc)?.FullName;
            if (path is null)
            {
                log("prove-mp3: no MP3 to read back");
                return ExitDoesNotReadBack;
            }

            using var stream = decoder.Open(path);
            var samples = new List<float>();
            var buffer = new float[4096 * stream.Channels];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                samples.AddRange(buffer.AsSpan(0, read));
            }

            var frames = samples.Count / stream.Channels;
            var length = (double)frames / InternalFormat.SampleRate;
            var level = Level(samples, stream.Channels, frames, hz);
            log(string.Create(CultureInfo.InvariantCulture, $"prove-mp3: read back {length:0.000} s, {hz:0} Hz at {level:0.000}"));
            var right = Math.Abs(length - seconds) < 0.05 && Math.Abs(level - amplitude) < amplitude * 0.2;
            return right ? 0 : ExitDoesNotReadBack;
        }
        catch (Exception ex)
        {
            // In full: a member the trimmer took away is named in it.
            log("prove-mp3: read back failed: " + ex);
            return ExitDoesNotReadBack;
        }
    }

    // The peak of the sine at `hz` on the first channel, over the middle half of the sound: away from the encoder's edges.
    private static double Level(List<float> samples, int channels, int frames, double hz)
    {
        var block = frames / 2;
        if (block == 0)
        {
            return 0;
        }

        var start = frames / 4;
        var coefficient = 2 * Math.Cos(2 * Math.PI * hz / InternalFormat.SampleRate);
        double previous = 0, beforePrevious = 0;
        for (var i = 0; i < block; i++)
        {
            var current = samples[(start + i) * channels] + (coefficient * previous) - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = (previous * previous) + (beforePrevious * beforePrevious) - (coefficient * previous * beforePrevious);
        return 2 * Math.Sqrt(Math.Max(power, 0)) / block;
    }
}
