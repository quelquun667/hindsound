using System.IO;
using Hindsound.Audio;
using Hindsound.Library;

namespace Hindsound.Export;

/// <summary>
/// The sound of an edit: a stretch of the files of a clip, each with its volume, summed when they are mixed, then held under
/// the ceiling by the limiter. Nothing is normalized: a louder edit is only as loud as its volume says. Read and written piece
/// by piece, so a long clip never sits whole in memory.
/// </summary>
internal static class ClipEditRender
{
    /// <summary>Frames read at a time from each file (about 0.17 s).</summary>
    internal const int ChunkFrames = 8192;

    /// <summary>The bitrate of a soundboard copy: 5.2 s at 128 kb/s is about 83 KB, far under <see cref="Soundboard.MaxBytes"/>.</summary>
    internal const int SoundboardBitrateKbps = 128;

    /// <summary>What the file of each format ends with.</summary>
    public static string ExtensionOf(ExportFormat format) => format == ExportFormat.Wav ? ".wav" : ".mp3";

    /// <summary>The format of a file of the library, from its extension: a <c>.wav</c> is a WAV, anything else an mp3.</summary>
    public static ExportFormat FormatOf(string path) =>
        Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Wav : ExportFormat.Mp3;

    /// <summary>A volume in dB, as a factor. Out of the bounds of the editor, it is held at them; anything not a number is 0 dB.</summary>
    public static float Gain(double db) =>
        (float)Math.Pow(10, (double.IsFinite(db) ? Math.Clamp(db, EditLimits.MinGainDb, EditLimits.MaxGainDb) : 0) / 20);

    /// <summary>The ceiling of the settings as a factor, held where the settings hold it (-20 to 0 dB, -1 when it is not a number).</summary>
    public static float Ceiling(double db) => (float)Math.Pow(10, (double.IsFinite(db) ? Math.Clamp(db, -20, 0) : -1) / 20);

    /// <summary>The bitrate of the settings, or the default one if it is not one an mp3 takes.</summary>
    public static int Bitrate(int kbps) => kbps is 128 or 192 or 320 ? kbps : 192;

    /// <summary>The frame at this time, rounded to the nearest one. Never before the start.</summary>
    public static long FrameAt(TimeSpan at) =>
        Math.Max(0, (long)Math.Round(at.Ticks * (double)InternalFormat.SampleRate / TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));

    /// <summary>The frames between two times, the way <see cref="Render"/> counts them.</summary>
    public static long FramesBetween(TimeSpan start, TimeSpan end) => Math.Max(0, FrameAt(end) - FrameAt(start));

    /// <summary>
    /// Writes the stretch from <paramref name="start"/> to <paramref name="end"/> of every source, each with its gain, summed into
    /// <paramref name="channels"/>, through a limiter at <paramref name="ceiling"/>. A mono source goes to both sides of a stereo
    /// output, a stereo source into a mono one as the mean of its sides. A source that ends early is silence from there; the
    /// output ends with the longest. Returns the frames written.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled: what was written so far is the caller's to remove.</exception>
    public static long Render(
        IReadOnlyList<IDecodedStream> sources,
        IReadOnlyList<double> gainsDb,
        TimeSpan start,
        TimeSpan end,
        int channels,
        float ceiling,
        IAudioFileWriter output,
        CancellationToken cancel)
    {
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels));

        var gains = new float[sources.Count];
        var buffers = new float[sources.Count][];
        for (var i = 0; i < sources.Count; i++)
        {
            gains[i] = Gain(i < gainsDb.Count ? gainsDb[i] : 0);
            buffers[i] = new float[ChunkFrames * sources[i].Channels];
            sources[i].Seek(start);
        }

        var limiter = new StreamingLimiter(channels, ceiling);
        var mix = new float[ChunkFrames * channels];
        var ended = new bool[sources.Count];
        var left = FramesBetween(start, end);
        long written = 0;
        while (left > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var want = (int)Math.Min(ChunkFrames, left);
            Array.Clear(mix, 0, want * channels);
            var produced = 0;
            for (var i = 0; i < sources.Count; i++)
            {
                if (ended[i])
                {
                    continue;
                }

                var frames = ReadFrames(sources[i], buffers[i], want);
                ended[i] = frames < want;
                AddInto(mix, channels, buffers[i], sources[i].Channels, frames, gains[i]);
                produced = Math.Max(produced, frames);
            }

            if (produced == 0)
            {
                break;
            }

            left -= produced;
            var emitted = limiter.Process(mix.AsSpan(0, produced * channels));
            output.Write(mix.AsSpan(0, emitted * channels));
            written += emitted;
        }

        // The look-ahead holds the last frames back: without them, the end of the edit would be cut.
        var tail = new float[(limiter.DelayFrames + 1) * channels];
        var flushed = limiter.Flush(tail);
        output.Write(tail.AsSpan(0, flushed * channels));
        return written + flushed;
    }

    /// <summary>Opens a writer for one file of an edit, in its format. The path may be an empty reservation.</summary>
    public static IAudioFileWriter OpenWriter(string path, ExportFormat format, int channels, int bitrateKbps) =>
        format == ExportFormat.Wav ? Encoders.OpenWav(path, channels) : Encoders.OpenMp3(path, channels, Bitrate(bitrateKbps));

    /// <summary>The channels of a mix: mono if every source is, stereo otherwise.</summary>
    public static int MixChannels(IEnumerable<IDecodedStream> sources) => sources.All(source => source.Channels == 1) ? 1 : 2;

    /// <summary>
    /// The bars of the wave of one file, read in one pass: the loudest sample of each slice of its length, on the scale of the
    /// clip waves. Stands in until the shared reading of the editor is wired.
    /// </summary>
    public static IReadOnlyList<float> Peaks(IDecodedStream stream, CancellationToken cancel)
    {
        const int bars = 128;
        var peaks = new float[bars];
        var total = Math.Max(1, FrameAt(stream.Duration));
        var channels = Math.Max(1, stream.Channels);
        var buffer = new float[ChunkFrames * channels];
        long frame = 0;
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++)
            {
                var bar = (int)Math.Min(bars - 1, (frame + i / channels) * bars / total);
                peaks[bar] = Math.Max(peaks[bar], Math.Abs(buffer[i]));
            }

            frame += read / channels;
        }

        return [.. peaks.Select(ClipPeaks.ToBar)];
    }

    private static int ReadFrames(IDecodedStream source, float[] buffer, int want)
    {
        var channels = source.Channels;
        var filled = 0;
        while (filled < want)
        {
            var got = source.Read(buffer.AsSpan(filled * channels, (want - filled) * channels));
            if (got <= 0)
            {
                break;
            }

            filled += got / channels;
        }

        return filled;
    }

    private static void AddInto(float[] mix, int channels, float[] source, int sourceChannels, int frames, float gain)
    {
        for (var f = 0; f < frames; f++)
        {
            if (sourceChannels == channels)
            {
                for (var c = 0; c < channels; c++)
                {
                    mix[f * channels + c] += source[f * channels + c] * gain;
                }
            }
            else if (sourceChannels == 1)
            {
                var sample = source[f] * gain;
                mix[f * 2] += sample;
                mix[f * 2 + 1] += sample;
            }
            else
            {
                mix[f] += (source[f * 2] + source[f * 2 + 1]) * 0.5f * gain;
            }
        }
    }
}
