using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A restart only follows a card on screen: whatever takes the window off the card drops the click.</summary>
public sealed class UpdateCardLeaveTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private FakePanel OnTheCard()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView() };
        Screen.SetUpdates(panel);
        Screen.ShowUpdateCard();
        Assert.Equal(Section.UpdateCard, Screen.Section);
        return panel;
    }

    [Fact]
    public void Showing_the_updates_section_drops_the_click()
    {
        // What a click on an update notification does.
        var panel = OnTheCard();

        Screen.ShowUpdates();

        Assert.Equal(1, panel.Cancels);
        Assert.Equal(Section.General, Screen.Section);
        Assert.True(Screen.Scroll > 0);
    }

    [Fact]
    public void Another_section_drops_the_click_and_stays_there()
    {
        var panel = OnTheCard();

        Screen.ShowSection(Section.Audio);

        Assert.Equal(1, panel.Cancels);
        Assert.Equal(Section.Audio, Screen.Section);
    }

    [Fact]
    public void Cancel_drops_the_click_once()
    {
        var panel = OnTheCard();

        _rig.Press(Vk.Escape);

        Assert.Equal(1, panel.Cancels);
        Assert.Equal(Section.General, Screen.Section);
    }

    [Fact]
    public void Showing_the_section_from_elsewhere_drops_nothing()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView() };
        Screen.SetUpdates(panel);
        Screen.ShowSection(Section.Audio);

        Screen.ShowUpdates();

        Assert.Equal(0, panel.Cancels);
    }
}
