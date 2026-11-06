using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public class TrayPresenceTests
{
    private sealed class Shell
    {
        public bool AddWorks { get; set; } = true;
        public bool ModifyWorks { get; set; } = true;
        public List<(uint Message, uint Flags)> Calls { get; } = [];

        public bool Call(uint message, uint flags)
        {
            Calls.Add((message, flags));
            return message switch
            {
                Native.NIM_ADD => AddWorks,
                Native.NIM_MODIFY => ModifyWorks,
                _ => true,
            };
        }

        public int CountOf(uint message) => Calls.Count(c => c.Message == message);
    }

    [Fact]
    public void An_add_sets_the_version_the_messages_depend_on()
    {
        var shell = new Shell();

        new TrayPresence(shell.Call).Add();

        Assert.Equal(1, shell.CountOf(Native.NIM_SETVERSION));
        Assert.Equal(Native.NIM_SETVERSION, shell.Calls[^1].Message);
    }

    [Fact]
    public void A_second_add_that_is_refused_still_leaves_an_icon_to_delete()
    {
        var shell = new Shell();
        var presence = new TrayPresence(shell.Call);
        presence.Add();

        // TaskbarCreated with the icon still there: NIM_ADD refuses it.
        shell.AddWorks = false;
        Assert.True(presence.Add());
        presence.Close();

        Assert.Equal(1, shell.CountOf(Native.NIM_DELETE));
    }

    [Fact]
    public void An_add_that_fails_entirely_does_not_forget_an_icon_seen_before()
    {
        var shell = new Shell();
        var presence = new TrayPresence(shell.Call);
        presence.Add();

        shell.AddWorks = false;
        shell.ModifyWorks = false;

        Assert.False(presence.Add());
        Assert.True(presence.Added);
        presence.Close();
        Assert.Equal(1, shell.CountOf(Native.NIM_DELETE));
    }

    [Fact]
    public void A_refused_add_counts_if_modify_finds_the_icon()
    {
        var shell = new Shell { AddWorks = false };
        var presence = new TrayPresence(shell.Call);

        Assert.True(presence.Add());
        Assert.True(presence.Added);
        Assert.Contains((Native.NIM_MODIFY, TrayPresence.AddFlags), shell.Calls);
    }

    [Fact]
    public void An_icon_never_added_is_not_deleted()
    {
        var shell = new Shell { AddWorks = false, ModifyWorks = false };
        var presence = new TrayPresence(shell.Call);

        Assert.False(presence.Add());
        presence.Close();

        Assert.False(presence.Added);
        Assert.Equal(0, shell.CountOf(Native.NIM_DELETE));
    }

    [Fact]
    public void An_update_without_an_icon_adds_it()
    {
        var shell = new Shell { AddWorks = false, ModifyWorks = false };
        var presence = new TrayPresence(shell.Call);
        presence.Add();
        shell.AddWorks = true;
        shell.Calls.Clear();

        Assert.True(presence.Update());

        Assert.True(presence.Added);
        Assert.Equal(Native.NIM_ADD, shell.Calls[0].Message);
    }

    [Fact]
    public void An_update_the_shell_no_longer_knows_adds_the_icon_again()
    {
        var shell = new Shell();
        var presence = new TrayPresence(shell.Call);
        presence.Add();
        shell.ModifyWorks = false;
        shell.Calls.Clear();

        Assert.True(presence.Update());

        Assert.Equal(1, shell.CountOf(Native.NIM_ADD));
    }

    [Fact]
    public void Nothing_brings_the_icon_back_after_the_last_call()
    {
        var shell = new Shell();
        var presence = new TrayPresence(shell.Call);
        presence.Add();
        presence.Close();
        shell.Calls.Clear();

        Assert.False(presence.Add());
        Assert.False(presence.Update());

        Assert.Empty(shell.Calls);
    }
}

public class TrayAddRetryTests
{
    [Fact]
    public void The_retries_last_two_minutes_at_one_every_two_seconds()
    {
        Assert.Equal(2000, TrayAddRetry.DelayMs);
        Assert.Equal(120_000, TrayAddRetry.DelayMs * TrayAddRetry.MaxTries);
    }

    [Fact]
    public void An_icon_that_is_there_writes_nothing_and_arms_nothing()
    {
        var retry = new TrayAddRetry();

        Assert.Null(retry.Note(true));
        Assert.False(retry.ShouldArm(true));
    }

    [Fact]
    public void A_failure_writes_one_line_however_many_tries_fail()
    {
        var retry = new TrayAddRetry();

        Assert.NotNull(retry.Note(false));
        for (var i = 0; i < TrayAddRetry.MaxTries + 5; i++)
        {
            Assert.Null(retry.Note(false));
        }
    }

    [Fact]
    public void A_success_after_a_failure_writes_one_line()
    {
        var retry = new TrayAddRetry();
        retry.Note(false);

        Assert.NotNull(retry.Note(true));
        Assert.Null(retry.Note(true));
    }

    [Fact]
    public void Only_one_try_is_armed_at_a_time()
    {
        var retry = new TrayAddRetry();

        Assert.True(retry.ShouldArm(false));
        Assert.False(retry.ShouldArm(false));
        retry.Fired();
        Assert.True(retry.ShouldArm(false));
    }

    [Fact]
    public void The_tries_end_after_two_minutes()
    {
        var retry = new TrayAddRetry();
        var tries = 0;
        while (retry.ShouldArm(false))
        {
            tries++;
            retry.Fired();
        }

        Assert.Equal(TrayAddRetry.MaxTries, tries);
    }

    [Fact]
    public void An_icon_lost_later_gets_all_its_tries_again()
    {
        var retry = new TrayAddRetry();
        while (retry.ShouldArm(false))
        {
            retry.Fired();
        }

        retry.Note(true);

        Assert.True(retry.ShouldArm(false));
    }
}
