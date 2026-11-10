using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public class TrayClickTests
{
    private static readonly ScreenInfo Second = new(
        new Native.RECT { Left = 1920, Top = 0, Right = 3840, Bottom = 1080 },
        new Native.RECT { Left = 1920, Top = 0, Right = 3840, Bottom = 1040 },
        144);

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("TaskListThumbnailWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void The_taskbar_and_the_desktop_never_name_a_clip(string className)
    {
        Assert.False(ForegroundWindow.UsableForApp("Some title", className, ownWindow: false));
    }

    [Theory]
    [InlineData("HindsoundMessages")]
    [InlineData("HindsoundNotification")]
    [InlineData("HindsoundSettings")]
    public void The_classes_of_this_app_never_name_a_clip(string className)
    {
        Assert.False(ForegroundWindow.UsableForApp("Hindsound", className, ownWindow: false));
    }

    [Fact]
    public void A_window_of_this_process_never_names_a_clip_whatever_its_class()
    {
        // The folder dialog of the settings window is a window of ours, of a class of Windows.
        Assert.False(ForegroundWindow.UsableForApp("Select folder", "#32770", ownWindow: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_window_without_a_title_names_nothing(string title)
    {
        Assert.False(ForegroundWindow.UsableForApp(title, "SomeGameWindow", ownWindow: false));
    }

    [Theory]
    [InlineData("Valorant", "UnrealWindow")]
    [InlineData("Hindsound - GitHub", "Chrome_WidgetWin_1")]
    public void Any_other_window_names_the_clip(string title, string className)
    {
        Assert.True(ForegroundWindow.UsableForApp(title, className, ownWindow: false));
    }

    [Fact]
    public void The_next_status_after_the_click_goes_to_its_screen()
    {
        var click = new TrayClick(null, Second, 1000);

        Assert.Equal(Second, click.ScreenAt(1000 + TrayClick.ScreenKeptMs));
    }

    [Fact]
    public void A_status_long_after_the_click_is_not_its_answer()
    {
        var click = new TrayClick(null, Second, 1000);

        Assert.Null(click.ScreenAt(1000 + TrayClick.ScreenKeptMs + 1));
    }
}
