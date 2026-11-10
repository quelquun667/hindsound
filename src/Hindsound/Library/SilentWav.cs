namespace Hindsound.Library;

/// <summary>
/// A WAV file of pure digital silence: what a run drawn off screen may play, and the only thing it may play. A run with
/// <c>--offscreen</c> is a test or a probe, which must never make a sound, so a clip is played there only once its data have
/// been read back as all zeros.
/// </summary>
internal static class SilentWav
{
    /// <summary>Beyond this, a file is not read: a probe writes a few seconds, and a real clip is never silent.</summary>
    internal const long MaxBytes = 4 * 1024 * 1024;

    private const int SampleRate = 48000, Channels = 2, BytesPerSample = 2;

    /// <summary>Writes this many milliseconds of silence, 16-bit stereo PCM at 48 kHz.</summary>
    public static void Write(string path, int milliseconds)
    {
        var frames = (long)SampleRate * milliseconds / 1000;
        var dataBytes = (int)(frames * Channels * BytesPerSample);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * BytesPerSample);
        writer.Write((short)(Channels * BytesPerSample));
        writer.Write((short)(BytesPerSample * 8));
        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
    }

    /// <summary>
    /// Whether the file is a PCM WAV whose data hold only zeros, and nothing else is said in it. Anything that can't be read that
    /// way, a compressed file, a chunk cut short, a file too big, is not silence.
    /// </summary>
    public static bool IsPureSilence(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes || stream.Length < 12)
            {
                return false;
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return IsPureSilence(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsPureSilence(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var pcm = false;
        var formatSeen = false;
        var data = false;
        var at = 12;
        while (at + 8 <= file.Length)
        {
            var id = file.Slice(at, 4);
            var size = BitConverter.ToUInt32(file.Slice(at + 4, 4));
            var start = at + 8;
            if (size > (uint)(file.Length - start))
            {
                return false;
            }

            var body = file.Slice(start, (int)size);
            if (id.SequenceEqual("fmt "u8))
            {
                // One format, said before the data: a second one could change what the zeros mean after the first was believed.
                // Only 16-bit PCM is signed around zero here, where a zero is silence; in 8-bit PCM it is full scale.
                if (formatSeen || data)
                {
                    return false;
                }

                formatSeen = true;
                pcm = body.Length >= 16 && BitConverter.ToUInt16(body[..2]) == 1 && BitConverter.ToUInt16(body.Slice(14, 2)) == BytesPerSample * 8;
            }
            else if (id.SequenceEqual("data"u8))
            {
                // A sound can hide in any byte: every one of them is looked at.
                if (body.IndexOfAnyExcept((byte)0) >= 0)
                {
                    return false;
                }

                data = true;
            }
            else
            {
                // Another chunk could be a second sound a player reads: only fmt and data are known to be quiet.
                return false;
            }

            at = start + (int)size + (int)(size & 1);
        }

        return pcm && data && at == file.Length;
    }
}
