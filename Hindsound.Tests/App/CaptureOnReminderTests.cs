using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>Started without an output, the headset arrives: what comes from there must be the reminder, not a mere "device changed".</summary>
public class CaptureOnReminderTests
{
    private static AudioEngineStatus Status(CaptureActivity activity, string? device = "Headset") =>
        new(activity, true, false, false, device, null, true, TimeSpan.Zero, 0);

    private readonly CaptureOnReminder _reminder = new();

    private StatusNotice Step(CaptureActivity? from, CaptureActivity to)
    {
        var next = Status(to);
        return _reminder.Adjust(StatusTransitions.Decide(from is { } activity ? Status(activity) : null, next), next);
    }

    [Fact]
    public void A_device_that_arrives_after_a_start_without_one_brings_the_reminder()
    {
        Assert.Equal(StatusNotice.NoDevice, Step(null, CaptureActivity.NoDevice));
        Assert.Equal(StatusNotice.CaptureOn, Step(CaptureActivity.NoDevice, CaptureActivity.Capturing));
    }

    [Fact]
    public void Once_it_has_shown_a_device_that_arrives_is_only_a_change()
    {
        Step(null, CaptureActivity.Capturing);
        _reminder.Shown(NotificationKind.CaptureOn);

        Assert.Equal(StatusNotice.NoDevice, Step(CaptureActivity.Capturing, CaptureActivity.NoDevice));
        Assert.Equal(StatusNotice.DeviceChanged, Step(CaptureActivity.NoDevice, CaptureActivity.Capturing));
    }

    [Fact]
    public void A_reminder_replaced_before_it_could_show_is_still_due()
    {
        Step(null, CaptureActivity.Capturing);
        _reminder.Shown(NotificationKind.NoDevice);

        Assert.Equal(StatusNotice.CaptureOn, Step(CaptureActivity.NoDevice, CaptureActivity.Capturing));
    }

    [Fact]
    public void A_pause_makes_it_due_again_even_if_the_device_goes_during_the_pause()
    {
        Step(null, CaptureActivity.Capturing);
        _reminder.Shown(NotificationKind.CaptureOn);

        Assert.Equal(StatusNotice.Paused, Step(CaptureActivity.Capturing, CaptureActivity.Paused));
        Assert.Equal(StatusNotice.NoDevice, Step(CaptureActivity.Paused, CaptureActivity.NoDevice));
        Assert.Equal(StatusNotice.CaptureOn, Step(CaptureActivity.NoDevice, CaptureActivity.Capturing));
    }

    [Fact]
    public void It_changes_nothing_else()
    {
        Assert.Equal(StatusNotice.None, _reminder.Adjust(StatusNotice.None, Status(CaptureActivity.Capturing)));
        Assert.Equal(StatusNotice.NoDevice, _reminder.Adjust(StatusNotice.NoDevice, Status(CaptureActivity.NoDevice)));
    }
}
