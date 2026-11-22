using Hindsound.Audio;
using Hindsound.Mac.Audio.Devices;
using NAudio.Wave;

namespace Hindsound.Mac.Tests.Audio.Devices;

/// <summary>
/// The default output changed during a capture of the sound of the Mac (the output setting on the default one): the watch of
/// the devices, told by the HAL, has the engine open the capture again on the new output. The CI proves it with real devices
/// (--prove-switch); here the HAL and the capture are fakes, and the watch and the engine are the real ones.
/// </summary>
public sealed class MacOutputSwitchTests
{
    [Fact]
    public async Task A_new_default_output_seen_by_the_watch_opens_the_capture_again_on_it()
    {
        Action? halChanged = null;
        var factory = new SwitchingFactory { Default = "BlackHole2ch_UID" };
        using var watcher = new MacDeviceWatcher(_ => { }, (selectors, changed) =>
        {
            halChanged = changed;
            return (new Nothing(), new int[selectors.Count]);
        });
        using var engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(5), MicEnabled: false), factory, watcher, processes: new NoProcesses());

        await engine.StartAsync();
        Assert.Equal([("BlackHole2ch_UID", true)], factory.Requests);
        Assert.NotNull(halChanged);

        factory.Default = "NullAudioDevice_UID";
        halChanged();
        await factory.SecondOpening.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await engine.RefreshDevicesAsync();

        Assert.Equal([("BlackHole2ch_UID", true), ("NullAudioDevice_UID", true)], factory.Requests);
        Assert.True(factory.Sources[0].Disposed);
        Assert.False(factory.Sources[1].Disposed);
        Assert.Equal("output NullAudioDevice_UID", engine.Status.SystemDeviceName);
        Assert.True(engine.Status.SystemCapturing);
        await engine.StopAsync();
    }

    [Fact]
    public async Task The_same_default_output_after_a_notification_keeps_the_capture_open()
    {
        Action? halChanged = null;
        var factory = new SwitchingFactory { Default = "BlackHole2ch_UID" };
        using var watcher = new MacDeviceWatcher(_ => { }, (selectors, changed) =>
        {
            halChanged = changed;
            return (new Nothing(), new int[selectors.Count]);
        });
        using var engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(5), MicEnabled: false), factory, watcher, processes: new NoProcesses());

        await engine.StartAsync();
        halChanged!();
        await Task.Delay(800);
        await engine.RefreshDevicesAsync();

        Assert.Equal([("BlackHole2ch_UID", true)], factory.Requests);
        Assert.False(factory.Sources[0].Disposed);
        await engine.StopAsync();
    }

    private sealed class SwitchingFactory : ICaptureSourceFactory
    {
        private readonly Lock _gate = new();

        public volatile string Default = string.Empty;

        public List<(string DeviceId, bool Exclude)> Requests { get; } = [];

        public List<FakeSource> Sources { get; } = [];

        public TaskCompletionSource SecondOpening { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? ResolveDevice(AudioFlow flow, string? requestedId) => flow == AudioFlow.System ? requestedId ?? Default : null;

        public Task<ICaptureSource> OpenAsync(CaptureRequest request)
        {
            var source = new FakeSource("output " + request.DeviceId);
            lock (_gate)
            {
                Requests.Add((request.DeviceId, request.ExcludeOwnProcess));
                Sources.Add(source);
                if (Requests.Count == 2)
                {
                    SecondOpening.TrySetResult();
                }
            }

            return Task.FromResult<ICaptureSource>(source);
        }
    }

    private sealed class FakeSource(string name) : ICaptureSource
    {
        public WaveFormat Format { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public string? DeviceName => name;

        public bool Disposed { get; private set; }

#pragma warning disable CS0067 // A fake that never delivers.
        public event CapturePacketHandler? PacketAvailable;

        public event Action<Exception?>? Stopped;
#pragma warning restore CS0067

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class NoProcesses : IProcessSnapshot
    {
        public IReadOnlyList<ProcessEntry> Take() => [];
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
