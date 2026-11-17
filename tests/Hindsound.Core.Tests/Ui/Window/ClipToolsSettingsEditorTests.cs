using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class ClipToolsSettingsEditorTests
{
    private readonly List<SettingsChange> _changes = [];

    private SettingsEditor Editor(AppSettings? settings = null) =>
        new((settings ?? new AppSettings()).Normalize(), _changes.Add);

    [Fact]
    public void The_voice_switch_reaches_the_devices_and_leaves_the_microphone_as_chosen()
    {
        var settings = new AppSettings();
        settings.Audio.MicDeviceId = "mic-1";
        var editor = Editor(settings);

        editor.SetRecordMic(false);

        Assert.False(settings.Audio.RecordMic);
        Assert.Equal("mic-1", settings.Audio.MicDeviceId);
        Assert.Equal([SettingsChange.Devices], _changes);
    }

    [Fact]
    public void The_voice_switch_says_nothing_when_it_does_not_change()
    {
        var editor = Editor();

        editor.SetRecordMic(true);

        Assert.Empty(_changes);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(140)]
    public void A_window_size_offered_is_kept_and_redraws_the_window(int percent)
    {
        var settings = new AppSettings();
        var editor = Editor(settings);

        editor.SetWindowZoom(percent);

        Assert.Equal(percent, settings.General.WindowZoom);
        Assert.Equal([SettingsChange.General | SettingsChange.WindowSize], _changes);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(110)]
    [InlineData(0)]
    [InlineData(200)]
    public void The_same_size_or_one_not_offered_changes_nothing(int percent)
    {
        var settings = new AppSettings();
        var editor = Editor(settings);

        editor.SetWindowZoom(percent);

        Assert.Equal(120, settings.General.WindowZoom);
        Assert.Empty(_changes);
    }
}
