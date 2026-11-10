using Hindsound.Audio;
using Hindsound.Linux.Audio;
using NAudio.Wave;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// A sound server that is slow to answer while it reconfigures, for instance when a headset connects, is not a server
/// without devices: the capture that works must go on.
/// </summary>
public sealed class PulseBusyServerTests
{
    private static readonly PulseServerState Server = new(
        "speakers",
        "speakers.monitor",
        [new PulseSink("speakers", "Speakers", "speakers.monitor")],
        [new PulseSource("speakers.monitor", "Monitor of Speakers", IsMonitor: true)]);

    [Fact]
    public void A_server_too_slow_to_list_its_devices_is_not_read_as_a_server_without_devices()
    {
        var lines = new List<string>();
        var factory = new PulseCaptureFactory(lines.Add, () => throw new TimeoutException("The sound server did not list its devices in time."));

        var failure = Record.Exception(() => factory.ResolveDevice(AudioFlow.System, null));
        Record.Exception(() => factory.ResolveDevice(AudioFlow.System, null));

        Assert.NotNull(failure);
        Assert.IsNotType<TimeoutException>(failure);
        Assert.IsType<TimeoutException>(failure.InnerException);
        Assert.Equal(["the sound server could not be read: TimeoutException: The sound server did not list its devices in time."], lines);
    }

    [Fact]
    public async Task The_engine_keeps_a_working_capture_while_the_server_is_too_slow_to_answer()
    {
        var busy = false;
        var pulse = new PulseCaptureFactory(_ => { }, () => busy ? throw new TimeoutException("The sound server did not answer in time.") : Server);
        var factory = new OpenWithoutServer(pulse);
        using var engine = new AudioEngine(
            new AudioEngineOptions(TimeSpan.FromSeconds(10), MicEnabled: false),
            factory,
            new SilentWatcher(),
            tickInterval: TimeSpan.FromHours(1),
            processes: new NoProcesses());

        await engine.StartAsync();
        var capture = Assert.Single(factory.Opened);

        busy = true;
        await engine.RefreshDevicesAsync();

        Assert.False(capture.Disposed);
        Assert.Single(factory.Opened);
    }

    // The choices are the real factory's; the opening stands in for the server, which this system may not have.
    private sealed class OpenWithoutServer(PulseCaptureFactory pulse) : ICaptureSourceFactory
    {
        public List<StandInSource> Opened { get; } = [];

        public string? ResolveDevice(AudioFlow flow, string? requestedId) => pulse.ResolveDevice(flow, requestedId);

        public Task<ICaptureSource> OpenAsync(CaptureRequest request)
        {
            var source = new StandInSource(request.Flow);
            Opened.Add(source);
            return Task.FromResult<ICaptureSource>(source);
        }
    }

    private sealed class StandInSource(AudioFlow flow) : ICaptureSource
    {
        public WaveFormat Format { get; } = PulseFormats.WaveFormatFor(flow);

        public string? DeviceName => "Monitor of Speakers";

        public bool Disposed { get; private set; }

        public event CapturePacketHandler? PacketAvailable
        {
            add { }
            remove { }
        }

        public event Action<Exception?>? Stopped
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class SilentWatcher : IDeviceWatcher
    {
        public event Action? DevicesChanged
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoProcesses : IProcessSnapshot
    {
        public IReadOnlyList<ProcessEntry> Take() => [];
    }
}
