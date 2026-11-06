using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>A shortcut pressed after a menu closed without a command: our hidden window is still the active one.</summary>
public sealed class TrayClickStandingInTests
{
    private static readonly ScreenInfo SecondScreen = new(
        new Native.RECT { Left = 2560, Top = 0, Right = 5120, Bottom = 1440 },
        new Native.RECT { Left = 2560, Top = 0, Right = 5120, Bottom = 1400 },
        144);

    private static readonly TrayClick Click = new("Game", SecondScreen, 1000);

    [Fact]
    public void The_click_stands_in_while_our_hidden_window_is_the_active_one()
    {
        var standing = TrayClick.StandingIn(hiddenWindowActive: true, Click);

        Assert.Equal("Game", standing?.AppName);
        Assert.Equal(SecondScreen, standing?.Screen);
    }

    [Fact]
    public void Once_the_user_went_elsewhere_the_active_window_says_it_again()
    {
        Assert.Null(TrayClick.StandingIn(hiddenWindowActive: false, Click));
    }

    [Fact]
    public void Without_any_click_nothing_stands_in()
    {
        Assert.Null(TrayClick.StandingIn(hiddenWindowActive: true, null));
    }

    [Fact]
    public void However_long_ago_the_click_was_it_still_stands_in()
    {
        var old = Click with { AtMs = 0 };

        Assert.NotNull(TrayClick.StandingIn(hiddenWindowActive: true, old));
        Assert.Null(old.ScreenAt(TrayClick.ScreenKeptMs + 1));
    }
}
