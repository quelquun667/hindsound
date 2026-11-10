using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>Which source the capture records, and what the settings would offer, from what the sound server has.</summary>
public sealed class PulseServerStateTests
{
    private static readonly PulseServerState Laptop = new(
        DefaultSink: "alsa_output.speakers",
        DefaultSource: "alsa_input.mic",
        Sinks:
        [
            new PulseSink("alsa_output.speakers", "Speakers", "alsa_output.speakers.monitor"),
            new PulseSink("bluez_output.headset", "Headset", "bluez_output.headset.monitor"),
        ],
        Sources:
        [
            new PulseSource("alsa_output.speakers.monitor", "Monitor of Speakers", IsMonitor: true),
            new PulseSource("bluez_output.headset.monitor", "Monitor of Headset", IsMonitor: true),
            new PulseSource("alsa_input.mic", "Internal Microphone", IsMonitor: false),
            new PulseSource("usb_input.mic", "USB Microphone", IsMonitor: false),
        ]);

    [Fact]
    public void The_sound_of_the_pc_is_the_monitor_of_the_default_output()
    {
        Assert.Equal("alsa_output.speakers.monitor", Laptop.Resolve(AudioFlow.System, null));
    }

    [Fact]
    public void A_new_default_output_gives_a_new_source_so_the_engine_moves_to_it()
    {
        var moved = Laptop with { DefaultSink = "bluez_output.headset" };

        Assert.Equal("bluez_output.headset.monitor", moved.Resolve(AudioFlow.System, null));
    }

    [Fact]
    public void An_output_set_in_the_settings_is_recorded_through_its_own_monitor()
    {
        Assert.Equal("bluez_output.headset.monitor", Laptop.Resolve(AudioFlow.System, "bluez_output.headset"));
    }

    [Fact]
    public void An_output_that_is_not_there_gives_nothing_rather_than_another_one()
    {
        Assert.Null(Laptop.Resolve(AudioFlow.System, "hdmi_output.tv"));
        Assert.Null(Laptop.Resolve(AudioFlow.System, "{0.0.0.00000000}.{a-windows-id}"));
    }

    [Fact]
    public void An_output_without_a_monitor_or_no_default_output_gives_nothing()
    {
        var noMonitor = Laptop with { Sinks = [new PulseSink("alsa_output.speakers", "Speakers", null)] };
        var noDefault = Laptop with { DefaultSink = null };

        Assert.Null(noMonitor.Resolve(AudioFlow.System, null));
        Assert.Null(noDefault.Resolve(AudioFlow.System, null));
        Assert.Null(PulseServerState.Empty.Resolve(AudioFlow.System, null));
    }

    [Fact]
    public void The_microphone_is_the_default_input_or_the_one_set()
    {
        Assert.Equal("alsa_input.mic", Laptop.Resolve(AudioFlow.Mic, null));
        Assert.Equal("usb_input.mic", Laptop.Resolve(AudioFlow.Mic, "usb_input.mic"));
        Assert.Null(Laptop.Resolve(AudioFlow.Mic, "usb_input.gone"));
    }

    [Fact]
    public void A_monitor_is_never_taken_as_the_microphone()
    {
        // Without a microphone the server makes the monitor of an output its default source.
        var noMic = Laptop with { DefaultSource = "alsa_output.speakers.monitor" };

        Assert.Null(noMic.Resolve(AudioFlow.Mic, null));
        Assert.Null(Laptop.Resolve(AudioFlow.Mic, "bluez_output.headset.monitor"));
    }

    [Fact]
    public void The_name_shown_is_the_output_for_its_monitor_and_the_microphone_for_itself()
    {
        Assert.Equal("Headset", Laptop.DisplayNameOf(AudioFlow.System, "bluez_output.headset.monitor"));
        Assert.Equal("USB Microphone", Laptop.DisplayNameOf(AudioFlow.Mic, "usb_input.mic"));
        Assert.Null(Laptop.DisplayNameOf(AudioFlow.System, "nothing.monitor"));
    }

    [Fact]
    public void The_settings_would_offer_the_outputs_and_the_inputs_that_are_not_monitors()
    {
        Assert.Equal(
            [new AudioDeviceInfo("alsa_output.speakers", "Speakers"), new AudioDeviceInfo("bluez_output.headset", "Headset")],
            Laptop.Devices(AudioFlow.System));
        Assert.Equal(
            [new AudioDeviceInfo("alsa_input.mic", "Internal Microphone"), new AudioDeviceInfo("usb_input.mic", "USB Microphone")],
            Laptop.Devices(AudioFlow.Mic));
    }

    [Theory]
    [InlineData("Speakers", "Speakers")]
    [InlineData(null, "alsa_output.speakers")]
    [InlineData("", "alsa_output.speakers")]
    [InlineData("  ", "alsa_output.speakers")]
    public void A_device_without_a_description_is_shown_by_its_name(string? description, string shown)
    {
        Assert.Equal(shown, PulseServerState.ShownName("alsa_output.speakers", description));
    }
}
