using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public class TrayGestureTests
{
    [Fact]
    public void A_left_click_opens_the_settings()
    {
        Assert.Equal(TrayGesture.OpenSettings, TrayMenu.GestureOf(Native.NIN_SELECT));
    }

    [Fact]
    public void Enter_on_the_icon_opens_the_settings()
    {
        Assert.Equal(TrayGesture.OpenSettings, TrayMenu.GestureOf(Native.NIN_KEYSELECT));
    }

    [Fact]
    public void A_right_click_opens_the_menu()
    {
        Assert.Equal(TrayGesture.Menu, TrayMenu.GestureOf(Native.WM_CONTEXTMENU));
    }

    [Theory]
    [InlineData(0x0200u)] // the mouse moves over the icon
    [InlineData(0x0201u)] // left button down
    [InlineData(0x0202u)] // left button up, which comes on top of NIN_SELECT
    [InlineData(0x0406u)] // the tip shows
    public void Anything_else_does_nothing(uint code)
    {
        Assert.Equal(TrayGesture.None, TrayMenu.GestureOf(code));
    }
}
