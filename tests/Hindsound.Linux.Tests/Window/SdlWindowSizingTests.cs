using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Linux.Window;
using Hindsound.Linux.Window.Input;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>The arithmetic of the Linux window's size and place, and the keys the probe sends.</summary>
public sealed class SdlWindowSizingTests
{
    [Theory]
    [InlineData(1f, 100, 760, 488)]
    [InlineData(1f, 140, 1064, 683)]
    [InlineData(1.5f, 100, 1140, 732)]
    [InlineData(2f, 120, 1824, 1171)]
    public void The_bitmap_is_the_design_size_times_the_display_scale_and_the_zoom_as_on_windows(float scale, int zoom, int width, int height) =>
        Assert.Equal((width, height), SdlWindowSizing.PixelSize(SdlWindowSizing.Scale(scale, zoom)));

    [Fact]
    public void A_screen_1024_wide_holds_the_window_at_120_and_never_below_100()
    {
        var area = new ScreenRect(0, 0, 1024, 740);
        var frame = new WindowBorders(30, 0, 0, 0);
        Assert.Equal(120, SdlWindowSizing.ChooseZoom(140, 1, 1, frame, area));
        Assert.Equal(100, SdlWindowSizing.ChooseZoom(140, 2, 1, frame, area));
        Assert.Equal(100, SdlWindowSizing.ChooseZoom(100, 1, 1, frame, area));

        // On Wayland a window coordinate is two pixels at 200 %: the same screen holds the same zoom.
        Assert.Equal(120, SdlWindowSizing.ChooseZoom(140, 2, 2, frame, area));
    }

    [Fact]
    public void A_window_too_big_for_its_screen_keeps_its_title_bar_in_reach()
    {
        var area = new ScreenRect(100, 50, 900, 650);
        var frame = new WindowBorders(30, 2, 2, 2);
        var outer = SdlWindowSizing.OuterSize(1, 1, frame);
        Assert.Equal((764, 520), outer);
        Assert.Equal((100 + 18 + 2, 50 + 40 + 30), SdlWindowSizing.Centered(area, outer, frame));

        var big = SdlWindowSizing.OuterSize(2, 1, frame);
        Assert.Equal((102, 80), SdlWindowSizing.Centered(area, big, frame));
        Assert.Equal((102, 80), SdlWindowSizing.Fit(-500, -500, big, area, frame));
    }

    [Fact]
    public void A_point_of_an_event_in_window_coordinates_is_turned_into_design_pixels()
    {
        Assert.Equal(100f, SdlWindowSizing.ToDesign(150, 1, 1.5));
        Assert.Equal(100f, SdlWindowSizing.ToDesign(100, 2, 2));
        Assert.Equal(50f, SdlWindowSizing.ToDesign(50, 0, 1));
    }

    [Fact]
    public void An_area_to_put_on_the_window_covers_its_pixels_and_stays_inside_the_bitmap()
    {
        Assert.Equal((15, 30, 46, 61), SdlWindowSizing.PixelArea(new Box(10.2f, 20.1f, 30.3f, 40.4f), 1.5, 1000, 1000));
        Assert.Equal((90, 0, 10, 5), SdlWindowSizing.PixelArea(new Box(90, -10, 50, 15), 1, 100, 100));
        Assert.Equal((10, 20, 100, 40), SdlWindowSizing.Union((10, 20, 30, 40), (100, 20, 10, 10)));
        Assert.Equal((1, 2, 3, 4), SdlWindowSizing.Union(default, (1, 2, 3, 4)));
    }

    [Theory]
    [InlineData(0x61u, 0u, "a")]
    [InlineData(0x61u, HotkeyGesture.ModShift, "A")]
    [InlineData(0xE9u, 0u, "é")]
    [InlineData(0x61u, HotkeyGesture.ModControl, null)]
    [InlineData(0x0Du, 0u, null)]
    [InlineData(0x7Fu, 0u, null)]
    [InlineData(0x40000050u, 0u, null)]
    public void A_key_that_brings_a_field_back_types_the_character_of_its_keycode(uint keycode, uint modifiers, string? text) =>
        Assert.Equal(text, SdlWindowSizing.TypedText(keycode, modifiers));

    [Theory]
    [InlineData(0x31u, 0x21u, "!")]
    [InlineData(0x26u, 0x31u, "1")]
    [InlineData(0x61u, 0x41u, "A")]
    [InlineData(0xE9u, 0xC9u, "É")]
    public void With_Shift_a_key_types_what_the_layout_gives_it(uint own, uint layout, string text) =>
        Assert.Equal(text, SdlWindowSizing.TypedText(own, HotkeyGesture.ModShift, layout));

    [Fact]
    public void A_layout_character_does_not_open_the_way_to_a_key_with_Ctrl_or_a_key_without_character()
    {
        Assert.Null(SdlWindowSizing.TypedText('1', HotkeyGesture.ModControl | HotkeyGesture.ModShift, '!'));
        Assert.Null(SdlWindowSizing.TypedText(0x40000050u, HotkeyGesture.ModShift, '!'));
        Assert.Equal("1", SdlWindowSizing.TypedText('1', HotkeyGesture.ModShift, 0x7F));
    }

    [Theory]
    [InlineData(Vk.Tab)]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Escape)]
    [InlineData(Vk.Space)]
    [InlineData(Vk.Down)]
    [InlineData(Vk.Left)]
    [InlineData(Vk.Home)]
    [InlineData(Vk.Back)]
    [InlineData((uint)'A')]
    [InlineData((uint)'7')]
    [SupportedOSPlatform("linux")]
    public void Each_key_the_probe_sends_is_read_back_as_that_virtual_key(uint vk)
    {
        var key = LinuxSettingsProbe.KeyFor(vk);
        Assert.NotNull(key);
        Assert.Equal(vk, SdlKeys.VirtualKey(key.Value.Scancode, key.Value.Keycode, 0));
    }
}
