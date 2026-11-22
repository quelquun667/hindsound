using System.Diagnostics;
using System.Runtime.InteropServices;
using Hindsound.Tests.Audio;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// The watchdog of the WinHTTP transport, without a network: the close and the timer are injected, a fake blocking call waits
/// for the close and leaves its error as WinHTTP does. The timer is fired by hand, at the moment each case needs.
/// </summary>
[Collection(nameof(TimingSensitive))]
public class WatchdogTests
{
    private const int ErrorCancelled = 12017;
    private const int ErrorTimeout = 12002;
    private const int ErrorCannotConnect = 12029;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static TransportResult TimedOut => new(TransportStatus.TimedOut);

    [Fact]
    public void TheTimerFiringDuringTheCallClosesTheRequestOnceAndEndsTimedOut()
    {
        var rig = new Rig();

        var running = Task.Run(() => rig.Dog.Run(2000, rig.Blocking(false, ErrorCancelled)));
        rig.WaitForTimers(1);
        rig.Fire(0);

        Assert.Equal(TimedOut, running.Result);
        Assert.Equal(1, rig.Closes);
        Assert.True(rig.Dog.Fired);
        Assert.Equal([2000], rig.Patiences);
    }

    [Fact]
    public void ACallThatSucceedsRightAtTheLimitIsTimedOut()
    {
        var rig = new Rig();

        var running = Task.Run(() => rig.Dog.Run(2000, rig.Blocking(true, 0)));
        rig.WaitForTimers(1);
        rig.Fire(0);

        Assert.Equal(TimedOut, running.Result);
        Assert.Equal(1, rig.Closes);
    }

    [Fact]
    public void AnErrorBeforeTheLimitIsKeptApartFromTheTimeout()
    {
        var rig = new Rig();

        var failed = rig.Dog.Run(2000, () =>
        {
            Marshal.SetLastPInvokeError(ErrorCannotConnect);
            return false;
        });
        var timedOut = rig.Dog.Run(2000, () =>
        {
            Marshal.SetLastPInvokeError(ErrorTimeout);
            return false;
        });

        Assert.Equal(new TransportResult(TransportStatus.Failed, ErrorCannotConnect), failed);
        Assert.Equal(TimedOut, timedOut);
        Assert.Equal(0, rig.Closes);
        Assert.False(rig.Dog.Fired);
    }

    [Fact]
    public void ACallThatSucceedsInTimeIsNull()
    {
        var rig = new Rig();

        Assert.Null(rig.Dog.Run(2000, () => true));
        Assert.Equal(0, rig.Closes);
    }

    [Fact]
    public void TheTimerOfAnEarlierCallClosesNothingDuringTheNextOneNorAfterTheEnd()
    {
        var rig = new Rig();
        Assert.Null(rig.Dog.Run(2000, () => true));

        var second = Task.Run(() => rig.Dog.Run(2000, rig.Blocking(true, 0)));
        rig.WaitForTimers(2);
        rig.Fire(0);
        Assert.Equal(0, rig.Closes);
        rig.Released.Set();
        Assert.Null(second.Result);

        rig.Fire(0);
        rig.Fire(1);

        Assert.Equal(0, rig.Closes);
        Assert.False(rig.Dog.Fired);
    }

    [Fact]
    public void AfterTheTimerFiredARunIsTimedOutWithoutMakingTheCall()
    {
        var rig = new Rig();
        var running = Task.Run(() => rig.Dog.Run(2000, rig.Blocking(false, ErrorCancelled)));
        rig.WaitForTimers(1);
        rig.Fire(0);
        Assert.Equal(TimedOut, running.Result);
        var calls = 0;

        var result = rig.Dog.Run(2000, () =>
        {
            calls++;
            return true;
        });

        Assert.Equal(TimedOut, result);
        Assert.Equal(0, calls);
        Assert.Equal(1, rig.Closes);
    }

