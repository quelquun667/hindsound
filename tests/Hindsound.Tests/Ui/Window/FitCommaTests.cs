using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A text cut right after a comma does not keep it in front of the dots: "Discord,..." reads as a list that stops.</summary>
public class FitCommaTests
{
    [Fact]
    public void A_cut_that_ends_on_a_comma_leaves_it_out()
    {
        // 6.5 px a character on the fake canvas: 72 px hold "Discord,..." but not "Discord, t...".
        Assert.Equal("Discord...", new FakeCanvas().Fit("Discord, then Spotify", Face.Medium, 13, 72));
    }

    [Fact]
    public void A_cut_elsewhere_is_unchanged()
    {
        Assert.Equal("Discord, th...", new FakeCanvas().Fit("Discord, then Spotify", Face.Medium, 13, 92));
    }
}
