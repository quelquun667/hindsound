using Hindsound.App;

namespace Hindsound.Tests.App;

/// <summary>A notification about the settings waits for as long as a game in exclusive full screen lasts.</summary>
public sealed class NoticeRetryTests
{
    [Fact]
    public void It_starts_at_the_fast_pace()
    {
        Assert.Equal(PendingNotices.FastPollMs, new NoticeRetry().Arm(1000));
    }

    [Fact]
    public void Only_one_look_at_a_time_is_on_its_way()
    {
        var retry = new NoticeRetry();
        retry.Arm(1000);

        Assert.Null(retry.Arm(1100));

        retry.Fired();
        Assert.NotNull(retry.Arm(1200));
    }

    [Fact]
    public void Past_the_fast_time_the_pace_drops()
    {
        var retry = new NoticeRetry();
        retry.Arm(0);
        retry.Fired();

        Assert.Equal(PendingNotices.SlowPollMs, retry.Arm(PendingNotices.FastPollingLimitMs));
    }

    [Fact]
    public void It_never_stops_looking_however_long_the_game_lasts()
    {
        var retry = new NoticeRetry();
        var hours = 6 * 3600 * 1000L;
        var looks = 0;
        for (var now = 0L; now < hours; now += PendingNotices.SlowPollMs)
        {
            if (retry.Arm(now) is not null)
            {
                looks++;
            }

            retry.Fired();
        }

        Assert.Equal(hours / PendingNotices.SlowPollMs, looks);
    }

    [Fact]
    public void Once_shown_the_next_one_starts_at_the_fast_pace_again()
    {
        var retry = new NoticeRetry();
        retry.Arm(0);
        retry.Fired();
        retry.Shown();

        Assert.Equal(PendingNotices.FastPollMs, retry.Arm(10 * PendingNotices.FastPollingLimitMs));
    }

    [Fact]
    public void A_waiting_notification_keeps_counting_from_its_first_look()
    {
        var retry = new NoticeRetry();
        retry.Arm(0);
        retry.Fired();
        retry.Arm(PendingNotices.FastPollingLimitMs - 1);
        retry.Fired();

        Assert.Equal(PendingNotices.SlowPollMs, retry.Arm(PendingNotices.FastPollingLimitMs));
    }
}
