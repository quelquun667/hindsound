using Hindsound.Audio;
using Hindsound.Ui;
using NAudio.CoreAudioApi;

namespace Hindsound.Tests;

/// <summary>The portable copies of Windows values: the Windows layer passes them on as they are, so they must not drift.</summary>
public class PlatformValuesTests
{
    [Fact]
    public void Each_packet_flag_has_the_value_of_its_WASAPI_flag()
    {
        var portable = Enum.GetValues<PacketFlags>();
        var wasapi = Enum.GetValues<AudioClientBufferFlags>();

        Assert.Equal(wasapi.Length, portable.Length);
        foreach (var flag in portable)
        {
            Assert.Equal((int)flag, (int)Enum.Parse<AudioClientBufferFlags>(flag.ToString()));
        }
    }

    [Fact]
    public void The_tray_menu_reads_the_codes_the_icon_receives()
    {
        Assert.Equal((uint)Native.WM_CONTEXTMENU, TrayMenu.ContextMenuCode);
        Assert.Equal((uint)Native.NIN_SELECT, TrayMenu.SelectCode);
        Assert.Equal((uint)Native.NIN_KEYSELECT, TrayMenu.KeySelectCode);
    }

    [Fact]
    public void A_Win32_rectangle_keeps_its_edges_on_the_portable_side()
    {
        var native = new Native.RECT { Left = -2560, Top = -200, Right = 0, Bottom = 1240 };

        ScreenRect portable = native;

        Assert.Equal(new ScreenRect(-2560, -200, 0, 1240), portable);
        Assert.Equal(native, Native.RECT.From(portable));
        Assert.Equal(2560, portable.Width);
        Assert.Equal(1440, portable.Height);
    }
}
