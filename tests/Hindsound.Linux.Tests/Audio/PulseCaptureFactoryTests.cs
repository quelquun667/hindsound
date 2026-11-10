using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>What the capture factory decides before it calls the sound server.</summary>
public sealed class PulseCaptureFactoryTests
{
    private static readonly PulseServerState Server = new(
        "speakers",
        "speakers.monitor",
        [new PulseSink("speakers", "Speakers", "speakers.monitor")],
        [new PulseSource("speakers.monitor", "Monitor of Speakers", IsMonitor: true)]);

    [Fact]
    public void Leaving_the_app_s_own_sounds_out_is_refused_so_the_engine_opens_an_ordinary_capture_and_says_so()
    {
        var factory = new PulseCaptureFactory(_ => { }, () => Server);

        var opening = factory.OpenAsync(new CaptureRequest(AudioFlow.System, "speakers.monitor", ExcludeOwnProcess: true));

        Assert.True(opening.IsFaulted);
        Assert.IsType<PlatformNotSupportedException>(opening.Exception!.InnerException);
    }

    [Fact]
    public void Capturing_one_application_is_refused()
    {
        var refusal = PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "speakers.monitor", false, TargetProcessId: 42, TargetName: "Game"));

        Assert.IsType<PlatformNotSupportedException>(refusal);
    }

    [Fact]
    public void An_ordinary_capture_of_either_side_is_not_refused()
    {
        Assert.Null(PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "speakers.monitor", false)));
        Assert.Null(PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.Mic, "alsa_input.mic", false)));
    }

    [Fact]
    public void The_device_comes_from_what_the_server_has_now()
    {
        var factory = new PulseCaptureFactory(_ => { }, () => Server);

        Assert.Equal("speakers.monitor", factory.ResolveDevice(AudioFlow.System, null));
        Assert.Null(factory.ResolveDevice(AudioFlow.Mic, null));
    }

    [Fact]
    public void A_server_that_cannot_be_read_gives_no_device_and_is_written_once_until_it_comes_back()
    {
        var lines = new List<string>();
        var reachable = false;
        var factory = new PulseCaptureFactory(lines.Add, () => reachable ? Server : throw new IOException("No sound server: Connection refused"));

        Assert.Null(factory.ResolveDevice(AudioFlow.System, null));
        Assert.Null(factory.ResolveDevice(AudioFlow.Mic, null));
        Assert.Null(factory.ResolveDevice(AudioFlow.System, null));
        reachable = true;
        Assert.Equal("speakers.monitor", factory.ResolveDevice(AudioFlow.System, null));
        reachable = false;
        Assert.Null(factory.ResolveDevice(AudioFlow.System, null));

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal("the sound server could not be read: IOException: No sound server: Connection refused", line));
    }
}
