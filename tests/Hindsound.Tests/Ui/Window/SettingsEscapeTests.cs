using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>File name and Color code write each valid value as it is typed: Escape has to give back the one from before.</summary>
public sealed class SettingsEscapeTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private string Template => Screen.Editor.Settings.Export.FileNameTemplate;

    private void FocusTheTemplate()
    {
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.template");
    }

    private void FocusTheColorCode()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal("general.hex", Screen.FocusId);
    }

    [Fact]
    public void Escape_gives_back_the_name_from_before_the_typing()
    {
        FocusTheTemplate();
        var before = Template;

        Screen.EditChanged("clip {date}");
        Screen.EditChanged("clip {date} two");
        Assert.Equal("clip {date} two", Template);
        _rig.Backend.Changes.Clear();

        Screen.Key(Vk.Escape, 0);

        Assert.Equal(before, Template);
        Assert.Equal(before, _rig.Host.EditText);
        Assert.Contains(SettingsChange.Export, _rig.Backend.Changes);
    }

    [Fact]
    public void Escape_after_an_invalid_value_goes_past_the_valid_one_before_it()
    {
        FocusTheTemplate();
        var before = Template;

        Screen.EditChanged("good");
        Screen.EditChanged("a<b");
        Screen.Key(Vk.Escape, 0);

        Assert.Equal(before, Template);
        Assert.Equal(before, Assert.IsType<TextField>(Screen.Focus).State.Draft);
        Assert.Null(Assert.IsType<TextField>(Screen.Focus).State.Message);
    }

    [Fact]
    public void Escape_gives_back_a_preset_color_and_closes_the_field()
    {
        FocusTheColorCode();
        var before = Screen.Editor.AccentHex;

        Screen.EditChanged("#FF8800");
        Assert.Equal("#FF8800", Screen.Editor.AccentHex);
        _rig.Backend.Changes.Clear();

        Screen.Key(Vk.Escape, 0);

        Assert.Equal(before, Screen.Editor.AccentHex);
        Assert.Equal("general.accent", Screen.FocusId);
        Assert.Contains(SettingsChange.Accent, _rig.Backend.Changes);
    }

    [Fact]
    public void Escape_gives_back_a_custom_color_and_stays_in_the_field()
    {
        var settings = new AppSettings().Normalize();
        settings.General.AccentColor = "#FF8800";
        using var rig = new ScreenRig(settings);
        rig.Screen.ShowSection(Section.General);
        rig.FocusOn("general.hex");

        rig.Screen.EditChanged("#88FF00");
        rig.Screen.Key(Vk.Escape, 0);

        Assert.Equal("#FF8800", rig.Screen.Editor.AccentHex);
        Assert.Equal("general.hex", rig.Screen.FocusId);
        Assert.Equal("#FF8800", rig.Host.EditText);
    }

    [Fact]
    public void Enter_keeps_the_value_for_good()
    {
        FocusTheTemplate();

        Screen.EditChanged("kept");
        Screen.Key(Vk.Enter, 0);
        Screen.EditChanged("not kept");
        Screen.Key(Vk.Escape, 0);

        Assert.Equal("kept", Template);
    }

    [Fact]
    public void Enter_on_an_invalid_value_keeps_nothing_and_escape_still_goes_back()
    {
        FocusTheTemplate();
        var before = Template;

        Screen.EditChanged("good");
        Screen.EditChanged("a<b");
        Screen.Key(Vk.Enter, 0);
        Screen.Key(Vk.Escape, 0);

        Assert.Equal(before, Template);
    }

    [Fact]
    public void Leaving_the_field_keeps_the_value_for_good()
    {
        FocusTheTemplate();

        Screen.EditChanged("kept");
        Screen.Key(Vk.Tab, 0);
        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal("export.template", Screen.FocusId);
        Screen.EditChanged("not kept");
        Screen.Key(Vk.Escape, 0);

        Assert.Equal("kept", Template);
    }

    [Fact]
    public void Escape_without_typing_writes_nothing()
    {
        FocusTheTemplate();
        _rig.Backend.Changes.Clear();

        Screen.Key(Vk.Escape, 0);

        Assert.Empty(_rig.Backend.Changes);
    }

    [Fact]
    public void A_value_only_written_on_leaving_needs_no_writing_back()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        _rig.Backend.Changes.Clear();

        Screen.EditChanged("1:30");
        Screen.Key(Vk.Escape, 0);

        Assert.Equal(30, _rig.Settings.ClipSeconds);
        Assert.Equal("0:30", _rig.Host.EditText);
        Assert.Empty(_rig.Backend.Changes);
    }

    [Fact]
    public void A_file_read_during_the_typing_is_what_escape_keeps()
    {
        FocusTheTemplate();
        Screen.EditChanged("typed");
        var fromTheFile = new AppSettings().Normalize();
        fromTheFile.Export.FileNameTemplate = "from the file";

        Screen.ReplaceSettings(fromTheFile);
        Screen.Key(Vk.Escape, 0);

        Assert.Equal("from the file", Template);
        Assert.Equal("from the file", _rig.Host.EditText);
    }
}
