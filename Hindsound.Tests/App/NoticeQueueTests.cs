using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

public class NoticeQueueTests
{
    private static readonly NotificationContent Damaged = NotificationStates.SettingsDamaged(copyKept: true);
    private static readonly NotificationContent NotSaved = NotificationStates.SettingsNotSaved();
    private static readonly NotificationContent CaptureOn = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10");
    private static readonly NotificationContent Paused = NotificationStates.Paused();
    private static readonly NotificationContent Answer = NotificationStates.NothingToSave();

    private static NotificationContent? ShowNext(NoticeQueue queue)
    {
        if (queue.Next() is not { } next)
        {
            return null;
        }

        queue.Remove(next);
        queue.Shown(next);
        return next;
    }

    [Theory]
    [InlineData(NotificationKind.SettingsDamaged, NoticeRank.Lasting)]
    [InlineData(NotificationKind.SettingsUnreadable, NoticeRank.Lasting)]
    [InlineData(NotificationKind.SettingsHasError, NoticeRank.Lasting)]
    [InlineData(NotificationKind.SettingsNotSaved, NoticeRank.Lasting)]
    [InlineData(NotificationKind.StartupNotChanged, NoticeRank.Lasting)]
    [InlineData(NotificationKind.CaptureOn, NoticeRank.Ordinary)]
    [InlineData(NotificationKind.Paused, NoticeRank.Ordinary)]
    [InlineData(NotificationKind.NoDevice, NoticeRank.Ordinary)]
    [InlineData(NotificationKind.DeviceChanged, NoticeRank.Ordinary)]
    [InlineData(NotificationKind.Saved, NoticeRank.Answer)]
    [InlineData(NotificationKind.SaveFailed, NoticeRank.Answer)]
    [InlineData(NotificationKind.Saving, NoticeRank.Answer)]
    public void Each_kind_has_its_rank(NotificationKind kind, NoticeRank rank)
    {
        Assert.Equal(rank, NoticeQueue.RankOf(kind));
    }

    [Fact]
    public void A_status_with_nothing_on_screen_shows_at_once()
    {
        Assert.True(new NoticeQueue().ShowsNow(CaptureOn));
    }

    [Fact]
    public void Capture_on_does_not_wipe_out_a_lasting_notice_and_comes_after_it()
    {
        var queue = new NoticeQueue();
        queue.Add(Damaged);
        Assert.Same(Damaged, ShowNext(queue));

        Assert.False(queue.ShowsNow(CaptureOn));
        Assert.Null(queue.Next());

        queue.Finished();
        Assert.Same(CaptureOn, ShowNext(queue));
    }

    [Fact]
    public void Lasting_notices_come_one_after_the_other_in_order()
    {
        var queue = new NoticeQueue();
        queue.Add(Damaged);
        queue.Add(NotSaved);

        Assert.Same(Damaged, ShowNext(queue));
        Assert.Null(queue.Next());

        queue.Finished();
        Assert.Same(NotSaved, ShowNext(queue));

        queue.Finished();
        Assert.Null(queue.Next());
    }

    [Fact]
    public void A_lasting_notice_replaces_a_status_on_screen()
    {
        var queue = new NoticeQueue();
        queue.Shown(CaptureOn);

        queue.Add(Damaged);

        Assert.Same(Damaged, queue.Next());
    }

    [Fact]
    public void A_status_replaces_a_status_on_screen()
    {
        var queue = new NoticeQueue();
        queue.Shown(CaptureOn);

        Assert.True(queue.ShowsNow(Paused));
    }

    [Fact]
    public void Only_the_latest_status_waits()
    {
        var queue = new NoticeQueue();
        queue.Shown(Damaged);

        queue.ShowsNow(CaptureOn);
        queue.ShowsNow(Paused);

        queue.Finished();
        Assert.Same(Paused, ShowNext(queue));
        queue.Finished();
        Assert.Null(queue.Next());
    }

    [Fact]
    public void An_answer_shows_at_once_and_the_lasting_notice_starts_over_after_it()
    {
        var queue = new NoticeQueue();
        queue.Add(NotSaved);
        queue.Shown(Damaged);

        Assert.True(queue.ShowsNow(Answer));
        queue.Shown(Answer);
        Assert.Null(queue.Next());

        queue.Finished();
        Assert.Same(Damaged, ShowNext(queue));
        queue.Finished();
        Assert.Same(NotSaved, ShowNext(queue));
    }

    [Fact]
    public void A_lasting_notice_waits_for_the_answer_on_screen()
    {
        var queue = new NoticeQueue();
        queue.Shown(Answer);

        queue.Add(Damaged);

        Assert.Null(queue.Next());
        queue.Finished();
        Assert.Same(Damaged, queue.Next());
    }

    [Fact]
    public void A_status_waits_for_the_answer_on_screen()
    {
        var queue = new NoticeQueue();
        queue.Shown(Answer);

        Assert.False(queue.ShowsNow(Paused));
    }

    [Fact]
    public void Next_leaves_it_in_line_until_it_is_removed()
    {
        var queue = new NoticeQueue();
        queue.Add(Damaged);

        Assert.Same(Damaged, queue.Next());
        Assert.Same(Damaged, queue.Next());

        queue.Remove(Damaged);
        Assert.Null(queue.Next());
    }

    [Fact]
    public void A_status_given_up_on_leaves_the_line()
    {
        var queue = new NoticeQueue();
        queue.Add(Paused);

        queue.Remove(Paused);

        Assert.Null(queue.Next());
    }
}
