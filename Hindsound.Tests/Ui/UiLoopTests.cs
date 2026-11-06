using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>Real messages to a hidden window of this test process. Nothing shows and nothing takes the focus.</summary>
public class UiLoopTests
{
    private const nuint CloseApp = 1;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    // A window belongs to the thread that made it, and a loop that never ends must not hold the test.
    private static bool LoopEnds(Action<UiLoop> before)
    {
        using var ended = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var loop = new UiLoop();
            before(loop);
            loop.Run();
            ended.Set();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return ended.Wait(Bound);
    }

    private static void PostEndSession(UiLoop loop, nuint ending) =>
        Native.PostMessageW(loop.Hwnd, Native.WM_ENDSESSION, ending, (nint)CloseApp);

    [Fact]
    public void An_installer_closing_the_app_runs_the_shutdown_and_ends_the_loop()
    {
        var shutdowns = 0;

        var ended = LoopEnds(loop =>
        {
            loop.SessionEnding = () => shutdowns++;
            PostEndSession(loop, 1);
        });

        Assert.True(ended, "the loop went on after WM_ENDSESSION");
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public void A_shutdown_that_throws_still_ends_the_loop()
    {
        var ended = LoopEnds(loop =>
        {
            loop.SessionEnding = () => throw new InvalidOperationException("on purpose");
            PostEndSession(loop, 1);
        });

        Assert.True(ended, "the loop went on after a failed shutdown");
    }

    [Fact]
    public void A_session_that_goes_on_after_all_does_not_end_the_loop()
    {
        var shutdowns = 0;
        var kept = 0;

        var ended = LoopEnds(loop =>
        {
            loop.SessionEnding = () => shutdowns++;
            loop.SessionKept = () => kept++;
            PostEndSession(loop, 0);
            loop.After(300, loop.Quit);
        });

        Assert.True(ended);
        Assert.Equal(0, shutdowns);
        Assert.Equal(1, kept);
    }

    [Fact]
    public void The_question_of_the_session_end_is_passed_on()
    {
        var asked = 0;

        var ended = LoopEnds(loop =>
        {
            loop.SessionEndAsked = () => asked++;
            Native.PostMessageW(loop.Hwnd, Native.WM_QUERYENDSESSION, 0, (nint)CloseApp);
            loop.After(100, loop.Quit);
        });

        Assert.True(ended);
        Assert.Equal(1, asked);
    }
}
