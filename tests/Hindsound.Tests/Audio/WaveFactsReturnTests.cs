using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

/// <summary>A device look-up that can be made to fail, in front of the fake hardware.</summary>
internal sealed class FlakyLookUp(ICaptureSourceFactory inner) : ICaptureSourceFactory
{
    public bool Fail { get; set; }

    public string? ResolveDevice(AudioFlow flow, string? requestedId)
    {
        return Fail ? throw new InvalidOperationException("The device list is not available") : inner.ResolveDevice(flow, requestedId);
    }

    public Task<ICaptureSource> OpenAsync(CaptureRequest request) => inner.OpenAsync(request);
}

[Collection(nameof(TimingSensitive))]
public class WaveFactsReturnTests
{
    private const string App = "Discord.exe";
    private const int Rate = InternalFormat.SampleRate;

    [Fact]
    public async Task Back_to_the_PC_sound_waits_while_the_device_look_up_fails_and_the_application_capture_runs_on()
    {
        using var rig = new AudioTestRig(sourceApp: App, processes: new FakeProcesses(new ProcessEntry(50, 1, App)));
        var devices = new FlakyLookUp(rig.Factory);
        using var engine = new AudioEngine(
            new AudioEngineOptions(TimeSpan.FromSeconds(60), true, null, SourceApp: App),
            devices,
            rig.Watcher,
            () => rig.Clock.Ticks,
            TimeSpan.FromHours(1),
            processes: new FakeProcesses(new ProcessEntry(50, 1, App)));
        await engine.StartAsync();
        rig.Run(1);
        var appCapture = rig.SystemSource;

        devices.Fail = true;
        await engine.SetSourceAppAsync(null);

        // Its packets keep coming and are still the application's: no line may say otherwise.
        Assert.Same(appCapture, rig.SystemSource);
        Assert.Empty(engine.WaveFacts.Changes);

        rig.Run(1);
        devices.Fail = false;
        await engine.RefreshDevicesAsync();

        Assert.NotSame(appCapture, rig.SystemSource);
        Assert.Equal([new SourceChange(2 * Rate, null)], engine.WaveFacts.Changes);
    }
}
