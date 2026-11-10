using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>Quit during the writing of a clip, in exclusive full screen: what waits for the desktop keeps the app no longer than its clip does.</summary>
public class LeaveWithWaitingStatusTests
{
    private static readonly NotificationContent Closing = NotificationStates.ClosingAfterClip();
    private static readonly NotificationContent CaptureOn = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10");
    private static readonly NotificationContent OrdinaryClip = NotificationStates.NothingToSave();
    private static readonly NotificationContent FailedClip = NotificationStates.SaveFailed();

    private readonly PendingNotices _pending = new(_ => { });
    private readonly LeaveFlow _leave = new();

    public LeaveWithWaitingStatusTests() => _leave.Asked(clipsBusy: true);

    [Fact]
    public void A_silent_clip_given_up_lets_the_app_go_though_the_closing_message_waited()
    {
        _pending.AddStatus(Closing, 0);
        _pending.AddClip(OrdinaryClip, important: false, 0);

        Assert.True(_leave.HoldForNotice(lastClipHasProblem: true, onScreen: false, _pending));
        Assert.False(_leave.NoticeGone(onScreen: false, _pending));

        _pending.GiveUpOld(PendingNotices.OrdinaryClipLimitMs);

        Assert.True(_leave.NoticeGone(onScreen: false, _pending));
        Assert.False(_pending.Any);
    }

    [Fact]
    public void The_closing_message_no_longer_waits_once_the_last_clip_is_done()
    {
        _pending.AddStatus(Closing, 0);

        Assert.False(_leave.HoldForNotice(lastClipHasProblem: false, onScreen: false, _pending));

        Assert.Null(_pending.TakeNext(0));
    }

    [Fact]
    public void Another_waiting_status_stays_for_the_desktop_but_keeps_nothing()
    {
        _pending.AddStatus(CaptureOn, 0);
        _pending.AddClip(OrdinaryClip, important: false, 0);
        Assert.True(_leave.HoldForNotice(lastClipHasProblem: true, onScreen: false, _pending));

        _pending.GiveUpOld(PendingNotices.OrdinaryClipLimitMs);

        Assert.True(_leave.NoticeGone(onScreen: false, _pending));
        Assert.True(_leave.NoticeLimit(_pending));
        Assert.Same(CaptureOn, _pending.TakeNext(PendingNotices.OrdinaryClipLimitMs));
    }

    [Fact]
    public void An_important_clip_keeps_the_app_past_the_limit_of_an_ordinary_one()
    {
        _pending.AddClip(FailedClip, important: true, 0);
        Assert.True(_leave.HoldForNotice(lastClipHasProblem: true, onScreen: false, _pending));

        _pending.GiveUpOld(PendingNotices.OrdinaryClipLimitMs);

        Assert.False(_leave.NoticeGone(onScreen: false, _pending));
        Assert.False(_leave.NoticeLimit(_pending));
    }

    [Fact]
    public void Only_a_clip_counts_as_a_clip_waiting()
    {
        Assert.False(_pending.ClipWaiting);

        _pending.AddStatus(Closing, 0);
        Assert.False(_pending.ClipWaiting);

        _pending.AddClip(OrdinaryClip, important: false, 0);
        Assert.True(_pending.ClipWaiting);

        _pending.NewerClipShown();
        Assert.False(_pending.ClipWaiting);

        _pending.AddClip(FailedClip, important: true, 0);
        Assert.True(_pending.ClipWaiting);
    }
}
