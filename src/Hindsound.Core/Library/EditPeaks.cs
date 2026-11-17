using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Library;

/// <summary>The wave the editor draws for one file: the peak of each equal slice of its length.</summary>
internal static class EditPeaks
{
    public const int BarCount = 128;

    /// <summary>Frames read at a time: about 0.17 s of stereo, so a clip of 30 minutes never sits in memory.</summary>
    private const int ChunkFrames = 8192;

    /// <summary>
    /// Reads the stream from where it stands to its end (a stream just opened stands at its start). Bar i is the highest
    /// absolute sample of the i-th equal slice of <see cref="IDecodedStream.Duration"/>, on the scale of
    /// <see cref="ClipPeaks.ToBar"/> (-60 dBFS is 0, 0 dBFS is 1). Frames past the announced length count in the last
    /// bar; a stream that announces no length gives empty bars and is not read.
    /// </summary>
    /// <exception cref="ClipDecodeException">The file can't be read to its end.</exception>
    /// <exception cref="OperationCanceledException">Asked to stop: checked between two reads.</exception>
    public static IReadOnlyList<float> Compute(IDecodedStream stream, int bars = BarCount, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bars);

        var peaks = new float[bars];
        var channels = stream.Channels;
        var totalFrames = (long)Math.Round(stream.Duration.TotalSeconds * InternalFormat.SampleRate);
        if (totalFrames <= 0 || channels <= 0) return peaks;

        var chunk = new float[ChunkFrames * channels];
        long frame = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var read = stream.Read(chunk);
            if (read <= 0) break;

            var frames = read / channels;
            for (var f = 0; f < frames; f++, frame++)
            {
                var bar = (int)Math.Min(frame * bars / totalFrames, bars - 1);
                var at = f * channels;
                for (var c = 0; c < channels; c++)
                {
                    var value = Math.Abs(chunk[at + c]);
                    if (value > peaks[bar]) peaks[bar] = value;
                }
            }
        }

        for (var i = 0; i < bars; i++) peaks[i] = ClipPeaks.ToBar(peaks[i]);
        return peaks;
    }
}
