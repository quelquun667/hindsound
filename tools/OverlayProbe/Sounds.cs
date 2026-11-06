using System.IO;
using System.Media;
using NAudio.Lame;
using NAudio.Wave;

namespace OverlayProbe;

internal static class Sounds
{
    private const int Rate = 48000;

    /// Two short tones, 880 then 1320 Hz, 70 ms each, at -18 dBFS: the sound planned when no overlay can show.
    public static byte[] ConfirmationWav()
    {
        var samples = new List<short>();
        AddTone(samples, 880, 0.070, 0.126);
        AddTone(samples, 1320, 0.070, 0.126);
        return ToWav(samples);
    }

    /// Played by a second, unrelated process: the control for the exclusion check.
    public static byte[] OtherAppWav()
    {
        var samples = new List<short>();
        AddTone(samples, 660, 0.4, 0.126);
        AddTone(samples, 990, 0.4, 0.126);
        return ToWav(samples);
    }

    public static void Play(byte[] wav)
    {
        using var stream = new MemoryStream(wav);
        using var player = new SoundPlayer(stream);
        player.PlaySync();
    }

    /// A 5 s test clip, encoded through NAudio.Lame to check that it works with NAudio 3.
    public static void WriteTestMp3(string path)
    {
        var notes = new[] { 440.0, 554.37, 659.25, 880.0, 659.25, 554.37, 440.0, 329.63 };
        var samples = new List<short>();
        foreach (var note in notes)
        {
            AddTone(samples, note, 0.625, 0.2);
        }

        var pcm = new byte[samples.Count * 4];
        for (var i = 0; i < samples.Count; i++)
        {
            var low = (byte)(samples[i] & 0xFF);
            var high = (byte)(samples[i] >> 8);
            pcm[i * 4] = low;
            pcm[i * 4 + 1] = high;
            pcm[i * 4 + 2] = low;
            pcm[i * 4 + 3] = high;
        }

        using var writer = new LameMP3FileWriter(path, new WaveFormat(Rate, 16, 2), 192);
        writer.Write(pcm, 0, pcm.Length);
    }

    private static void AddTone(List<short> samples, double frequency, double seconds, double amplitude)
    {
        var count = (int)(Rate * seconds);
        var fade = Rate / 200;
        for (var i = 0; i < count; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)fade);
            var value = Math.Sin(2 * Math.PI * frequency * i / Rate) * amplitude * envelope;
            samples.Add((short)(value * short.MaxValue));
        }
    }

    private static byte[] ToWav(List<short> samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataSize = samples.Count * 2;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
