using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public class TrayIconComposeTests
{
    private const uint Accent = 0xD4F53C;
    private const string Plate = "#15181E";

    [Fact]
    public void The_plate_is_the_notification_background()
    {
        Assert.Equal(0x15181Eu, TrayIconShape.PlateColor);
        Assert.Equal(NotificationPalette.Fill, TrayIconShape.PlateColor);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void The_shape_sits_on_an_opaque_dark_plate_with_rounded_corners(int size)
    {
        var pixels = TrayIconShape.Compose(TrayState.Active, size, Accent);

        Assert.Equal(size * size, pixels.Length);
        Assert.True(pixels[0] >> 24 < 128, "top left corner");
        Assert.True(pixels[size - 1] >> 24 < 128, "top right corner");
        Assert.True(pixels[size * size - 1] >> 24 < 128, "bottom right corner");

        var opaque = pixels.Where(p => p >> 24 == 255).ToArray();
        Assert.Contains(0xFF000000u | TrayIconShape.PlateColor, opaque);
        Assert.Contains(0xFF000000u | Accent, opaque);
    }

    [Fact]
    public void The_edge_of_the_icon_is_plate_and_never_the_shape()
    {
        const int Size = 16;
        var pixels = TrayIconShape.Compose(TrayState.Active, Size, Accent);

        for (var i = 3; i < Size - 3; i++)
        {
            Assert.Equal(0xFF000000u | TrayIconShape.PlateColor, pixels[i]);
            Assert.Equal(0xFF000000u | TrayIconShape.PlateColor, pixels[i * Size]);
        }
    }

    [Theory]
    [InlineData(TrayState.Paused)]
    [InlineData(TrayState.Error)]
    public void Pause_and_error_are_drawn_on_the_same_plate_in_their_own_colors(TrayState state)
    {
        var pixels = TrayIconShape.Compose(state, 24, Accent);

        Assert.Contains(0xFF000000u | TrayIconShape.PlateColor, pixels);
        Assert.Contains(0xFF000000u | TrayIconShape.ColorOf(state, Accent), pixels);
        Assert.DoesNotContain(0xFF000000u | Accent, pixels);
    }

    [Theory]
    [InlineData("#E6E6E6")]
    [InlineData("#F3F3F3")]
    public void The_plate_stands_out_on_a_light_taskbar(string taskbar)
    {
        var contrast = AccentColors.ContrastRatio(Plate, taskbar);

        Assert.True(contrast > 10, $"contrast {contrast}");
    }

    [Theory]
    [InlineData("#D4F53C")]
    [InlineData("#2EF2C4")]
    [InlineData("#35D6FF")]
    [InlineData("#B79CFF")]
    [InlineData("#FF6FB5")]
    [InlineData("#F2F4F7")]
    public void Every_preset_reads_on_the_plate(string hex)
    {
        var contrast = AccentColors.ContrastRatio(hex, Plate);

        Assert.True(contrast >= 3, $"{hex}: contrast {contrast}");
    }
}
