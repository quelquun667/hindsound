using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>"Capture on" waits for the desktop, the settings are read again meanwhile: what it says must be what is true now.</summary>
public class CaptureOnFreshTests
{
    private static readonly HotkeyResult[] AllGood = [new(HotkeyAction.SaveDefault, "F24", HotkeyStatus.Registered, null)];

    private static AppSettings Read()
    {
        var settings = new AppSettings { BufferSeconds = 60 };
        settings.Hotkeys.SaveDefault = "F24";
        return settings;
    }

    [Fact]
    public void A_capture_on_that_waited_says_the_length_and_keys_of_now()
    {
        var waited = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", "Save hotkey is taken");

        var fresh = StatusTransitions.Fresh(waited, Read(), AllGood);

        Assert.Equal(NotificationKind.CaptureOn, fresh.Kind);
        Assert.Equal(NotificationStates.CaptureOn(60, "F24").Body, fresh.Body);
        Assert.Equal(["F24"], fresh.Keys);
        Assert.DoesNotContain("taken", fresh.Body);
    }

    [Fact]
    public void The_keys_that_stopped_working_are_said_too()
    {
        var waited = NotificationStates.CaptureOn(60, "F24");
        HotkeyResult[] taken = [new(HotkeyAction.SaveDefault, "F24", HotkeyStatus.InUse, HotkeyResult.InUseMessage)];

        var fresh = StatusTransitions.Fresh(waited, Read(), taken);

        Assert.Contains("Save hotkey is taken", fresh.Body);
        Assert.Empty(fresh.Keys);
    }

    [Fact]
    public void Anything_else_stays_what_it_was()
    {
        var noDevice = NotificationStates.NoDevice();

        Assert.Same(noDevice, StatusTransitions.Fresh(noDevice, Read(), AllGood));
    }
}
