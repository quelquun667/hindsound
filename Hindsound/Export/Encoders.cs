using System.IO;
using NAudio.Lame;
using NAudio.Wave;

namespace Hindsound.Export;

/// <summary>Writes float audio to disk as WAV (16-bit) or MP3 (LAME).</summary>
public static class Encoders
{
    /// <summary>Frames converted and written at a time (about 0.2 s at 48 kHz).</summary>
    private const int ChunkFrames = 8192;

    /// <summary>
    /// Converts one float sample to 16-bit: clamped to [-1, 1], scaled by 32767 so that full scale is
    /// symmetric, then rounded to the nearest integer. NaN becomes silence.
    /// </summary>
    public static short ToInt16(float sample)
    {
        if (float.IsNaN(sample)) return 0;
        var clamped = Math.Clamp(sample, -1f, 1f);
        return (short)MathF.Round(clamped * short.MaxValue, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts floats to little-endian 16-bit PCM bytes. The destination needs two bytes per sample.</summary>
    public static void ToPcm16(ReadOnlySpan<float> samples, Span<byte> destination)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var value = ToInt16(samples[i]);
            destination[2 * i] = (byte)(value & 0xFF);
            destination[2 * i + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    /// <summary>
    /// Writes interleaved float samples as a 16-bit PCM WAV file at <see cref="Audio.InternalFormat.SampleRate"/>.
    /// Never overwrites: fails if the file already exists.
    /// </summary>
    public static void WriteWav(string path, ReadOnlySpan<float> interleaved, int channels)
    {
        ValidateChannels(channels);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new WaveFileWriter(stream, new WaveFormat(Audio.InternalFormat.SampleRate, 16, channels));
        WriteChunks(interleaved, channels, (buffer, count) => writer.Write(buffer, 0, count));
    }

    /// <summary>
    /// Writes interleaved float samples as an MP3 file at <see cref="Audio.InternalFormat.SampleRate"/>.
    /// Never overwrites: fails if the file already exists.
    /// </summary>
    /// <param name="bitrateKbps">128, 192 or 320.</param>
    public static void WriteMp3(string path, ReadOnlySpan<float> interleaved, int channels, int bitrateKbps)
    {
        ValidateChannels(channels);
        if (bitrateKbps is not (128 or 192 or 320))
            throw new ArgumentOutOfRangeException(nameof(bitrateKbps), bitrateKbps, "The MP3 bitrate must be 128, 192 or 320 kbps.");

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new LameMP3FileWriter(stream, new WaveFormat(Audio.InternalFormat.SampleRate, 16, channels), bitrateKbps);
        WriteChunks(interleaved, channels, (buffer, count) => writer.Write(buffer, 0, count));
    }

    private static void ValidateChannels(int channels)
    {
        if (channels is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono and stereo are supported.");
    }

    private static void WriteChunks(ReadOnlySpan<float> interleaved, int channels, Action<byte[], int> write)
    {
        var chunkSamples = ChunkFrames * channels;
        var buffer = new byte[chunkSamples * 2];
        for (var offset = 0; offset < interleaved.Length; offset += chunkSamples)
        {
            var count = Math.Min(chunkSamples, interleaved.Length - offset);
            ToPcm16(interleaved.Slice(offset, count), buffer);
            write(buffer, count * 2);
        }
    }
}