    [Fact]
    public void ACallThatThrowsLeavesTheWatchdogDisarmed()
    {
        var rig = new Rig();

        Assert.Throws<InvalidOperationException>(() => rig.Dog.Run(2000, () => throw new InvalidOperationException()));
        rig.Fire(0);

        Assert.Equal(0, rig.Closes);
        Assert.False(rig.Dog.Fired);
        Assert.Null(rig.Dog.Run(2000, () => true));
    }

    [Fact]
    public void ReleaseClosesTheRequestOnceAndNotAfterTheWatchdogClosedIt()
    {
        var plain = new Rig();
        plain.Dog.Release();
        plain.Dog.Release();
        Assert.Equal(1, plain.Closes);

        var fired = new Rig();
        var running = Task.Run(() => fired.Dog.Run(2000, fired.Blocking(false, ErrorCancelled)));
        fired.WaitForTimers(1);
        fired.Fire(0);
        Assert.Equal(TimedOut, running.Result);
        fired.Dog.Release();
        Assert.Equal(1, fired.Closes);
    }

    [Fact]
    public void ThePatienceRunsOutOnTheRealTimerToo()
    {
        var closes = 0;
        var released = new ManualResetEventSlim();
        var dog = new WinHttpTransport.Watchdog(() =>
        {
            Interlocked.Increment(ref closes);
            released.Set();
        });
        var watch = Stopwatch.StartNew();

        var result = dog.Run(150, () =>
        {
            released.Wait(Patience);
            Marshal.SetLastPInvokeError(ErrorCancelled);
            return false;
        });
        watch.Stop();

        Assert.Equal(TimedOut, result);
        Assert.Equal(1, closes);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
    }

    /// <summary>The close happens under the lock: a call ending at that very moment cannot report a success of a closed request.</summary>
    [Fact]
    public void TheCloseHappensWhileTheWatchdogHoldsItsLock()
    {
        var held = false;
        WinHttpTransport.Watchdog? dog = null;
        var rig = new Rig();
        dog = new WinHttpTransport.Watchdog(
            () =>
            {
                // Fired takes the lock: while the close runs, it cannot answer.
                var probe = Task.Run(() => dog!.Fired);
                held = !probe.Wait(TimeSpan.FromMilliseconds(200));
                rig.Released.Set();
            },
            rig.StartTimer);

        var running = Task.Run(() => dog.Run(2000, rig.Blocking(false, ErrorCancelled)));
        rig.WaitForTimers(1);
        rig.Fire(0);

        Assert.Equal(TimedOut, running.Result);
        Assert.True(held);
    }

    private sealed class Rig
    {
        private readonly List<Action> _timers = [];
        private readonly SemaphoreSlim _started = new(0);
        private int _closes;

        public Rig()
        {
            Dog = new WinHttpTransport.Watchdog(
                () =>
                {
                    Interlocked.Increment(ref _closes);
                    Released.Set();
                },
                StartTimer);
        }

        public WinHttpTransport.Watchdog Dog { get; }

        public ManualResetEventSlim Released { get; } = new();

        public List<int> Patiences { get; } = [];

        public int Closes => Volatile.Read(ref _closes);

        public IDisposable StartTimer(int patience, Action fire)
        {
            lock (_timers)
            {
                _timers.Add(fire);
                Patiences.Add(patience);
            }

            _started.Release();
            return new Nothing();
        }

        /// <summary>Waits until that many timers were started: the call under test is running.</summary>
        public void WaitForTimers(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Assert.True(_started.Wait(Patience));
            }
        }

        public void Fire(int timer)
        {
            Action fire;
            lock (_timers)
            {
                fire = _timers[timer];
            }

            fire();
        }

        /// <summary>A blocking call that ends when the request is closed, and leaves its error, as WinHTTP does.</summary>
        public Func<bool> Blocking(bool succeeds, int error) => () =>
        {
            Released.Wait(Patience);
            Marshal.SetLastPInvokeError(error);
            return succeeds;
        };
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
