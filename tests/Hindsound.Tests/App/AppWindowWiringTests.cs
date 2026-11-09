using Hindsound.App;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// What the app gives a new window and where its gestures lead, with the app built and never run, and no real window made:
/// WindowGoingTests and WindowRoutesTests cover the table, this covers that the app uses it.
/// </summary>
public class AppWindowWiringTests
{
    private static HindsoundApp Built(TempFolder temp, List<Section> opened)
    {
        var options = AppOptions.Parse(["--settings", temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        return new HindsoundApp(options, new Journal(temp.Combine("settings.log"))) { OpenInstead = opened.Add };
    }

    [Fact]
    public void A_new_screen_is_given_the_now_page_and_opens_on_it()
    {
        using var temp = new TempFolder();
        using var rig = new ScreenRig();
        var app = Built(temp, []);
        Assert.Equal(Section.Hotkeys, rig.Screen.Section);

        app.WireScreen(rig.Screen);

        Assert.Equal(Section.Now, rig.Screen.Section);
    }

    [Fact]
    public void A_new_screen_tells_the_app_the_settings_page_it_was_on()
    {
        using var temp = new TempFolder();
        using var rig = new ScreenRig();
        var app = Built(temp, []);
        app.WireScreen(rig.Screen);
        Assert.Equal(Section.Hotkeys, app.LastSettingsPage);

        rig.Screen.ShowSection(Section.Export);

        Assert.Equal(Section.Export, app.LastSettingsPage);
    }

    [Fact]
    public void The_menu_entry_Settings_opens_the_page_seen_last_and_Open_Hindsound_opens_Now()
    {
        using var temp = new TempFolder();
        using var rig = new ScreenRig();
        var opened = new List<Section>();
        var app = Built(temp, opened);
        app.WireScreen(rig.Screen);
        rig.Screen.ShowSection(Section.Export);

        app.OnTrayMessage(Native.WM_CONTEXTMENU, (nuint)TrayCommand.Settings);
        app.OnTrayMessage(Native.WM_CONTEXTMENU, (nuint)TrayCommand.OpenHindsound);

        Assert.Equal([Section.Export, Section.Now], opened);
    }

    [Fact]
    public void A_click_on_the_capture_on_notice_opens_the_window_on_Now()
    {
        using var temp = new TempFolder();
        var opened = new List<Section>();
        var app = Built(temp, opened);

        app.NotificationClicked(NotificationStates.CaptureOn(300, "F24"));

        Assert.Equal([Section.Now], opened);
    }
}
