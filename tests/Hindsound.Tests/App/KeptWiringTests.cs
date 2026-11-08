using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>
/// What the app hands to the pure rules: the memory is read from the last status at the moment a text is made, never
/// carried from before. The rules themselves are tested elsewhere; this is the link between the app and them.
/// </summary>
public class KeptWiringTests
{
    private static readonly HotkeyResult[] AllGood = [new(HotkeyAction.SaveDefault, "F24", HotkeyStatus.Registered, null)];

    private static AppSettings Settings()
    {
        var settings = new AppSettings { BufferSeconds = 300 };
        settings.Hotkeys.SaveDefault = "F24";
        return settings;
    }

    private static AudioEngineStatus Holding(double seconds, CaptureActivity activity = CaptureActivity.Capturing) =>
        ScreenRig.Capturing(activity: activity) with { Buffered = TimeSpan.FromSeconds(seconds) };

    [Fact]
    public void The_kept_seconds_are_those_of_the_status_or_unknown_before_any()
    {
        Assert.Equal(12, StatusTransitions.KeptSecondsOf(Holding(12)));
        Assert.Null(StatusTransitions.KeptSecondsOf(null));
    }

    [Fact]
    public void Capture_on_from_a_status_names_what_the_memory_holds()
    {
        var content = StatusTransitions.NoticeContent(StatusNotice.CaptureOn, Holding(12), Settings(), AllGood, null);

        Assert.NotNull(content);
        Assert.Equal(NotificationStates.CaptureOn(300, "F24", null, null, 12).Body, content.Body);
        Assert.NotEqual(NotificationStates.CaptureOn(300, "F24").Body, content.Body);
    }

    [Fact]
    public void Paused_from_a_status_names_what_the_memory_holds()
    {
        var content = StatusTransitions.NoticeContent(StatusNotice.Paused, Holding(12, CaptureActivity.Paused), Settings(), AllGood, null);

        Assert.NotNull(content);
        Assert.Equal(NotificationStates.Paused(12).Body, content.Body);
        Assert.NotEqual(NotificationStates.Paused(0).Body, content.Body);
    }

    [Fact]
    public void The_other_notices_keep_their_own_words_and_the_quiet_ones_have_none()
    {
        var status = Holding(12) with { SystemDeviceName = "Desk speakers" };

        Assert.Equal(NotificationStates.NoDevice().Body, StatusTransitions.NoticeContent(StatusNotice.NoDevice, status, Settings(), AllGood, null)!.Body);
        Assert.Equal(NotificationStates.DeviceChanged("Desk speakers").Body, StatusTransitions.NoticeContent(StatusNotice.DeviceChanged, status, Settings(), AllGood, null)!.Body);
        Assert.Equal(NotificationStates.DeviceChanged("the default output").Body,
            StatusTransitions.NoticeContent(StatusNotice.DeviceChanged, status with { SystemDeviceName = null }, Settings(), AllGood, null)!.Body);
        Assert.Null(StatusTransitions.NoticeContent(StatusNotice.None, status, Settings(), AllGood, null));
        Assert.Null(StatusTransitions.NoticeContent(StatusNotice.MicLost, status, Settings(), AllGood, null));
    }

    [Fact]
    public void A_capture_on_that_waited_says_the_memory_of_now()
    {
        var waited = NotificationStates.CaptureOn(300, "F24", null, null, 290);

        var fresh = StatusTransitions.FreshFrom(waited, Settings(), AllGood, null, Holding(12));

        Assert.Equal(NotificationStates.CaptureOn(300, "F24", null, null, 12).Body, fresh.Body);
    }

    [Fact]
    public void A_capture_on_that_waited_before_any_status_does_not_promise_a_full_memory_either_way()
    {
        var waited = NotificationStates.CaptureOn(300, "F24", null, null, 290);

        var fresh = StatusTransitions.FreshFrom(waited, Settings(), AllGood, null, null);

        Assert.Equal(StatusTransitions.Fresh(waited, Settings(), AllGood).Body, fresh.Body);
    }

    [Fact]
    public void The_update_that_waited_with_a_capture_on_is_named_again()
    {
        var waited = NotificationStates.CaptureOn(300, "F24");

        var fresh = StatusTransitions.FreshFrom(waited, Settings(), AllGood, "0.2.0", Holding(12));

        Assert.Equal(NotificationStates.CaptureOn(300, "F24", null, "0.2.0", 12).Body, fresh.Body);
    }

    [Fact]
    public void Anything_else_that_waited_stays_what_it_was()
    {
        var noDevice = NotificationStates.NoDevice();

        Assert.Same(noDevice, StatusTransitions.FreshFrom(noDevice, Settings(), AllGood, null, Holding(12)));
    }

    [Fact]
    public void The_menu_reads_the_memory_and_the_pause_from_the_status()
    {
        var capturing = Holding(12);
        var paused = Holding(12, CaptureActivity.Paused);

        Assert.Equal(TrayMenu.Items(false, true, false, "Update to 0.2.0...", capturing.Buffered), TrayMenu.ItemsFor(capturing, true, false, "Update to 0.2.0..."));
        Assert.Equal(TrayMenu.Items(true, false, false, null, paused.Buffered), TrayMenu.ItemsFor(paused, false, false, null));
        Assert.Equal(TrayMenu.Items(false, false, false), TrayMenu.ItemsFor(null, false, false, null));
    }

    [Fact]
    public void The_menu_from_a_status_says_only_what_is_kept_and_greys_what_is_empty()
    {
        var short12 = TrayMenu.ItemsFor(Holding(12), false, false, null);
        var empty = TrayMenu.ItemsFor(Holding(0.4), false, false, null);

        Assert.Contains(short12, item => item.Command == TrayCommand.Save5m && item.Text.Contains("only 0:12", StringComparison.Ordinal) && item.Enabled);
        Assert.All(empty.Where(item => item.Command is TrayCommand.Save30s or TrayCommand.Save5m), item => Assert.False(item.Enabled));
    }
}
