using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class ThemeMixTests
{
    private static (uint R, uint G, uint B) Split(uint rgb) => ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    [Fact]
    public void All_of_one_colour_is_that_colour_and_none_of_it_is_the_other()
    {
        Assert.Equal(0xD4F53Cu, Theme.Mix(0xD4F53C, Theme.Panel, 1f));
        Assert.Equal(Theme.Panel, Theme.Mix(0xD4F53C, Theme.Panel, 0f));
    }

    [Fact]
    public void Half_is_the_middle_channel_by_channel()
    {
        Assert.Equal(0x808080u, Theme.Mix(0xFFFFFF, 0x000000, 0.5f));
        Assert.Equal(0x204060u, Theme.Mix(0x4080C0, 0x000000, 0.5f));
    }

    [Theory]
    [InlineData(0xFFFFFFu)]
    [InlineData(0x000000u)]
    [InlineData(0xF2F4F7u)]
    [InlineData(0x35D6FFu)]
    public void Any_accent_mixes_between_itself_and_the_ground_whatever_the_share(uint accent)
    {
        foreach (var share in new[] { 0.07f, 0.2f, 0.4f, 0.42f, 0.72f, 0.99f })
        {
            var (r, g, b) = Split(Theme.Mix(accent, Theme.Panel, share));
            var (ar, ag, ab) = Split(accent);
            var (gr, gg, gb) = Split(Theme.Panel);
            Assert.InRange(r, Math.Min(ar, gr), Math.Max(ar, gr));
            Assert.InRange(g, Math.Min(ag, gg), Math.Max(ag, gg));
            Assert.InRange(b, Math.Min(ab, gb), Math.Max(ab, gb));
        }
    }

    [Fact]
    public void A_white_accent_gets_darker_as_the_share_falls_and_stays_a_plain_24_bit_colour()
    {
        var previous = uint.MaxValue;
        foreach (var share in new[] { 1f, 0.72f, 0.42f, 0.4f, 0.2f, 0.07f })
        {
            var mixed = Theme.Mix(0xFFFFFF, Theme.Panel, share);
            Assert.Equal(0u, mixed >> 24);
            Assert.True(mixed <= previous);
            previous = mixed;
        }
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void A_share_below_zero_or_not_a_number_is_none_of_it(float share) =>
        Assert.Equal(Theme.Panel, Theme.Mix(0xFFFFFF, Theme.Panel, share));

    [Theory]
    [InlineData(2f)]
    [InlineData(float.PositiveInfinity)]
    public void A_share_above_one_is_all_of_it(float share) =>
        Assert.Equal(0xFFFFFFu, Theme.Mix(0xFFFFFF, Theme.Panel, share));

    [Fact]
    public void The_alpha_of_either_colour_is_ignored()
    {
        Assert.Equal(Theme.Mix(0x35D6FF, Theme.Panel, 0.4f), Theme.Mix(0xFF35D6FF, 0xAA15181E, 0.4f));
    }
}
