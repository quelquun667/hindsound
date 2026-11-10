using System.Buffers.Binary;

namespace Hindsound.Linux.Tests.Export;

/// <summary>Reads a 16-bit PCM WAV file and measures the pitch of what it holds. Enough to check a decoded clip.</summary>
internal static class WavProbe
{
    public sealed record Wav(int SampleRate, int Channels, short[] Samples)
    {
        public int Frames => Samples.Length / Channels;
        public double Seconds => (double)Frames / SampleRate;

        /// <summary>One channel of the file.</summary>
        public short[] Channel(int index)
        {
            var one = new short[Frames];
            for (var i = 0; i < one.Length; i++)
                one[i] = Samples[i * Channels + index];
            return one;
        }
    }

    /// <summary>Parses the chunks of a RIFF/WAVE file. A data chunk that claims more than the file holds is cut to the file.</summary>
    public static Wav Read(byte[] file)
    {
        if (file.Length < 12 || !file.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !file.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a RIFF/WAVE file.");

        int sampleRate = 0, channels = 0, bits = 0;
        var position = 12;
        while (position + 8 <= file.Length)
        {
            var id = file.AsSpan(position, 4);
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4, 4));
            var body = position + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(body + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(body + 14, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (bits != 16 || channels == 0)
                    throw new InvalidDataException("Only 16-bit PCM is read, and the fmt chunk comes first.");

                var available = Math.Min(size, file.Length - body);
                var count = (int)(available / 2) / channels * channels;
                var samples = new short[count];
                for (var i = 0; i < count; i++)
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(file.AsSpan(body + 2 * i, 2));
                return new Wav(sampleRate, channels, samples);
            }

            position = (int)Math.Min(file.Length, body + size + (size & 1));
        }

        throw new InvalidDataException("The file has no data chunk.");
    }

    /// <summary>The frequency of a tone: the rising crossings of zero in the middle half, per second of that stretch.</summary>
    public static double Frequency(short[] channel, int sampleRate)
    {
        var from = channel.Length / 4;
        var to = channel.Length * 3 / 4;
        var crossings = 0;
        for (var i = from + 1; i < to; i++)
            if (channel[i - 1] < 0 && channel[i] >= 0)
                crossings++;
        return crossings * (double)sampleRate / (to - from);
    }
}
