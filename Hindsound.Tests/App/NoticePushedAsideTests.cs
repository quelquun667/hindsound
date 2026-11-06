using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>A settings notice replaces a status on screen, as it should: a status that must reach the user then comes back.</summary>
public class NoticePushedAsideTests
{
    private static readonly NotificationContent Damaged = NotificationStates.SettingsDamaged(copyKept: true);

    private static AudioEngineStatus Status(CaptureActivity activity) =>
        new(activity, true, true, false, "Speakers", null, true, TimeSpan.Zero, 0);

    /// <summary>What the app does when the lasting notice takes the screen: gives what comes back once it has left.</summary>
    private static NotificationContent? AfterTheLastingNotice(NotificationContent onScreen, AudioEngineStatus? status, NoticeQueue? queue = null)
    {
        queue ??= new NoticeQueue();
        queue.Shown(onScreen);
        queue.Add(Damaged);
        var next = queue.Next();
        Assert.Same(Damaged, next);
        queue.Remove(next!);
        queue.PushedAside(onScreen, status);
        queue.Shown(next!);
        Assert.Null(queue.Next());
        queue.Finished();
        return queue.Next();
    }

    [Fact]
    public void Capture_on_comes_back()
    {
        var capture = NotificationStates.CaptureOn(300, "F24");

        Assert.Same(capture, AfterTheLastingNotice(capture, Status(CaptureActivity.Capturing)));
    }

    [Fact]
    public void No_audio_device_comes_back()
    {
        var noDevice = NotificationStates.NoDevice();

        Assert.Same(noDevice, AfterTheLastingNotice(noDevice, Status(CaptureActivity.NoDevice)));
    }

    [Fact]
    public void A_lost_microphone_comes_back()
    {
        var lost = NotificationStates.MicLost();

        Assert.Same(lost, AfterTheLastingNotice(lost, Status(CaptureActivity.Capturing)));
    }

    [Fact]
    public void An_ordinary_status_does_not()
    {
        Assert.Null(AfterTheLastingNotice(NotificationStates.Paused(), Status(CaptureActivity.Paused)));
        Assert.Null(AfterTheLastingNotice(NotificationStates.DeviceChanged("Headset"), Status(CaptureActivity.Capturing)));
    }

    [Fact]
    public void A_status_that_has_stopped_being_true_does_not()
    {
        Assert.Null(AfterTheLastingNotice(NotificationStates.CaptureOn(300, "F24"), Status(CaptureActivity.Paused)));
    }

    [Fact]
    public void One_that_has_since_stopped_being_true_is_taken_out_on_the_way()
    {
        var queue = new NoticeQueue();
        var capture = NotificationStates.CaptureOn(300, "F24");
        queue.PushedAside(capture, Status(CaptureActivity.Capturing));

        queue.WithdrawCeased(Status(CaptureActivity.Paused));

        Assert.Null(queue.Next());
    }

    [Fact]
    public void A_newer_status_in_line_keeps_its_place()
    {
        var queue = new NoticeQueue();
        var newer = NotificationStates.NoDevice();
        queue.Add(newer);

        queue.PushedAside(NotificationStates.CaptureOn(300, "F24"), Status(CaptureActivity.Capturing));

        Assert.Same(newer, queue.Next());
    }

    [Fact]
    public void The_closing_message_is_an_answer_and_never_comes_back_this_way()
    {
        var queue = new NoticeQueue();

        queue.PushedAside(NotificationStates.ClosingAfterClip(), Status(CaptureActivity.Capturing));

        Assert.Null(queue.Next());
    }
}
