using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>The switch of the voice closes the microphone for the engine and leaves the device the user chose where it was.</summary>
public sealed class RecordMicSwitchTests
{
    private static AudioSettings Audio(string mic, bool recordMic) => new() { MicDeviceId = mic, RecordMic = recordMic };

    private static AudioEngineStatus Capturing() =>
        new(CaptureActivity.Capturing, true, true, true, "Speakers", "Headset", true, TimeSpan.FromSeconds(62), 86_688_000);

    [Fact]
    public void Switched_off_with_a_microphone_chosen_the_microphone_is_closed_and_the_choice_stays()
    {
        var audio = Audio("{real-mic}", recordMic: false);

        var devices = SettingsMapping.ToDevices(audio);
        var engine = SettingsMapping.ToEngineOptions(new AppSettings { Audio = audio }, new AppOptions());

        Assert.False(devices.MicEnabled);
        Assert.Null(devices.MicDeviceId);
        Assert.False(engine.MicEnabled);
        Assert.Equal("{real-mic}", audio.MicDeviceId);
    }

    [Fact]
    public void Switched_on_the_chosen_microphone_is_opened()
    {
        var devices = SettingsMapping.ToDevices(Audio("{real-mic}", recordMic: true));

        Assert.True(devices.MicEnabled);
        Assert.Equal("{real-mic}", devices.MicDeviceId);
    }

    [Fact]
    public void A_microphone_disabled_in_the_settings_stays_closed_with_the_switch_on()
    {
        var devices = SettingsMapping.ToDevices(Audio(AudioSettings.DisabledMic, recordMic: true));

        Assert.False(devices.MicEnabled);
        Assert.Null(devices.MicDeviceId);
    }

    [Fact]
    public void The_memory_estimate_leaves_out_the_microphone_when_it_is_not_captured()
    {
        var on = new AppSettings { Audio = Audio("{real-mic}", recordMic: true) };
        var off = new AppSettings { Audio = Audio("{real-mic}", recordMic: false) };

        Assert.True(RamEstimate.Megabytes(off) < RamEstimate.Megabytes(on));
        Assert.Equal(RamEstimate.Megabytes(off.BufferSeconds, micActive: false), RamEstimate.Megabytes(off));
    }

    [Fact]
    public void Closing_the_microphone_from_the_switch_raises_no_notice()
    {
        var closed = Capturing() with { MicEnabled = false, MicCapturing = false };

        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(Capturing(), closed));
        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(closed, closed));
    }
}
