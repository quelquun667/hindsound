using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>The tests that hold every thread of the pool for a while: one at a time, apart from the others.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BusyPoolCollection
{
    public const string Name = "Busy thread pool";
}

/// <summary>
/// Without a session bus, each notification ends on its own, before a later one can take its place, however slow the rest of
/// the machine is: the notices of the app wait on that end.
/// </summary>
[Collection(BusyPoolCollection.Name)]
public sealed class NotificationWithoutBusTests
{
    private static NotificationContent Content(string body) =>
        new(NotificationKind.Silent, "Clip saved", body, [], 0, NotificationVisual.Bars, 0, []);

    // Two notifications 200 ms apart, as in the logic tests: how many ended, and what the log said.
    private static (int Finished, bool Showing, List<string> Log) TwoNotifications(Func<SessionBus> bus, Action? beforeFirst = null)
    {
        using var loop = new LinuxLoop();
        var log = new List<string>();
        var finished = 0;
        using var notification = new FreedesktopNotification(loop, log.Add, bus, new NoFolder(), holdMs: 60_000);
        notification.Finished = () =>
        {
            finished++;
            if (finished == 2)
            {
                loop.Quit();
            }
        };

        // A thread of its own, not the pool: the pool may be the slow part here.
        var guard = new Thread(() =>
        {
            Thread.Sleep(10_000);
            loop.Post(loop.Quit);
        }) { IsBackground = true };
        guard.Start();

        loop.Post(() =>
        {
            beforeFirst?.Invoke();
            notification.Present(Content("Saved"), default, 0);
        });
        loop.Post(() => loop.After(200, () => notification.Present(Content("again"), default, 0)));
        loop.Run();
        return (finished, notification.Showing is not null, log);
    }

    [Fact]
    public void A_bus_slow_to_refuse_still_ends_each_notification_on_its_own()
    {
        var (finished, showing, log) = TwoNotifications(() =>
        {
            Thread.Sleep(500);
            throw new InvalidOperationException("no bus");
        });

        Assert.Equal(2, finished);
        Assert.False(showing);
        Assert.Equal(["notification: not shown (no session bus)"], log);
    }

    [Fact]
    public void A_pool_busy_for_a_second_still_ends_each_notification_on_its_own()
    {
        using var busy = new BusyPool(TimeSpan.FromSeconds(1));
        var (finished, showing, log) = TwoNotifications(() => throw new InvalidOperationException("no bus"), busy.Start);

        Assert.Equal(2, finished);
        Assert.False(showing);
        Assert.Equal(["notification: not shown (no session bus)"], log);
    }

    private sealed class NoFolder : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }

    /// <summary>
    /// Every thread of the pool asleep for <c>hold</c>, with more sleepers queued than there are threads, so what is queued
    /// after them waits about that long: what a loaded machine running tests side by side does to the pool.
    /// </summary>
    private sealed class BusyPool(TimeSpan hold) : IDisposable
    {
        private CountdownEvent? _left;

        public void Start()
        {
            ThreadPool.GetMinThreads(out var minWorkers, out _);
            var sleepers = Math.Max(ThreadPool.ThreadCount, minWorkers) + 8;
            _left = new CountdownEvent(sleepers);
            for (var i = 0; i < sleepers; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(left =>
                {
                    Thread.Sleep(hold);
                    left.Signal();
                }, _left, preferLocal: false);
            }
        }

        // The pool is given back before the next test. The count is not disposed: a sleeper late past the wait still signals it.
        public void Dispose() => _left?.Wait(TimeSpan.FromSeconds(30));
    }
}
