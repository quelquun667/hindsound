using Hindsound.Audio;
using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The band as the window builds and paints it: the widths are tested apart, this is where the pieces really land.</summary>
public class BandLayoutTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_pause_button_sits_where_the_save_button_leaves_it_and_the_texts_end_before_it(bool micOn)
    {
        var settings = new AppSettings { BufferSeconds = 1800 }.Normalize();
        using var rig = new ScreenRig(settings);
        var status = ScreenRig.Capturing(micCapturing: micOn, micEnabled: micOn) with { Buffered = TimeSpan.FromSeconds(29 * 60 + 50) };
        rig.Screen.UpdateStatus(status);
        rig.Canvas.DrawnAt.Clear();
        rig.Screen.Paint(rig.Canvas);

        var pause = rig.Screen.WidgetById("band.pause")!.Bounds;
        var save = rig.Screen.WidgetById("band.save")!.Bounds;
        var label = StatusText.SaveLabel(settings.ClipSeconds);
        var gesture = settings.Hotkeys.SaveDefault ?? string.Empty;

        Assert.Equal(SettingsScreen.BandPauseLeft(rig.Canvas, label, gesture), pause.X);
        Assert.Equal(save.X - 20, pause.X + pause.W);

        var band = StatusText.From(status, settings.BufferSeconds);
        var drawn = rig.Canvas.DrawnAt.Where(d => d.Text == band.Title || d.Text == band.Mic || d.Text == band.Duration || d.Text == band.Ram).ToList();
        Assert.Contains(drawn, d => d.Text == band.Title);
        foreach (var (text, x) in drawn)
        {
            var face = text == band.Title ? Face.Title : text == band.Mic ? Face.Body : Face.Mono;
            var size = text == band.Title ? 22 : text == band.Mic ? 14 : 13;
            Assert.True(x + rig.Canvas.Measure(text, face, size) <= pause.X, $"{text} ends at {x + rig.Canvas.Measure(text, face, size)}, the Pause button starts at {pause.X}");
        }
    }
}
