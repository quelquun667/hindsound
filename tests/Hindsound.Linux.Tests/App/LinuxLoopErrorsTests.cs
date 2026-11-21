using Hindsound.Linux.App;
using Hindsound.Ui;
using Hindsound.Unix.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// A failed piece of work is reported through <see cref="UiErrors"/> and the loop goes on, as the Windows loop does: the
/// buffer of the app lives as long as the loop.
/// </summary>
[Collection(UiErrorsCollection.Name)]
public sealed class UnixLoopErrorsTests
{
    // A loop that never ends would hang the run: past this, the guard asks it to quit and the test fails.
    private const int GuardMs = 10_000;

    private static List<string> Capture(Action body)
    {
        var before = UiErrors.Handler;
        var reported = new List<string>();
        UiErrors.Handler = ex => reported.Add(ex.Message);
        try
        {
            body();
        }
        finally
        {
            UiErrors.Handler = before;
        }

        return reported;
    }

    private static void RunGuarded(UnixLoop loop)
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
    public void An_exception_in_an_action_or_a_timer_is_reported_and_the_next_work_still_runs()
    {
        var ran = new List<string>();
        var reported = Capture(() =>
        {
            using var loop = new UnixLoop();
            loop.Post(() => throw new InvalidOperationException("from an action"));
            loop.Post(() => ran.Add("next action"));
            loop.After(0, () => throw new InvalidOperationException("from a timer"));
            loop.After(0, () => ran.Add("next timer"));
            loop.After(0, loop.Quit);

            RunGuarded(loop);

            Assert.False(loop.OnLoopThread);
        });

        Assert.Equal(["from an action", "from a timer"], reported);
        Assert.Equal(["next action", "next timer"], ran);
    }

    [Fact]
    public void An_exception_in_a_window_event_is_reported_and_the_loop_goes_on()
    {
        var ran = false;
        var reported = Capture(() =>
        {
            using var loop = new UnixLoop();
            loop.Pump = new ThrowingOncePump(loop, () => ran = true);

            RunGuarded(loop);
        });

        Assert.Equal(["from a window event"], reported);
        Assert.True(ran, "an action posted after the failed event did not run");
    }

    // A pump whose first wait throws, as a handler of the window does; the second posts an action and quits.
    private sealed class ThrowingOncePump(UnixLoop loop, Action after) : ILoopPump
    {
        private int _waits;

        public void Wait(int timeoutMs)
        {
            if (_waits++ == 0)
            {
                throw new InvalidOperationException("from a window event");
            }

            loop.Post(after);
            loop.Quit();
        }

        public void Wake()
        {
        }
    }
}
