using Hindsound.App;
using Hindsound.Input;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

public class SecondLaunchTests
{
    [Fact]
    public void A_plain_launch_wakes_the_running_instance()
    {
        Assert.True(Program.WakesTheFirstInstance(AppOptions.Parse([])));
    }

    [Fact]
    public void A_run_with_its_own_settings_never_does()
    {
        Assert.False(Program.WakesTheFirstInstance(AppOptions.Parse(["--settings", "test.json", "--offscreen"])));
    }
}

public class SlowSaveOptionTests
{
    [Fact]
    public void The_option_is_read_and_off_by_default()
    {
        Assert.Equal(40, AppOptions.Parse(["--selftest-slow-save", "40"]).SelfTestSlowSaveSeconds);
        Assert.Null(AppOptions.Parse(["--selftest-slow-save", "nonsense"]).SelfTestSlowSaveSeconds);
        Assert.Null(AppOptions.Parse([]).SelfTestSlowSaveSeconds);
    }
}

public class LeaveFlowTests
{
    [Fact]
    public void With_no_clip_being_written_quit_leaves_at_once()
    {
        var leave = new LeaveFlow();

        Assert.Equal(LeaveStep.LeaveNow, leave.Asked(clipsBusy: false));
        Assert.False(leave.Waiting);
    }

    [Fact]
    public void With_a_clip_being_written_quit_waits()
    {
        var leave = new LeaveFlow();

        Assert.Equal(LeaveStep.WaitForClips, leave.Asked(clipsBusy: true));
        Assert.True(leave.Waiting);
    }

    [Fact]
    public void A_second_quit_during_the_wait_leaves_at_once()
    {
        var leave = new LeaveFlow();
        leave.Asked(clipsBusy: true);

        Assert.Equal(LeaveStep.LeaveNow, leave.Asked(clipsBusy: true));
    }

    [Fact]
    public void The_wait_ends_with_the_last_clip_only()
    {
        var leave = new LeaveFlow();
        leave.Asked(clipsBusy: true);

        Assert.False(leave.ClipEnded(clipsBusy: true));
        Assert.True(leave.ClipEnded(clipsBusy: false));
    }

    [Fact]
    public void A_clip_that_ends_with_no_quit_asked_leaves_nothing()
    {
        Assert.False(new LeaveFlow().ClipEnded(clipsBusy: false));
    }

    [Fact]
    public void The_limit_only_counts_during_a_wait()
    {
        var leave = new LeaveFlow();
        Assert.False(leave.TimeIsUp());

        leave.Asked(clipsBusy: true);

        Assert.True(leave.TimeIsUp());
        Assert.Equal(TimeSpan.FromMinutes(5), LeaveFlow.WaitLimit);
    }

    [Fact]
    public void The_settings_do_not_open_during_the_wait()
    {
        var leave = new LeaveFlow();
        Assert.True(leave.MayOpenSettings);

        leave.Asked(clipsBusy: true);

        Assert.False(leave.MayOpenSettings);
    }
}

public class TrayMenuTests
{
    [Fact]
    public void During_the_wait_the_menu_offers_only_quit()
    {
        var items = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: true);

        var quit = Assert.Single(items);
        Assert.Equal(TrayCommand.Quit, quit.Command);
        Assert.Equal("Quit", quit.Text);
        Assert.True(quit.Enabled);
    }

    [Fact]
    public void Otherwise_the_menu_offers_everything()
    {
        var items = TrayMenu.Items(paused: true, hasLastClip: false, closingAfterClip: false);

        Assert.Equal(
            [TrayCommand.OpenHindsound, TrayCommand.Library, TrayCommand.Save30s, TrayCommand.Save15s, TrayCommand.Save1m, TrayCommand.Save5m, TrayCommand.None, TrayCommand.CopyLast, TrayCommand.TogglePause, TrayCommand.OpenFolder, TrayCommand.Settings, TrayCommand.None, TrayCommand.Quit],
            items.Select(item => item.Command));
        Assert.False(items.Single(item => item.Command == TrayCommand.CopyLast).Enabled);
        Assert.Equal("Resume capture", items.Single(item => item.Command == TrayCommand.TogglePause).Text);
    }
}

public class ClosingNoticeTests
{
    [Fact]
    public void The_words_are_the_ones_decided()
    {
        var content = NotificationStates.ClosingAfterClip();

        Assert.Equal(NotificationKind.ClosingAfterClip, content.Kind);
        Assert.Equal("Saving your clip", content.Title);
        Assert.Equal("Hindsound closes when it's done", content.Body);
    }

    [Fact]
    public void It_answers_the_user_and_is_said_whatever_the_notifications_setting()
    {
        Assert.Equal(NoticeRank.Answer, NoticeQueue.RankOf(NotificationKind.ClosingAfterClip));
        Assert.True(PendingNotices.IsImportantStatus(NotificationKind.ClosingAfterClip));
    }

    [Fact]
    public void The_tip_says_it_before_any_problem_and_the_icon_stays_what_it_was()
    {
        var hotkeys = new[] { new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+F10", HotkeyStatus.InUse, HotkeyResult.InUseMessage) };

        var (state, tip) = StatusTransitions.TrayOf(null, hotkeys, importantClipWaiting: true, closingAfterClip: true);

        Assert.Equal("Hindsound - finishing a clip, then closing", tip);
        Assert.Equal(TrayState.Error, state);
    }
}

public class SessionEndBlockTests
{
    [Fact]
    public void No_clip_means_no_reason_to_give()
    {
        var block = new SessionEndBlock();

        Assert.False(block.Asked(clipsBusy: false));
        Assert.False(block.Over());
    }

    [Fact]
    public void A_clip_being_written_gives_the_reason_once_and_takes_it_back_once()
    {
        var block = new SessionEndBlock();

        Assert.True(block.Asked(clipsBusy: true));
        Assert.False(block.Asked(clipsBusy: true));
        Assert.True(block.Over());
        Assert.False(block.Over());
    }

    [Fact]
    public void A_session_that_went_on_can_end_later_with_a_new_reason()
    {
        var block = new SessionEndBlock();
        block.Asked(clipsBusy: true);
        block.Over();

        Assert.True(block.Asked(clipsBusy: true));
    }

    [Fact]
    public void The_words_and_the_limit_are_the_ones_decided()
    {
        Assert.Equal("Saving your clip", SessionEndBlock.Reason);
        Assert.Equal(TimeSpan.FromSeconds(15), SessionEndBlock.ClipsLimit);
    }
}
