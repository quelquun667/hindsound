using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

public sealed class LinuxLoopTests
{
    // A loop that never ends would hang the run: past this, the guard asks it to quit and the test fails.
    private const int GuardMs = 10_000;

    private static void RunGuarded(LinuxLoop loop)
    {
        var guarded = false;
        using var guard = new Timer(_ =>
        {
            guarded = true;
            loop.Quit();
        }, null, GuardMs, Timeout.Infinite);
        loop.Run();
        Assert.False(guarded, "the loop had to be stopped by the guard");
    }

    [Fact]
    public void Posted_actions_run_in_order_on_the_thread_that_runs_the_loop()
    {
        using var loop = new LinuxLoop();
        var ran = new List<int>();
        var onLoop = new List<bool>();
        Thread? thread = null;
        loop.Post(() => ran.Add(1));
        loop.Post(() =>
        {
            ran.Add(2);
            thread = Thread.CurrentThread;
            onLoop.Add(loop.OnLoopThread);
        });
        loop.Post(() =>
        {
            ran.Add(3);
            loop.Quit();
        });

        RunGuarded(loop);

        Assert.Equal([1, 2, 3], ran);
        Assert.Same(Thread.CurrentThread, thread);
        Assert.Equal([true], onLoop);
        Assert.False(loop.OnLoopThread);
    }

    [Fact]
    public void Quit_lets_what_is_already_posted_run_first()
    {
        using var loop = new LinuxLoop();
        var ran = false;
        loop.Post(() =>
        {
            loop.Quit();
            loop.Post(() => ran = true);
        });

        RunGuarded(loop);

        Assert.True(ran);
    }

    [Fact]
    public void Quit_before_run_still_runs_what_was_posted_and_returns()
    {
        using var loop = new LinuxLoop();
        var ran = false;
        loop.Post(() => ran = true);
        loop.Quit();

        RunGuarded(loop);

        Assert.True(ran);
    }

    [Fact]
    public void Timers_run_by_their_time_then_in_the_order_they_were_set()
    {
        using var loop = new LinuxLoop();
        var ran = new List<string>();
        loop.After(80, () => ran.Add("late"));
        loop.After(0, () => ran.Add("first"));
        loop.After(0, () => ran.Add("second"));
        loop.After(160, loop.Quit);

        RunGuarded(loop);

        Assert.Equal(["first", "second", "late"], ran);
    }

    [Fact]
    public void A_posted_action_goes_before_a_due_timer()
    {
        using var loop = new LinuxLoop();
        var ran = new List<string>();
        loop.After(0, () => ran.Add("timer"));
        loop.Post(() => ran.Add("posted"));
        loop.After(50, loop.Quit);

        RunGuarded(loop);

        Assert.Equal(["posted", "timer"], ran);
    }

    [Fact]
    public void A_timer_waits_for_its_clock()
    {
        long now = 0;
        using var loop = new LinuxLoop(() => Interlocked.Read(ref now));
        var ran = new List<string>();
        loop.After(1000, () => ran.Add("timer"));
        loop.After(1000, loop.Quit);
        loop.Post(() => ran.Add("before"));
        loop.Post(() => Interlocked.Exchange(ref now, 1000));

        RunGuarded(loop);

        Assert.Equal(["before", "timer"], ran);
    }

    [Fact]
    public void A_far_timer_does_not_hold_quit_and_is_dropped()
    {
        using var loop = new LinuxLoop();
        var fired = false;
        loop.After(60_000, () => fired = true);
        loop.Post(loop.Quit);
        RunGuarded(loop);

        // Run again: the timer from before must be gone, not waiting.
        loop.Post(loop.Quit);
        RunGuarded(loop);

        Assert.False(fired);
    }

    [Fact]
    public void A_post_from_another_thread_wakes_the_waiting_loop()
    {
        using var loop = new LinuxLoop();
        var ran = false;
        var poster = new Thread(() =>
        {
            Thread.Sleep(100);
            loop.Post(() =>
            {
                ran = true;
                loop.Quit();
            });
        });
        poster.Start();

        RunGuarded(loop);
        poster.Join();

        Assert.True(ran);
    }

    [Fact]
    public void Nothing_runs_once_disposed()
    {
        var loop = new LinuxLoop();
        var ran = false;
        loop.Post(() => ran = true);
        loop.Dispose();
        loop.Post(() => ran = true);
        loop.After(0, () => ran = true);

        RunGuarded(loop);

        Assert.False(ran);
    }

    [Fact]
    public void An_exception_in_an_action_leaves_run()
    {
        using var loop = new LinuxLoop();
        loop.Post(() => throw new InvalidOperationException("from an action"));

        var thrown = Assert.Throws<InvalidOperationException>(loop.Run);

        Assert.Equal("from an action", thrown.Message);
        Assert.False(loop.OnLoopThread);
    }

    [Fact]
    public void The_app_has_no_window_in_front()
    {
        using var loop = new LinuxLoop();

        Assert.False(loop.InFront());
    }
}
