using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

public class RamEstimateTests
{
    [Fact]
    public void FiveMinutesWithMic_Is86_4MB()
    {
        // 300 s * (48000 * 2 + 48000 * 1) samples * 2 bytes (16-bit) = 86,400,000 bytes
        Assert.Equal(86_400_000, RamEstimate.Bytes(300, micActive: true));
        Assert.Equal(86.4, RamEstimate.Megabytes(300, micActive: true), 6);
        Assert.Equal("~86 MB", RamEstimate.Format(300, micActive: true));
    }

    [Fact]
    public void FiveMinutesWithoutMic_Is57_6MB()
    {
        // 300 s * 48000 * 2 * 2 bytes = 57,600,000 bytes
        Assert.Equal(57_600_000, RamEstimate.Bytes(300, micActive: false));
        Assert.Equal(57.6, RamEstimate.Megabytes(300, micActive: false), 6);
        Assert.Equal("~58 MB", RamEstimate.Format(300, micActive: false));
    }

    [Fact]
    public void ZeroSeconds_IsZero()
    {
        Assert.Equal(0, RamEstimate.Bytes(0, micActive: true));
    }

    [Fact]
    public void FromSettings_FollowsTheDisabledMicrophone()
    {
        var withMic = new AppSettings();
        var withoutMic = new AppSettings { Audio = { MicDeviceId = "disabled" } };

        Assert.Equal(86.4, RamEstimate.Megabytes(withMic), 6);
        Assert.Equal(57.6, RamEstimate.Megabytes(withoutMic), 6);
    }
}
