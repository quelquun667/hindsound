using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// A record stream is never told that the server dropped audio (its overflow callback is for playback streams only), so
/// no packet may claim a gap it cannot know about.
/// </summary>
public sealed class PulsePacketGapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_packet_claims_lost_audio_before_it(bool hole)
    {
        Assert.False(PulseFormats.FlagsOf(hole).HasFlag(PacketFlags.DataDiscontinuity));
    }

    [Fact]
    public void The_capture_asks_for_no_overflow_callback()
    {
        var registers = typeof(PulseCaptureSource).GetMethod("OnOverflow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.Null(registers);
    }
}
