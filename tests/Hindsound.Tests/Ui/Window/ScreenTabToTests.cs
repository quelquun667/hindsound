using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>What --settings-probe uses to reach a text field, whatever keys are held on the real keyboard.</summary>
public sealed class ScreenTabToTests
{
    [Fact]
    public void It_stops_on_the_field_it_was_asked_for()
    {
        using var rig = new ScreenRig();
        rig.Screen.ShowSection(Section.Export);

        Assert.True(rig.Screen.TabTo("export.template"));
        Assert.Equal("export.template", rig.Screen.FocusId);
    }

    [Fact]
    public void It_says_so_when_the_field_is_not_on_the_page()
    {
        using var rig = new ScreenRig();
        rig.Screen.ShowSection(Section.Durations);

        Assert.False(rig.Screen.TabTo("export.template"));
    }

    [Fact]
    public void It_does_nothing_when_the_focus_is_already_there()
    {
        using var rig = new ScreenRig();
        rig.Screen.ShowSection(Section.Export);
        rig.Screen.TabTo("export.template");
        var resets = rig.Host.EditResets;

        Assert.True(rig.Screen.TabTo("export.template"));
        Assert.Equal(resets, rig.Host.EditResets);
    }
}
