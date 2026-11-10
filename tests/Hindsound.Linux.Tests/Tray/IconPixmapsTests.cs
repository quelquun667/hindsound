using Hindsound.Linux.DBus;
using Hindsound.Linux.Tray;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Tray;

/// <summary>The images and the properties of StatusNotifierItem. Pure: runs on every system.</summary>
public sealed class IconPixmapsTests
{
    private const uint Accent = 0xA3E635;

    [Fact]
    public void The_icon_comes_in_22_32_and_48_pixels_each_with_four_bytes_a_pixel()
    {
        var pixmaps = IconPixmaps.Of(TrayState.Active, Accent);

        Assert.Equal([22, 32, 48], pixmaps.Select(pixmap => pixmap.Width));
        Assert.Equal([22, 32, 48], pixmaps.Select(pixmap => pixmap.Height));
        Assert.All(pixmaps, pixmap => Assert.Equal(pixmap.Width * pixmap.Height * 4, pixmap.Argb.Length));
    }

    [Theory]
    [InlineData(TrayState.Active)]
    [InlineData(TrayState.Paused)]
    [InlineData(TrayState.Error)]
    public void Each_pixel_is_the_one_of_the_Windows_icon_as_A_R_G_B_in_network_byte_order(TrayState state)
    {
        foreach (var size in IconPixmaps.Sizes)
        {
            var composed = TrayIconShape.Compose(state, size, Accent);
            var bytes = IconPixmaps.Of(state, Accent, size).Argb;
            for (var i = 0; i < composed.Length; i++)
            {
                Assert.Equal(composed[i], (uint)(bytes[i * 4] << 24 | bytes[i * 4 + 1] << 16 | bytes[i * 4 + 2] << 8 | bytes[i * 4 + 3]));
            }
        }
    }

    [Fact]
    public void The_corner_is_clear_and_the_middle_is_opaque()
    {
        var pixmap = IconPixmaps.Of(TrayState.Active, Accent, 32);

        Assert.Equal(0, pixmap.Argb[0]);
        var middle = (16 * 32 + 16) * 4;
        Assert.Equal(255, pixmap.Argb[middle]);
    }

    [Fact]
    public void The_shapes_differ_between_the_states()
    {
        var active = IconPixmaps.Of(TrayState.Active, Accent, 22).Argb;
        var paused = IconPixmaps.Of(TrayState.Paused, Accent, 22).Argb;
        var error = IconPixmaps.Of(TrayState.Error, Accent, 22).Argb;

        Assert.NotEqual(active, paused);
        Assert.NotEqual(active, error);
        Assert.NotEqual(paused, error);
    }

    [Fact]
    public void The_item_is_a_menu_names_its_menu_and_tells_its_tooltip()
    {
        var look = ItemLook.Of(TrayState.Paused, Accent, "Hindsound: paused");

        var properties = StatusNotifierItemObject.PropertiesOf(look).ToDictionary(property => property.Key, property => property.Value);

        Assert.Equal("ApplicationStatus", properties["Category"]);
        Assert.Equal(DBusNames.AppId, properties["Id"]);
        Assert.Equal("Hindsound", properties["Title"]);
        Assert.Equal("Active", properties["Status"]);
        Assert.Equal(true, properties["ItemIsMenu"]);
        Assert.Equal(new BusPath("/MenuBar"), properties["Menu"]);
        Assert.Same(look.Pixmaps, properties["IconPixmap"]);
        var tip = Assert.IsType<ToolTip>(properties["ToolTip"]);
        Assert.Equal("Hindsound", tip.Title);
        Assert.Equal("Hindsound: paused", tip.Text);
    }

    [Fact]
    public void Each_property_has_the_D_Bus_type_of_the_specification()
    {
        var types = StatusNotifierItemObject.PropertiesOf(ItemLook.Of(TrayState.Active, Accent, "tip"))
            .ToDictionary(property => property.Key, property => TrayWire.SignatureOf(property.Value));

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Category"] = "s",
                ["Id"] = "s",
                ["Title"] = "s",
                ["Status"] = "s",
                ["WindowId"] = "i",
                ["IconName"] = "s",
                ["IconPixmap"] = "a(iiay)",
                ["OverlayIconName"] = "s",
                ["OverlayIconPixmap"] = "a(iiay)",
                ["AttentionIconName"] = "s",
                ["AttentionIconPixmap"] = "a(iiay)",
                ["AttentionMovieName"] = "s",
                ["ToolTip"] = "(sa(iiay)ss)",
                ["ItemIsMenu"] = "b",
                ["Menu"] = "o",
            },
            types);
    }

    [Fact]
    public void A_new_tooltip_alone_keeps_the_images_already_drawn()
    {
        var look = ItemLook.Of(TrayState.Active, Accent, "one");

        Assert.Same(look.Pixmaps, look.With(TrayState.Active, Accent, "two").Pixmaps);
        Assert.NotSame(look.Pixmaps, look.With(TrayState.Paused, Accent, "two").Pixmaps);
        Assert.NotSame(look.Pixmaps, look.With(TrayState.Active, 0x38BDF8, "two").Pixmaps);
    }
}
