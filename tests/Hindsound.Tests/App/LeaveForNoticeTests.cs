using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>Quit during the writing of a clip that ends badly: the app must not leave before the user could read about it.</summary>
public class LeaveForNoticeTests
{
    private static LeaveFlow Waiting()
    {
        var leave = new LeaveFlow();
        leave.Asked(clipsBusy: true);
        return leave;
    }

    [Theory]
    [InlineData(NotificationKind.Silent, false, true)]
    [InlineData(NotificationKind.SaveFailed, true, true)]
    [InlineData(NotificationKind.Saved, true, true)]
    [InlineData(NotificationKind.Saved, false, false)]
    public void What_counts_as_a_problem(NotificationKind kind, bool important, bool expected)
    {
        Assert.Equal(expected, LeaveFlow.IsProblem(kind, important));
    }

    [Fact]
    public void A_problem_on_screen_keeps_the_app_until_it_has_left()
    {
        var leave = Waiting();

        Assert.True(leave.HoldForNotice(lastClipHasProblem: true, onScreen: true, anyWaiting: false, importantClipWaiting: false));
        Assert.False(leave.NoticeGone(onScreen: true, anyWaiting: false));
        Assert.True(leave.NoticeGone(onScreen: false, anyWaiting: false));
    }

    [Fact]
    public void A_good_clip_leaves_at_once_even_with_the_closing_message_on_screen()
    {
        var leave = Waiting();

        Assert.False(leave.HoldForNotice(lastClipHasProblem: false, onScreen: true, anyWaiting: false, importantClipWaiting: false));
        Assert.False(leave.NoticeGone(onScreen: false, anyWaiting: false));
        Assert.False(leave.NoticeLimit(anyWaiting: false));
    }

    [Fact]
    public void A_problem_that_shows_nowhere_keeps_nothing()
    {
        var leave = Waiting();

        Assert.False(leave.HoldForNotice(lastClipHasProblem: true, onScreen: false, anyWaiting: false, importantClipWaiting: false));
    }

    [Fact]
    public void A_problem_waiting_for_the_desktop_keeps_the_app_even_if_a_later_clip_went_well()
    {
        var leave = Waiting();

        Assert.True(leave.HoldForNotice(lastClipHasProblem: false, onScreen: false, anyWaiting: true, importantClipWaiting: true));
        Assert.False(leave.NoticeGone(onScreen: false, anyWaiting: true));
        Assert.False(leave.NoticeLimit(anyWaiting: true));
        Assert.True(leave.NoticeGone(onScreen: false, anyWaiting: false));
    }

    [Fact]
    public void A_silent_clip_waiting_for_the_desktop_keeps_the_app_until_it_is_given_up()
    {
        var leave = Waiting();

        Assert.True(leave.HoldForNotice(lastClipHasProblem: true, onScreen: false, anyWaiting: true, importantClipWaiting: false));
        Assert.True(leave.NoticeGone(onScreen: false, anyWaiting: false));
    }

    [Fact]
    public void The_limit_on_screen_leaves_even_if_the_notification_never_says_it_has_left()
    {
        var leave = Waiting();
        leave.HoldForNotice(lastClipHasProblem: true, onScreen: true, anyWaiting: false, importantClipWaiting: false);

        Assert.True(leave.NoticeLimit(anyWaiting: false));
    }

    [Fact]
    public void A_second_quit_and_the_limit_of_the_wait_still_leave_during_the_hold()
    {
        var leave = Waiting();
        leave.HoldForNotice(lastClipHasProblem: true, onScreen: true, anyWaiting: false, importantClipWaiting: false);

        Assert.Equal(LeaveStep.LeaveNow, leave.Asked(clipsBusy: false));
        Assert.True(leave.TimeIsUp());
    }

    [Fact]
    public void With_no_quit_asked_nothing_is_ever_held()
    {
        var leave = new LeaveFlow();

        Assert.False(leave.HoldForNotice(lastClipHasProblem: true, onScreen: true, anyWaiting: true, importantClipWaiting: true));
        Assert.False(leave.NoticeGone(onScreen: false, anyWaiting: false));
    }
}
