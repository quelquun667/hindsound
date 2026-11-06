using System.IO;
using NAudio.Lame;
using NAudio.Wave;

namespace Hindsound.Export;

/// <summary>An audio file being written piece by piece. Dispose finishes the file.</summary>
public interface IAudioFileWriter : IDisposable
{
    /// <summary>Appends interleaved float samples (a whole number of frames).</summary>
    void Write(ReadOnlySpan<float> interleaved);
}

/// <summary>Writes float audio to disk as WAV (16-bit) or MP3 (LAME).</summary>
public static class Encoders
{
    /// <summary>Frames converted and written at a time (about 0.17 s at 48 kHz).</summary>
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
    /// Opens a 16-bit PCM WAV file at <see cref="Audio.InternalFormat.SampleRate"/>.
    /// Never overwrites: fails if the file already exists.
    /// </summary>
    public static IAudioFileWriter OpenWav(string path, int channels)
    {
        ValidateChannels(channels);
        return Open(path, channels, stream => new WaveFileWriter(stream, new WaveFormat(Audio.InternalFormat.SampleRate, 16, channels)));
    }

    /// <summary>
    /// Opens an MP3 file (constant bitrate) at <see cref="Audio.InternalFormat.SampleRate"/>.
    /// Never overwrites: fails if the file already exists.
    /// </summary>
    /// <param name="bitrateKbps">128, 192 or 320.</param>
    public static IAudioFileWriter OpenMp3(string path, int channels, int bitrateKbps)
    {
        ValidateChannels(channels);
        if (bitrateKbps is not (128 or 192 or 320))
            throw new ArgumentOutOfRangeException(nameof(bitrateKbps), bitrateKbps, "The MP3 bitrate must be 128, 192 or 320 kbps.");

        return Open(path, channels, stream => new LameMP3FileWriter(stream, new WaveFormat(Audio.InternalFormat.SampleRate, 16, channels), bitrateKbps));
    }

    /// <summary>Writes a whole buffer as WAV.</summary>
    public static void WriteWav(string path, ReadOnlySpan<float> interleaved, int channels)
    {
        using var writer = OpenWav(path, channels);
        writer.Write(interleaved);
    }

    /// <summary>Writes a whole buffer as MP3.</summary>
    public static void WriteMp3(string path, ReadOnlySpan<float> interleaved, int channels, int bitrateKbps)
    {
        using var writer = OpenMp3(path, channels, bitrateKbps);
        writer.Write(interleaved);
    }

    private static void ValidateChannels(int channels)
    {
        if (channels is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono and stereo are supported.");
    }

    private static PcmFileWriter Open(string path, int channels, Func<Stream, Stream> createEncoder)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            return new PcmFileWriter(stream, createEncoder(stream), channels);
        }
        catch
        {
            stream.Dispose();
            try { File.Delete(path); } catch (Exception) { /* best effort */ }
            throw;
        }
    }

    /// <summary>Converts to 16-bit through one small reused buffer and hands the bytes to a WAV or MP3 stream.</summary>
    private sealed class PcmFileWriter : IAudioFileWriter
    {
        private readonly Stream _file;
        private readonly Stream _encoder;
        private readonly int _channels;
        private readonly byte[] _buffer;

        public PcmFileWriter(Stream file, Stream encoder, int channels)
        {
            _file = file;
            _encoder = encoder;
            _channels = channels;
            _buffer = new byte[ChunkFrames * channels * 2];
        }

        public void Write(ReadOnlySpan<float> interleaved)
        {
            var chunkSamples = ChunkFrames * _channels;
            for (var offset = 0; offset < interleaved.Length; offset += chunkSamples)
            {
                var count = Math.Min(chunkSamples, interleaved.Length - offset);
                ToPcm16(interleaved.Slice(offset, count), _buffer);
                _encoder.Write(_buffer, 0, count * 2);
            }
        }

        public void Dispose()
        {
            try { _encoder.Dispose(); }
            finally { _file.Dispose(); }
        }
    }
}
