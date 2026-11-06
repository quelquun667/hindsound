using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

public class RamEstimateTests
{
    [Fact]
    public void FiveMinutesWithMic_Is86_688MB()
    {
        // 300 s * (48000 * 2 + 48000 * 1) samples * 2 bytes (16-bit) = 86,400,000 bytes,
        // plus the fixed slack of the sources: one second of frames, 48000 * 3 * 2 = 288,000 bytes.
        Assert.Equal(86_688_000, RamEstimate.Bytes(300, micActive: true));
        Assert.Equal(86.688, RamEstimate.Megabytes(300, micActive: true), 6);
        Assert.Equal("~87 MB", RamEstimate.Format(300, micActive: true));
    }

    [Fact]
    public void FiveMinutesWithoutMic_Is57_792MB()
    {
        // 300 s * 48000 * 2 * 2 bytes = 57,600,000 bytes, plus the slack of the PC audio alone: 48000 * 2 * 2 = 192,000.
        Assert.Equal(57_792_000, RamEstimate.Bytes(300, micActive: false));
        Assert.Equal(57.792, RamEstimate.Megabytes(300, micActive: false), 6);
        Assert.Equal("~58 MB", RamEstimate.Format(300, micActive: false));
    }

    [Fact]
    public void FifteenSecondsWithMic_CountsTheSlack_SoItIsNotRoundedDown()
    {
        // 15 s * 288,000 = 4,320,000 plus 288,000 of slack = 4,608,000 bytes: "~5 MB", not "~4 MB".
        Assert.Equal(4_608_000, RamEstimate.Bytes(15, micActive: true));
        Assert.Equal("~5 MB", RamEstimate.Format(15, micActive: true));
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

        Assert.Equal(86.688, RamEstimate.Megabytes(withMic), 6);
        Assert.Equal(57.792, RamEstimate.Megabytes(withoutMic), 6);
    }
}
