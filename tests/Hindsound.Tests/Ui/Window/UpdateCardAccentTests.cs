using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// An accented button is painted in the accent whether it is enabled or not: a greyed Update must lose the accent too,
/// or it still looks like the button to press.
/// </summary>
public sealed class UpdateCardAccentTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private Button Update => (Button)Screen.WidgetById("card.update")!;

    [Fact]
    public void Clip_being_saved_takes_the_accent_off_update()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView(), Busy = true };
        Screen.SetUpdates(panel);
        Screen.ShowUpdateCard();

        Assert.False(Update.Enabled);
        Assert.False(Update.Accented);
    }

    [Fact]
    public void Download_under_way_takes_the_accent_off_update()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView() with { CardBusy = true, CardLine = "Downloading 0.2.0..." } };
        Screen.SetUpdates(panel);
        Screen.ShowUpdateCard();

        Assert.False(Update.Enabled);
        Assert.False(Update.Accented);
    }

    [Fact]
    public void Accent_comes_back_with_the_button()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView(), Busy = true };
        Screen.SetUpdates(panel);
        Screen.ShowUpdateCard();

        panel.Busy = false;
        Screen.RefreshUpdates();

        Assert.True(Update.Enabled);
        Assert.True(Update.Accented);
    }
}
