using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>A status in line behind a lasting notice, that stops being true before its turn comes.</summary>
public class NoticeCeasedTests
{
    private static readonly NotificationContent Damaged = NotificationStates.SettingsDamaged(copyKept: true);

    private static AudioEngineStatus Status(CaptureActivity activity, bool micEnabled = false, bool micCapturing = false) =>
        new(activity, true, micEnabled, micCapturing, "Speakers", null, true, TimeSpan.Zero, 0);

    private static NoticeQueue BehindALastingNotice(NotificationContent status)
    {
        var queue = new NoticeQueue();
        queue.Shown(Damaged);
        Assert.False(queue.ShowsNow(status));
        return queue;
    }

    [Fact]
    public void A_status_no_longer_true_never_shows()
    {
        (NotificationContent Status, AudioEngineStatus Now)[] cases =
        [
            (NotificationStates.CaptureOn(300, "F24"), Status(CaptureActivity.Paused)),
            (NotificationStates.NoDevice(), Status(CaptureActivity.Capturing)),
            (NotificationStates.MicLost(), Status(CaptureActivity.Capturing, micEnabled: true, micCapturing: true)),
        ];

        foreach (var (status, now) in cases)
        {
            var queue = BehindALastingNotice(status);

            queue.WithdrawCeased(now);
            queue.Finished();

            Assert.Null(queue.Next());
        }
    }

    [Fact]
    public void A_status_still_true_keeps_its_place()
    {
        var capture = NotificationStates.CaptureOn(300, "F24");
        var queue = BehindALastingNotice(capture);

        queue.WithdrawCeased(Status(CaptureActivity.Capturing));
        queue.Finished();

        Assert.Same(capture, queue.Next());
    }

    [Fact]
    public void A_lasting_notice_in_line_is_not_touched()
    {
        var queue = new NoticeQueue();
        queue.Add(Damaged);

        queue.WithdrawCeased(Status(CaptureActivity.Paused));

        Assert.Same(Damaged, queue.Next());
    }
}
