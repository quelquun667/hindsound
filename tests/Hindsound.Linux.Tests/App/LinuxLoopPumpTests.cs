using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// The loop while a window's pump does the waiting: it waits through the pump, with the time to its next timer, and every
/// way the loop can be asked something from another thread (post, timer, quit, dispose, a new pump) wakes the pump.
/// </summary>
public sealed class LinuxLoopPumpTests
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

    /// <summary>
    /// A pump like a window's: events raised from any thread wait in its queue and are handed out by <see cref="Wait"/>,
    /// and a wake is kept until a wait takes it.
    /// </summary>
    private sealed class FakePump : ILoopPump
    {
        private readonly object _gate = new();
        private readonly Queue<Action> _events = new();
        private bool _woken;

        /// <summary>Runs on each wait, on the loop thread, with the timeout it was given (the test's clock moves here).</summary>
        public Action<int>? OnWait { get; set; }

        public List<int> Timeouts { get; } = [];

        public List<Thread> WaitThreads { get; } = [];

        public int Wakes;

        public void Raise(Action windowEvent)
        {
            lock (_gate)
            {
                _events.Enqueue(windowEvent);
                Monitor.PulseAll(_gate);
            }
        }

        public void Wait(int timeoutMs)
        {
            lock (_gate)
            {
                Timeouts.Add(timeoutMs);
                WaitThreads.Add(Thread.CurrentThread);
            }

            OnWait?.Invoke(timeoutMs);
            var handed = new List<Action>();
            lock (_gate)
            {
                if (!_woken && _events.Count == 0)
                {
                    Monitor.Wait(_gate, timeoutMs);
                }

                _woken = false;
                while (_events.Count > 0)
                {
                    handed.Add(_events.Dequeue());
                }
            }

            foreach (var windowEvent in handed)
            {
                windowEvent();
            }
        }

        public void Wake()
        {
            Interlocked.Increment(ref Wakes);
            lock (_gate)
            {
                _woken = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private static Thread Later(int delayMs, Action action)
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(delayMs);
            action();
        });
        thread.Start();
        return thread;
    }

    [Fact]
    public void A_post_from_another_thread_wakes_the_loop_waiting_in_its_pump()
    {
        using var loop = new LinuxLoop();
        var pump = new FakePump();
        loop.Pump = pump;
        var ran = false;
        var poster = Later(100, () => loop.Post(() =>
        {
            ran = true;
            loop.Quit();
        }));

        RunGuarded(loop);
        poster.Join();

        Assert.True(ran);
        Assert.Contains(Timeout.Infinite, pump.Timeouts);
        Assert.All(pump.WaitThreads, thread => Assert.Same(Thread.CurrentThread, thread));
    }

    [Fact]
    public void A_window_event_runs_on_the_loop_thread_and_what_it_posts_runs_next()
    {
        using var loop = new LinuxLoop();
        var pump = new FakePump();
        loop.Pump = pump;
        var ran = new List<string>();
        var onLoop = new List<bool>();
        var raiser = Later(100, () => pump.Raise(() =>
        {
            ran.Add("event");
            onLoop.Add(loop.OnLoopThread);
            loop.Post(() =>
            {
                ran.Add("posted");
                loop.Quit();
            });
        }));

        RunGuarded(loop);
        raiser.Join();

        Assert.Equal(["event", "posted"], ran);
        Assert.Equal([true], onLoop);
    }

    [Fact]
    public void The_pump_waits_until_the_next_timer_and_the_timer_runs_when_it_is_due()
    {
        long now = 0;
        using var loop = new LinuxLoop(() => Interlocked.Read(ref now));
        var pump = new FakePump();

        // Each wait lasts as long as it was asked: the clock jumps by its timeout.
        pump.OnWait = timeout => Interlocked.Add(ref now, timeout);
        loop.Pump = pump;
        var ran = new List<string>();
        loop.After(700, () => ran.Add("late"));
        loop.After(300, () => ran.Add("early"));
        loop.After(700, loop.Quit);

        RunGuarded(loop);

        Assert.Equal(["early", "late"], ran);
        Assert.Equal(700, Interlocked.Read(ref now));
        Assert.Equal(300, pump.Timeouts[^2]);
        Assert.Equal(400, pump.Timeouts[^1]);
    }

    [Fact]
    public void A_timer_set_from_another_thread_wakes_the_pump_so_the_wait_is_shortened()
    {
        using var loop = new LinuxLoop();
        var pump = new FakePump();
        loop.Pump = pump;
        var ran = false;
        var setter = Later(100, () => loop.After(0, () =>
        {
            ran = true;
            loop.Quit();
        }));

        RunGuarded(loop);
        setter.Join();

        Assert.True(ran);
        Assert.True(Volatile.Read(ref pump.Wakes) >= 1);
    }

    [Fact]
    public void Quit_from_another_thread_ends_the_wait_in_the_pump()
    {
        using var loop = new LinuxLoop();
        loop.Pump = new FakePump();
        var quitter = Later(100, loop.Quit);

        RunGuarded(loop);
        quitter.Join();
    }

    [Fact]
    public void Dispose_from_another_thread_ends_the_wait_in_the_pump()
    {
        var loop = new LinuxLoop();
        loop.Pump = new FakePump();
        var disposer = Later(100, loop.Dispose);

        RunGuarded(loop);
        disposer.Join();
    }

    [Fact]
    public void A_pump_taken_away_while_the_loop_waits_in_it_is_woken_and_the_loop_waits_on_its_own()
    {
        using var loop = new LinuxLoop();
        var pump = new FakePump();
        loop.Pump = pump;
        var ran = false;
        var closer = Later(100, () =>
        {
            loop.Pump = null;
            Thread.Sleep(100);
            loop.Post(() =>
            {
                ran = true;
                loop.Quit();
            });
        });

        RunGuarded(loop);
        closer.Join();

        Assert.True(ran);

        // The post came after the pump was gone: nothing woke the old pump for it.
        Assert.Equal(1, Volatile.Read(ref pump.Wakes));
    }

    [Fact]
    public void A_pump_given_while_the_loop_waits_on_its_own_takes_over_the_wait()
    {
        using var loop = new LinuxLoop();
        var pump = new FakePump();
        var ran = false;
        var opener = Later(100, () =>
        {
            loop.Pump = pump;
            pump.Raise(() =>
            {
                ran = true;
                loop.Quit();
            });
        });

        RunGuarded(loop);
        opener.Join();

        Assert.True(ran);
    }

    [Fact]
    public void Posted_actions_still_go_before_due_timers_and_quit_still_lets_them_run()
    {
        using var loop = new LinuxLoop();
        loop.Pump = new FakePump();
        var ran = new List<string>();
        loop.After(0, () => ran.Add("timer"));
        loop.Post(() => ran.Add("posted"));
        loop.Post(() =>
        {
            loop.Quit();
            loop.Post(() => ran.Add("after quit"));
        });

        RunGuarded(loop);

        Assert.Equal(["posted", "after quit"], ran);
    }
}
