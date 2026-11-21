using Hindsound.Mac.Tray;
using Hindsound.Ui;

namespace Hindsound.Mac.Tests.Tray;

/// <summary>The pixels of the icon of the menu bar. Pure: runs on every system.</summary>
public sealed class TemplateIconTests
{
    [Theory]
    [InlineData(TrayState.Active)]
    [InlineData(TrayState.Paused)]
    [InlineData(TrayState.Error)]
    public void The_icon_is_black_with_the_shape_in_its_alpha(TrayState state)
    {
        var pixels = TemplateIcon.Rgba(state);
        var coverage = TrayIconShape.Coverage(state, TemplateIcon.Pixels);

        Assert.Equal(36, TemplateIcon.Pixels);
        Assert.Equal(TemplateIcon.Pixels * TemplateIcon.Pixels * 4, pixels.Length);
        for (var i = 0; i < coverage.Length; i++)
        {
            Assert.Equal(0, pixels[i * 4]);
            Assert.Equal(0, pixels[i * 4 + 1]);
            Assert.Equal(0, pixels[i * 4 + 2]);
            Assert.Equal((byte)MathF.Round(Math.Clamp(coverage[i], 0f, 1f) * 255f), pixels[i * 4 + 3]);
        }

        Assert.Contains(pixels.Where((_, i) => i % 4 == 3), alpha => alpha == 255);
        Assert.Contains(pixels.Where((_, i) => i % 4 == 3), alpha => alpha == 0);
    }

    [Fact]
    public void Each_state_has_its_own_shape_since_a_template_has_no_colour()
    {
        var active = TemplateIcon.Rgba(TrayState.Active);
        var paused = TemplateIcon.Rgba(TrayState.Paused);
        var error = TemplateIcon.Rgba(TrayState.Error);

        Assert.NotEqual(active, paused);
        Assert.NotEqual(active, error);
        Assert.NotEqual(paused, error);
    }
}
