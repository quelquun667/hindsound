using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>After an update, "Capture on" says the new version in its title. It may wait for the desktop and be made again: the title must survive that.</summary>
public class UpdatedCaptureOnTests
{
    private static readonly HotkeyResult[] AllGood = [new(HotkeyAction.SaveDefault, "F24", HotkeyStatus.Registered, null)];
    private static readonly HotkeyResult[] Taken = [new(HotkeyAction.SaveDefault, "F24", HotkeyStatus.InUse, HotkeyResult.InUseMessage)];

    private static AppSettings Read()
    {
        var settings = new AppSettings { BufferSeconds = 60 };
        settings.Hotkeys.SaveDefault = "F24";
        return settings;
    }

    [Fact]
    public void Updated_says_the_version_and_the_usual_line()
    {
        var updated = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", updatedTo: "0.2.0");
        var usual = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10");

        Assert.Equal("Updated to 0.2.0", updated.Title);
        Assert.Equal(NotificationKind.CaptureOn, updated.Kind);
        Assert.Equal("Keeping the last 5:00. Save:", updated.Body);
        Assert.Equal(usual.Keys, updated.Keys);
        Assert.Equal((usual.Stripe, usual.Visual, usual.VisualColor, usual.ClickOpensSettings), (updated.Stripe, updated.Visual, updated.VisualColor, updated.ClickOpensSettings));
        Assert.False(updated.ClickShowsUpdates);
    }

    [Fact]
    public void Fresh_keeps_updated_to()
    {
        var waited = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", updatedTo: "0.2.0");

        var fresh = StatusTransitions.Fresh(waited, Read(), AllGood, "0.2.0");

        Assert.Equal("Updated to 0.2.0", fresh.Title);
        Assert.Equal(NotificationStates.CaptureOn(60, "F24").Body, fresh.Body);
        Assert.Equal(["F24"], fresh.Keys);
    }

    [Fact]
    public void Without_updated_to_it_is_capture_on_again()
    {
        var waited = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", updatedTo: "0.2.0");

        Assert.Equal("Capture on", StatusTransitions.Fresh(waited, Read(), AllGood).Title);
        Assert.Equal("Capture on", StatusTransitions.CaptureOnNow(Read(), AllGood).Title);
    }

    [Fact]
    public void Capture_on_now_says_the_version_it_is_given()
    {
        Assert.Equal("Updated to 1.10.0", StatusTransitions.CaptureOnNow(Read(), AllGood, "1.10.0").Title);
    }

    [Fact]
    public void Save_hotkey_is_taken_keeps_the_version_in_the_title()
    {
        var fresh = StatusTransitions.Fresh(NotificationStates.CaptureOn(60, "F24"), Read(), Taken, "0.2.0");

        Assert.Equal("Updated to 0.2.0", fresh.Title);
        Assert.Equal("Keeping the last 1:00. Save hotkey is taken", fresh.Body);
        Assert.Empty(fresh.Keys);
        Assert.True(fresh.ClickOpensSettings);
        Assert.False(fresh.ClickShowsUpdates);
    }

    [Fact]
    public void Another_notification_that_waited_is_shown_as_it_was()
    {
        var waited = NotificationStates.UpdateReady("0.2.0", NotificationPalette.Lime);

        Assert.Same(waited, StatusTransitions.Fresh(waited, Read(), AllGood, "0.2.0"));
    }
}
