using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class UpdatesRowTests
{
    [Fact]
    public void The_row_of_the_updates_section_does_not_repeat_its_title()
    {
        using var rig = new ScreenRig();
        var panel = new FakePanel { View = new("Version 0.1.0", "Up to date.", Active: true, CheckEnabled: true, null, UpdateEnabled: false, ShowWhatsNew: false, ShowSkip: false, ShowUpdate: false) };
        rig.Screen.SetUpdates(panel);
        rig.Screen.ShowSection(Section.General);
        var canvas = new FakeCanvas();
        rig.Screen.Paint(canvas);

        Assert.Contains("When a version is out", canvas.Drawn);
        Assert.Single(canvas.Drawn, text => text == "Updates");
    }

    [Fact]
    public void The_new_words_fit_beside_the_list()
    {
        using var canvas = new GdiCanvas(760, 488, 1.0);
        var label = canvas.Measure("When a version is out", Face.Body, 14);

        // A row runs from 200 to the list, which starts at 728 - 220.
        Assert.True(label <= 308, $"{label}");
    }
}
