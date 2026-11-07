using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The first-launch card only goes away by its own buttons: a click on an update notice or on "Update to X..." brings the
/// window back as it is. Left, its steps would never come back, and the updater would stop waiting for them.
/// </summary>
public sealed class FirstRunKeepsCardTests : IDisposable
{
    private readonly ScreenRig _rig = new(firstRun: true);

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private FakePanel WithPanel()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView() };
        Screen.SetUpdates(panel);
        return panel;
    }

    [Fact]
    public void Show_updates_keeps_the_first_launch_card()
    {
        WithPanel();

        Screen.ShowUpdates();

        Assert.Equal(Section.FirstRun, Screen.Section);
        Assert.Equal(0, Screen.FirstRunStep);
    }

    [Fact]
    public void Update_card_does_not_open_over_the_first_launch_card()
    {
        var panel = WithPanel();

        Screen.ShowUpdateCard();

        Assert.Equal(Section.FirstRun, Screen.Section);
        Assert.Empty(panel.Confirms);
        Assert.Equal(0, panel.Cancels);
    }

    [Fact]
    public void Step_reached_stays_after_a_refused_show()
    {
        WithPanel();
        _rig.FocusOn("run.next");
        _rig.Press(Vk.Enter);

        Screen.ShowUpdates();
        Screen.ShowUpdateCard();

        Assert.Equal(Section.FirstRun, Screen.Section);
        Assert.Equal(1, Screen.FirstRunStep);
    }

    [Fact]
    public void Outside_the_first_launch_both_still_show()
    {
        using var rig = new ScreenRig();
        rig.Screen.SetUpdates(new FakePanel { View = UpdatesSectionTests.ReadyView() });

        rig.Screen.ShowUpdates();
        Assert.Equal(Section.General, rig.Screen.Section);

        rig.Screen.ShowUpdateCard();
        Assert.Equal(Section.UpdateCard, rig.Screen.Section);
    }
}
