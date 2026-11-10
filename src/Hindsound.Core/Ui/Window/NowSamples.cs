using Hindsound.Audio;

namespace Hindsound.Ui.Window;

/// <summary>
/// Made-up audio for the pictures of the Now page: a fixed seed, no real sound anywhere. It goes through a real track and the real
/// lanes, so the pictures show what the code makes of samples, not values set by hand.
/// </summary>
internal static class NowSamples
{
    public const int MemoryFrames = 300 * InternalFormat.SampleRate;
    public const int Slices = MemoryFrames / WaveLane.FramesPerSlice;

    private static float Db(double db) => (float)Math.Pow(10, db / 20);

    private static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    /// <summary>A real track holding these peaks, one per slice, the first at the origin.</summary>
    public static SourceTrack Track(ReadOnlySpan<float> peaks, long origin = 0)
    {
        var track = new SourceTrack(1, peaks.Length * WaveLane.FramesPerSlice, origin);
        var packet = new float[WaveLane.FramesPerSlice];
        for (var i = 0; i < peaks.Length; i++)
        {
            packet[WaveLane.FramesPerSlice / 2] = peaks[i];
            track.Append(origin + (long)i * WaveLane.FramesPerSlice, packet, exact: true);
        }

        return track;
    }

    /// <summary>People talking on a call, with blanks, and now and then a loud sound.</summary>
    public static float[] Voice(int slices, int seed)
    {
        var r = new Random(seed);
        var result = new float[slices];
        var i = 0;
        while (i < slices)
        {
            var level = -17 + r.NextDouble() * 7;
            var phrase = r.Next(20, 70);
            for (var p = 0; p < phrase && i < slices; p++, i++)
            {
                var syllable = Math.Sin(i * 2 * Math.PI / 4.2);
                var db = level + 3 * syllable + 2.2 * Gauss(r) - (r.NextDouble() < 0.12 ? 14 : 0);
                result[i] = Db(Math.Min(-1, db));
            }

            if (r.NextDouble() < 0.14)
            {
                var burst = r.Next(3, 11);
                var top = -7 + r.NextDouble() * 5;
                for (var p = 0; p < burst && i < slices; p++, i++)
                {
                    result[i] = Db(top - p * 1.5 + Gauss(r));
                }
            }

            var kind = r.NextDouble();
            i += kind < 0.55 ? r.Next(4, 16) : kind < 0.87 ? r.Next(16, 70) : r.Next(90, 280);
        }

        return result;
    }

    /// <summary>A mastered song: the limiter holds the peaks close to 0 dB almost everywhere, with a quiet intro and a bridge.</summary>
    public static float[] LoudMusic(int slices, int seed)
    {
        var r = new Random(seed);
        var result = new float[slices];
        (int Seconds, double Top)[] song = [(10, -9), (32, -1.4), (24, -0.2), (30, -1.4), (28, -0.2), (12, -5.5), (44, -0.2), (6, -14), (2, -200), (26, -1.2), (28, -0.2), (30, -0.9), (32, -0.2)];
        var i = 0;
        foreach (var (seconds, top) in song)
        {
            for (var t = 0; t < seconds * 10 && i < slices; t++, i++)
            {
                if (top < -100)
                {
                    continue;
                }

                var kick = t % 5 == 0;
                var db = top - (kick ? r.NextDouble() * 0.25 : 1.2 + r.NextDouble() * 3.5);
                result[i] = Db(Math.Min(-0.05, db));
            }
        }

        return result;
    }

    /// <summary>A headset mic: a quiet voice over its own small noise, which stays under the silence floor.</summary>
    public static float[] WeakMic(int slices, int seed)
    {
        var r = new Random(seed);
        var result = new float[slices];
        var i = 0;
        while (i < slices)
        {
            var level = -31 + r.NextDouble() * 9;
            var phrase = r.Next(15, 60);
            for (var p = 0; p < phrase && i < slices; p++, i++)
            {
                result[i] = Db(level + 3 * Math.Sin(i * 2 * Math.PI / 3.7) + 2 * Gauss(r));
            }

            var gap = r.NextDouble() < 0.6 ? r.Next(10, 50) : r.Next(60, 320);
            for (var p = 0; p < gap && i < slices; p++, i++)
            {
                result[i] = Db(-57 + Gauss(r));
            }
        }

        return result;
    }
}
