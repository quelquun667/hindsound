using System.Runtime.Versioning;
using Hindsound.Linux.Window;
using Hindsound.Unix.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// The wake of the SDL pump against the stop of SDL's events, with the push handed to a delegate: nothing native runs. SDL
/// frees its queue's lock when its events stop, so a push from another thread must never overlap that stop.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SdlPumpTests
{
    private const uint WakeType = 0x8000;

    [Fact]
    public void A_wake_pushes_its_own_type_once_until_the_wait_takes_it()
    {
        var pushes = new List<uint>();
        var pump = new SdlPump(WakeType, type => { pushes.Add(type); return true; });

        pump.Wake();
        pump.Wake();

        Assert.Equal([WakeType], pushes);
    }

    [Fact]
    public void After_stop_no_wake_from_another_thread_reaches_SDL()
    {
        var pushes = 0;
        var pump = new SdlPump(WakeType, _ => { Interlocked.Increment(ref pushes); return false; });
        pump.Wake();
        Assert.Equal(1, pushes);

        pump.Stop();
        var other = new Thread(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                pump.Wake();
            }
        });
        other.Start();
        Assert.True(other.Join(10_000));

        Assert.Equal(1, Volatile.Read(ref pushes));
    }

    [Fact]
    public void Stop_waits_for_a_push_already_under_way_on_another_thread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pump = new SdlPump(WakeType, _ =>
        {
            entered.Set();
            release.Wait(10_000);
            return true;
        });

        var waker = new Thread(pump.Wake);
        waker.Start();
        Assert.True(entered.Wait(10_000), "the wake never reached its push");

        var stop = Task.Run(pump.Stop);
        Assert.False(stop.Wait(200), "the stop returned while a push was still under way");

        release.Set();
        Assert.True(stop.Wait(10_000), "the stop never returned after the push ended");
        Assert.True(waker.Join(10_000));
    }
}
