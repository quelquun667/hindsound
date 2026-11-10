using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>
/// The announcement of a new version waits for the desktop in a place of its own: it never takes the place of "Capture on",
/// never comes before a status or on top of a clip with a problem, and never keeps an app that is leaving.
/// </summary>
public class PendingUpdateTests
{
    private static readonly NotificationContent Update = NotificationStates.UpdateReady("0.2.0", NotificationPalette.Lime);
    private static readonly NotificationContent CaptureOn = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10");
    private static readonly NotificationContent FailedClip = NotificationStates.SaveFailed();
    private static readonly NotificationContent OrdinaryClip = NotificationStates.Saving(TimeSpan.FromSeconds(30));

    private readonly List<string> _journal = [];
    private readonly PendingNotices _pending;

    public PendingUpdateTests() => _pending = new PendingNotices(_journal.Add);

    [Fact]
    public void The_update_comes_after_the_status_and_the_clip_with_a_problem()
    {
        _pending.AddUpdate(Update, 0);
        _pending.AddClip(FailedClip, important: true, 0);
        _pending.AddStatus(CaptureOn, 0);

        Assert.Same(CaptureOn, _pending.TakeNext(0));
        Assert.Same(FailedClip, _pending.TakeNext(0));
        Assert.Same(Update, _pending.TakeNext(0));
        Assert.Null(_pending.TakeNext(0));
    }

    [Fact]
    public void The_update_comes_after_an_ordinary_clip()
    {
        _pending.AddClip(OrdinaryClip, important: false, 0);
        _pending.AddUpdate(Update, 0);

        Assert.Same(OrdinaryClip, _pending.TakeNext(0));
        Assert.Same(Update, _pending.TakeNext(0));
        Assert.Null(_pending.TakeNext(0));
    }

    [Fact]
    public void The_update_alone_comes_out()
    {
        _pending.AddUpdate(Update, 0);

        Assert.Same(Update, _pending.TakeNext(0));
        Assert.False(_pending.Any);
    }

    [Fact]
    public void Capture_on_is_not_written_over()
    {
        _pending.AddStatus(CaptureOn, 0);
        _pending.AddUpdate(Update, 10);

        Assert.Same(CaptureOn, _pending.TakeNext(10));
        Assert.Same(Update, _pending.TakeNext(10));
    }

    [Fact]
    public void A_waiting_update_is_something_to_look_at_the_screen_for_but_no_clip()
    {
        _pending.AddUpdate(Update, 0);

        Assert.True(_pending.Any);
        Assert.False(_pending.ClipWaiting);
        Assert.False(_pending.ImportantClipWaiting);
    }

    [Fact]
    public void An_arrival_starts_the_fast_pace_again()
    {
        _pending.AddUpdate(Update, 100_000);

        Assert.Equal(PendingNotices.FastPollMs, _pending.PollDelayMs(100_000));
        Assert.Equal(PendingNotices.SlowPollMs, _pending.PollDelayMs(100_000 + PendingNotices.FastPollingLimitMs));
    }

    [Fact]
    public void A_later_announcement_replaces_the_one_before()
    {
        var later = NotificationStates.UpdateAvailable("0.3.0", NotificationPalette.Lime);
        _pending.AddUpdate(Update, 0);
        _pending.AddUpdate(later, 0);

        Assert.Same(later, _pending.TakeNext(0));
        Assert.Null(_pending.TakeNext(0));
    }

    [Fact]
    public void A_withdrawn_update_no_longer_waits_and_says_so()
    {
        _pending.AddUpdate(Update, 0);
        _pending.AddStatus(CaptureOn, 0);

        _pending.WithdrawUpdate();

        Assert.Same(CaptureOn, _pending.TakeNext(0));
        Assert.Null(_pending.TakeNext(0));
        Assert.False(_pending.Any);
        Assert.Contains(_journal, line => line.Contains("UpdateReady no longer waits"));
    }

    [Fact]
    public void Withdrawing_nothing_says_nothing()
    {
        _pending.WithdrawUpdate();

        Assert.Empty(_journal);
    }

    [Fact]
    public void The_update_is_never_given_up_like_an_old_clip()
    {
        _pending.AddUpdate(Update, 0);

        _pending.GiveUpOld(10 * PendingNotices.OrdinaryClipLimitMs);

        Assert.Same(Update, _pending.TakeNext(10 * PendingNotices.OrdinaryClipLimitMs));
    }

    [Fact]
    public void A_waiting_announcement_does_not_keep_an_app_that_leaves()
    {
        var leave = new LeaveFlow();
        leave.Asked(clipsBusy: true);
        _pending.AddClip(OrdinaryClip, important: false, 0);
        _pending.AddUpdate(Update, 0);
        Assert.True(leave.HoldForNotice(lastClipHasProblem: true, onScreen: false, _pending));

        _pending.GiveUpOld(PendingNotices.OrdinaryClipLimitMs);

        Assert.True(_pending.Any);
        Assert.True(leave.NoticeGone(onScreen: false, _pending));
        Assert.True(leave.NoticeLimit(_pending));
    }
}
