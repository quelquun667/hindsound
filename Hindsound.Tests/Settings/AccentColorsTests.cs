using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

public class AccentColorsTests
{
    [Theory]
    [InlineData("#D4F53C")]
    [InlineData("#2EF2C4")]
    [InlineData("#35D6FF")]
    [InlineData("#B79CFF")]
    [InlineData("#FF6FB5")]
    [InlineData("#F2F4F7")]
    public void PlanColors_AreAllValid(string hex)
    {
        Assert.Equal(AccentCheck.Valid, AccentColors.Validate(hex));
        Assert.Equal(hex, AccentColors.Resolve(hex));
    }

    [Fact]
    public void PresetOf_NamesThePresetsAndCustom()
    {
        Assert.Equal(AccentPreset.Lime, AccentColors.PresetOf("#d4f53c"));
        Assert.Equal(AccentPreset.Mint, AccentColors.PresetOf("#2EF2C4"));
        Assert.Equal(AccentPreset.Cyan, AccentColors.PresetOf("#35D6FF"));
        Assert.Equal(AccentPreset.Violet, AccentColors.PresetOf("#B79CFF"));
        Assert.Equal(AccentPreset.Pink, AccentColors.PresetOf("#FF6FB5"));
        Assert.Equal(AccentPreset.White, AccentColors.PresetOf("#F2F4F7"));
        Assert.Equal(AccentPreset.Custom, AccentColors.PresetOf("#FF8800"));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#1A1A1A")]
    [InlineData("#15181E")]
    [InlineData("#202040")]
    public void DarkColors_AreRefused(string hex)
    {
        Assert.Equal(AccentCheck.TooDark, AccentColors.Validate(hex));
        Assert.Equal("#D4F53C", AccentColors.Resolve(hex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void InvalidText_FallsBackToLime(string? text)
    {
        Assert.Equal(AccentCheck.InvalidFormat, AccentColors.Validate(text));
        Assert.Equal("#D4F53C", AccentColors.Resolve(text));
    }

    [Theory]
    [InlineData("#fff", "#FFFFFF")]
    [InlineData("ff8800", "#FF8800")]
    [InlineData("  #aBcDeF ", "#ABCDEF")]
    public void Hex_IsNormalised(string text, string expected)
    {
        Assert.Equal(expected, AccentColors.Resolve(text));
    }

    [Fact]
    public void Contrast_BlackOnWhiteIs21()
    {
        Assert.Equal(21.0, AccentColors.ContrastRatio("#000000", "#FFFFFF"), 6);
    }

    [Fact]
    public void Contrast_WhiteOnNotificationBackground_MatchesHandCalculation()
    {
        // L(#15181E) = 0.2126*0.00751 + 0.7152*0.00914 + 0.0722*0.01300 = 0.009072 ; (1 + 0.05) / (0.009072 + 0.05) = 17.78
        Assert.Equal(17.78, AccentColors.ContrastRatio("#FFFFFF", AccentColors.BackgroundHex), 1);
    }

    [Fact]
    public void Contrast_IsSymmetricAndOneForSameColour()
    {
        Assert.Equal(1.0, AccentColors.ContrastRatio("#336699", "#336699"), 9);
        Assert.Equal(AccentColors.ContrastRatio("#336699", "#FFFFFF"), AccentColors.ContrastRatio("#FFFFFF", "#336699"), 9);
    }

    [Fact]
    public void GeneralSettings_WithBadAccent_NormalisesToLime()
    {
        var s = new AppSettings { General = { AccentColor = "#000000" } }.Normalize();

        Assert.Equal("#D4F53C", s.General.AccentColor);
    }
}
