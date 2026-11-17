using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The switch "Record my voice" on the line of the title of the Mic lane.</summary>
public sealed class NowVoiceSwitchTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly List<ScreenRig> _rigs = [];

    public void Dispose()
    {
        foreach (var rig in _rigs)
        {
            rig.Dispose();
        }
    }

    private static AppSettings Settings(string mic = "{real-mic}", bool recordMic = true) =>
        new AppSettings { Audio = new AudioSettings { MicDeviceId = mic, RecordMic = recordMic } }.Normalize();

    private ScreenRig Shown(AppSettings settings)
    {
        var rig = new ScreenRig(settings);
        _rigs.Add(rig);
        rig.Screen.SetNow(new FakeNow());
        rig.Screen.UpdateStatus(ScreenRig.Capturing(micEnabled: settings.Audio.MicCaptured));
        return rig;
    }

    private static void Click(SettingsScreen screen, Widget widget)
    {
        screen.MouseDown(widget.Bounds.MidX, widget.Bounds.MidY);
        screen.MouseUp(widget.Bounds.MidX, widget.Bounds.MidY);
    }

    [Fact]
    public void The_switch_is_the_stop_after_the_list_of_programs_and_before_the_foot()
    {
        var rig = Shown(Settings());

        Assert.Equal(["band.pause", "band.save", "nav", "now.record", "now.voice", "foot.open"], rig.Screen.StopIds());
    }

    [Fact]
    public void The_switch_has_its_words_and_the_title_of_the_lane_stays_Mic()
    {
        var rig = Shown(Settings());
        var canvas = new FakeCanvas();

        rig.Screen.Paint(canvas);

        Assert.True(rig.Screen.WidgetById("now.voice")!.Enabled);
        Assert.Contains("Record my voice", canvas.Drawn);
        Assert.Equal("Mic", WaveView.MicLaneTitle(ScreenRig.Capturing()));
    }

    [Fact]
    public void With_the_microphone_disabled_the_switch_is_grey_and_a_click_changes_nothing()
    {
        var settings = Settings(AudioSettings.DisabledMic);
        var rig = Shown(settings);
        var voice = rig.Screen.WidgetById("now.voice")!;

        Assert.False(voice.Enabled);
        Assert.DoesNotContain("now.voice", rig.Screen.StopIds());

        Click(rig.Screen, voice);

        Assert.True(settings.Audio.RecordMic);
        Assert.Equal(AudioSettings.DisabledMic, settings.Audio.MicDeviceId);
        Assert.Empty(rig.Backend.Changes);
    }

    [Fact]
    public void A_click_switches_the_voice_off_announces_the_devices_and_keeps_the_microphone_chosen()
    {
        var settings = Settings();
        var rig = Shown(settings);

        Click(rig.Screen, rig.Screen.WidgetById("now.voice")!);

        Assert.False(settings.Audio.RecordMic);
        Assert.Equal("{real-mic}", settings.Audio.MicDeviceId);
        Assert.True((rig.Backend.AllChanges & SettingsChange.Devices) != 0);
    }

    [Fact]
    public void The_keyboard_switches_it_back_on()
    {
        var settings = Settings(recordMic: false);
        var rig = Shown(settings);

        rig.FocusOn("now.voice");
        rig.Press(Vk.Space);

        Assert.True(settings.Audio.RecordMic);
        Assert.Equal("{real-mic}", settings.Audio.MicDeviceId);
    }
}
