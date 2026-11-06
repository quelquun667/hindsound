namespace Hindsound.Audio;

/// <summary>What to do with a packet so that it lands where its timestamp says.</summary>
/// <param name="PadFrames">Zeros to write first.</param>
/// <param name="SkipFrames">Frames to drop from the start of the packet.</param>
/// <param name="RepeatLastFrame">Write the last frame of the packet twice.</param>
public readonly record struct PacketPlacement(long PadFrames, int SkipFrames, bool RepeatLastFrame);

/// <summary>
/// Keeps each source on the shared timeline. The loopback delivers nothing while the PC is silent, so a packet
/// can start well after the last one ended: that hole becomes zeros. The other small differences are clock
/// drift between the sound card and the performance counter, corrected one frame at a time so nothing is heard.
/// </summary>
public static class SilencePadder
{
    /// <summary>Stamp jitter ignored, 2 ms.</summary>
    public const int JitterFrames = 96;

    /// <summary>Up to this error, 20 ms, the source is drifting and not interrupted.</summary>
    public const int MaxDriftFrames = 960;

    /// <param name="writeEnd">Timeline frame just after the last frame written.</param>
    /// <param name="packetStart">Timeline frame the packet's stamp points to.</param>
    /// <param name="packetFrames">Frames in the packet.</param>
    /// <param name="exact">The stream was interrupted, so there is no drift to smooth: place the packet exactly.</param>
    public static PacketPlacement Place(long writeEnd, long packetStart, int packetFrames, bool exact)
    {
        var error = packetStart - writeEnd;
        var size = Math.Abs(error);
        if (size <= (exact ? 0 : JitterFrames) || packetFrames == 0)
        {
            return default;
        }

        if (!exact && size <= MaxDriftFrames)
        {
            return error > 0
                ? new PacketPlacement(0, 0, true)
                : new PacketPlacement(0, 1, false);
        }

        return error > 0
            ? new PacketPlacement(error, 0, false)
            : new PacketPlacement(0, (int)Math.Min(size, packetFrames), false);
    }
}
