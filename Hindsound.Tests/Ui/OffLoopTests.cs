using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>Opening a folder can take as long as a sleeping network drive likes, with the shortcuts waiting behind it.</summary>
public sealed class OffLoopTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact]
    public void It_returns_while_the_work_is_still_going()
    {
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();

        var thread = OffLoop.Start(() =>
        {
            release.Wait(Limit);
            finished.Set();
        }, _ => { });

        Assert.False(finished.IsSet);
        release.Set();
        Assert.True(finished.Wait(Limit));
        Assert.True(thread.Join(Limit));
    }

    [Fact]
    public void The_work_runs_on_another_thread_in_the_apartment_the_shell_asks_for()
    {
        var caller = Environment.CurrentManagedThreadId;
        var workThread = caller;
        var apartment = ApartmentState.Unknown;

        OffLoop.Start(() =>
        {
            workThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
        }, _ => { }).Join(Limit);

        Assert.NotEqual(caller, workThread);
        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public void A_failure_is_told_and_stays_on_its_thread()
    {
        Exception? told = null;

        OffLoop.Start(() => throw new InvalidOperationException("no folder"), ex => told = ex).Join(Limit);

        Assert.Equal("no folder", Assert.IsType<InvalidOperationException>(told).Message);
    }

    [Fact]
    public void A_thread_left_going_does_not_keep_the_app_alive()
    {
        using var release = new ManualResetEventSlim();

        var thread = OffLoop.Start(() => release.Wait(Limit), _ => { });

        Assert.True(thread.IsBackground);
        release.Set();
        thread.Join(Limit);
    }
}
