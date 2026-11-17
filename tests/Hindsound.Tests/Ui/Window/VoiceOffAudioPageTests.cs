using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The voice switch off is a microphone that is not captured: the Audio page shows it like a muted one.</summary>
public sealed class VoiceOffAudioPageTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private void Open(bool voice)
    {
        _rig.Settings.Audio.RecordMic = voice;
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Audio);
        _rig.Screen.Paint(_rig.Canvas);
    }

    [Fact]
    public void With_the_voice_off_the_meter_and_the_gain_go_quiet()
    {
        Open(voice: false);

        Assert.Contains("The microphone is off", _rig.Canvas.Drawn);
        Assert.False(_rig.Screen.WantsMeter);
        Assert.False(_rig.Screen.WidgetById("audio.micGain")!.Enabled);
    }

    [Fact]
    public void With_the_voice_on_they_work()
    {
        Open(voice: true);

        Assert.DoesNotContain("The microphone is off", _rig.Canvas.Drawn);
        Assert.True(_rig.Screen.WantsMeter);
        Assert.True(_rig.Screen.WidgetById("audio.micGain")!.Enabled);
    }
}
