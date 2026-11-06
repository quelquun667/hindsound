namespace Hindsound.Ui;

/// <summary>The confirmation played when no notification can show: two short tones, generated once, played by PlaySound.</summary>
internal static unsafe class ConfirmSound
{
    private const int Rate = 48000;
    private const int HeaderBytes = 44;

    private static byte[]? _wav;

    /// <summary>Starts playing and returns at once. Never throws: a missing sound must not cost a clip.</summary>
    public static bool Play()
    {
        try
        {
            // PlaySound reads this memory while it plays, so it stays pinned for the life of the process.
            _wav ??= BuildWav();
            fixed (byte* wav = _wav)
            {
                return Native.PlaySoundW(wav, 0, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT) != 0;
            }
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
            return false;
        }
    }

    /// <summary>880 then 1320 Hz, 70 ms each, at -18 dBFS, as a mono 16-bit WAV.</summary>
    public static byte[] BuildWav()
    {
        var toneFrames = (int)(Rate * 0.070);
        var dataBytes = toneFrames * 2 * 2;
        var wav = GC.AllocateUninitializedArray<byte>(HeaderBytes + dataBytes, pinned: true);
        "RIFF"u8.CopyTo(wav);
        BitConverter.TryWriteBytes(wav.AsSpan(4), HeaderBytes - 8 + dataBytes);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(24), Rate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), Rate * 2);
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BitConverter.TryWriteBytes(wav.AsSpan(40), dataBytes);
        WriteTone(wav.AsSpan(HeaderBytes), 880, toneFrames);
        WriteTone(wav.AsSpan(HeaderBytes + toneFrames * 2), 1320, toneFrames);
        return wav;
    }

    private static void WriteTone(Span<byte> target, double frequency, int frames)
    {
        const double amplitude = 0.126;
        var fade = Rate / 200;
        for (var i = 0; i < frames; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, frames - 1 - i) / (double)fade);
            var value = Math.Sin(2 * Math.PI * frequency * i / Rate) * amplitude * envelope;
            BitConverter.TryWriteBytes(target[(i * 2)..], (short)(value * short.MaxValue));
        }
    }
}
