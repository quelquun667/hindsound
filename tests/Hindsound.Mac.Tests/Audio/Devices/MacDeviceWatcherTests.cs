using Hindsound.Mac.Audio.Devices;

namespace Hindsound.Mac.Tests.Audio.Devices;

public sealed class MacDeviceWatcherTests
{
    [Fact]
    public void Start_listens_once_to_the_devices_and_the_default_output_and_input()
    {
        var hal = new FakeHal();
        using var watcher = new MacDeviceWatcher(_ => { }, hal.Listen);

        watcher.Start();
        watcher.Start();

        Assert.Equal(1, hal.Listens);
        Assert.Equal([HalCodes.Devices, HalCodes.DefaultOutput, HalCodes.DefaultInput], hal.Selectors);
    }

    [Fact]
    public void Several_notifications_of_one_change_raise_one_event()
    {
        var hal = new FakeHal();
        using var watcher = new MacDeviceWatcher(_ => { }, hal.Listen);
        var events = 0;
        using var raised = new ManualResetEventSlim();
        watcher.DevicesChanged += () =>
        {
            Interlocked.Increment(ref events);
            raised.Set();
        };
        watcher.Start();

        for (var i = 0; i < 5; i++)
        {
            hal.Changed!();
        }

        Assert.True(raised.Wait(5_000));
        Thread.Sleep(600);
        Assert.Equal(1, Volatile.Read(ref events));
    }

    [Fact]
    public void Dispose_removes_the_listeners_and_no_event_follows()
    {
        var hal = new FakeHal();
        var watcher = new MacDeviceWatcher(_ => { }, hal.Listen);
        var events = 0;
        watcher.DevicesChanged += () => Interlocked.Increment(ref events);
        watcher.Start();
        hal.Changed!();

        watcher.Dispose();
        watcher.Dispose();
        hal.Changed!();
        Thread.Sleep(600);

        Assert.Equal(1, hal.Removed);
        Assert.Equal(0, Volatile.Read(ref events));
    }

    [Fact]
    public void A_watch_disposed_before_it_starts_never_listens()
    {
        var hal = new FakeHal();
        var watcher = new MacDeviceWatcher(_ => { }, hal.Listen);

        watcher.Dispose();
        watcher.Start();

        Assert.Equal(0, hal.Listens);
    }

    [Fact]
    public void A_listener_the_hal_refuses_is_said_and_the_others_stay()
    {
        var hal = new FakeHal { Statuses = [0, 560947818, 0] };
        var log = new List<string>();
        using var watcher = new MacDeviceWatcher(log.Add, hal.Listen);

        watcher.Start();

        Assert.Equal(["the watch of the devices: the HAL refused the listener on 'dOut' (status 560947818)"], log);
        Assert.Equal(0, hal.Removed);
    }

    [Fact]
    public void A_hal_that_throws_is_said_and_Start_does_not_throw()
    {
        var log = new List<string>();
        using var watcher = new MacDeviceWatcher(log.Add, (_, _) => throw new DllNotFoundException("no CoreAudio"));

        watcher.Start();

        Assert.Equal(["the watch of the devices could not start: DllNotFoundException: no CoreAudio"], log);
    }

    private sealed class FakeHal
    {
        public int[]? Statuses { get; init; }

        public int Listens { get; private set; }

        public int Removed { get; private set; }

        public IReadOnlyList<uint> Selectors { get; private set; } = [];

        public Action? Changed { get; private set; }

        public (IDisposable Remove, int[] Statuses) Listen(IReadOnlyList<uint> selectors, Action changed)
        {
            Listens++;
            Selectors = selectors;
            Changed = changed;
            return (new Removal(this), Statuses ?? new int[selectors.Count]);
        }

        private sealed class Removal(FakeHal hal) : IDisposable
        {
            public void Dispose() => hal.Removed++;
        }
    }
}
