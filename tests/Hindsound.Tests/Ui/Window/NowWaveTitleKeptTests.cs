using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A tick that brings nothing new must not work the title out again: while the line of a change shows, that would be every tick.</summary>
public class NowWaveTitleKeptTests
{
    private const int Rate = NowRig.Rate;

    [Fact]
    public void A_second_look_at_the_same_line_with_a_past_app_changes_nothing()
    {
        var wave = new NowWave();
        var status = ScreenRig.Capturing() with { SourceApp = "Spotify.exe" };
        var facts = NowRig.FullUntil(null, null, 100, new SourceChange(95 * Rate, "Spotify.exe", "Discord.exe"));

        wave.Look(1.0, facts, 30, status);
        Assert.Equal("Discord.exe", wave.Marker.Past);

        Assert.Equal(WaveChange.None, wave.Look(1.0, facts, 30, status));
    }
}
