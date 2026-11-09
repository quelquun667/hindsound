using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The side bar with the Now page: one stop for Tab, the caption out of the way, and where the window lands.</summary>
public sealed class ScreenNowNavTests : IDisposable
{
    private sealed class FakeNow : INowPanel;

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private const float NavX = 40;
    private const float NavTop = SettingsScreen.BandHeight + 16;

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    [Fact]
    public void Without_a_panel_the_window_stays_as_it_was()
    {
        Assert.Equal(Section.Hotkeys, Screen.Section);
        Assert.Equal(196, Screen.WidgetById("nav")!.Bounds.H);

        Screen.SetNow(null);

        Assert.Equal(Section.Hotkeys, Screen.Section);
        Assert.Equal(196, Screen.WidgetById("nav")!.Bounds.H);
    }

    [Fact]
    public void A_panel_adds_the_line_and_the_window_goes_on_it()
    {
        Screen.SetNow(new FakeNow());

        Assert.Equal(Section.Now, Screen.Section);
        Assert.Equal("nav", Screen.FocusId);
        Assert.Equal(260, Screen.WidgetById("nav")!.Bounds.H);
    }

    [Fact]
    public void Giving_the_panel_again_does_not_take_the_user_back_to_now()
    {
        Screen.SetNow(new FakeNow());
        Screen.ShowSection(Section.Audio);

        Screen.SetNow(new FakeNow());

        Assert.Equal(Section.Audio, Screen.Section);
    }

    [Fact]
    public void Taking_the_panel_away_leaves_now_for_hotkeys()
    {
        Screen.SetNow(new FakeNow());

        Screen.SetNow(null);

        Assert.Equal(Section.Hotkeys, Screen.Section);
        Assert.Equal(196, Screen.WidgetById("nav")!.Bounds.H);
    }

    [Fact]
    public void Taking_the_panel_away_elsewhere_stays_there()
    {
        Screen.SetNow(new FakeNow());
        Screen.ShowSection(Section.Export);

        Screen.SetNow(null);

        Assert.Equal(Section.Export, Screen.Section);
    }

    [Fact]
    public void The_side_bar_is_one_stop_for_tab_with_or_without_the_caption()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        var before = Screen.StopIds();
        Assert.Single(before, id => id.StartsWith("nav", StringComparison.Ordinal));

        Screen.SetNow(new FakeNow());

        var stops = Screen.StopIds();
        Assert.Equal(["band.pause", "band.save", "nav", "foot.open"], stops);
    }

    [Fact]
    public void Tab_goes_round_now_and_comes_back_to_the_side_bar()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());

        _rig.FocusOn("foot.open");
        _rig.Press(Vk.Tab);
        Assert.Equal("band.pause", Screen.FocusId);
        _rig.Press(Vk.Tab, Vk.Tab);
        Assert.Equal("nav", Screen.FocusId);
    }

    [Fact]
    public void The_arrows_walk_past_the_caption()
    {
        Screen.SetNow(new FakeNow());

        _rig.Press(Vk.Down);
        Assert.Equal(Section.Hotkeys, Screen.Section);
        Assert.Equal("nav", Screen.FocusId);

        _rig.Press(Vk.Up);
        Assert.Equal(Section.Now, Screen.Section);
        Assert.Equal("nav", Screen.FocusId);

        _rig.Press(Vk.Up);
        Assert.Equal(Section.Now, Screen.Section);

        _rig.Press(Vk.End);
        Assert.Equal(Section.General, Screen.Section);

        _rig.Press(Vk.Down);
        Assert.Equal(Section.General, Screen.Section);

        _rig.Press(Vk.Home);
        Assert.Equal(Section.Now, Screen.Section);
    }

    [Fact]
    public void The_arrows_stay_within_the_five_pages_without_a_panel()
    {
        _rig.Press(Vk.Up);
        Assert.Equal(Section.Hotkeys, Screen.Section);

        _rig.Press(Vk.End);
        Assert.Equal(Section.General, Screen.Section);

        _rig.Press(Vk.Home);
        Assert.Equal(Section.Hotkeys, Screen.Section);
    }

    [Fact]
    public void A_click_on_a_line_goes_there_and_a_click_on_the_caption_goes_nowhere()
    {
        Screen.SetNow(new FakeNow());

        Click(NavX, NavTop + 104 + 18);
        Assert.Equal(Section.Durations, Screen.Section);

        Click(NavX, NavTop + 54);
        Assert.Equal(Section.Durations, Screen.Section);

        Click(NavX, NavTop + 18);
        Assert.Equal(Section.Now, Screen.Section);

        Click(NavX, NavTop + 224 + 18);
        Assert.Equal(Section.General, Screen.Section);
    }

    [Fact]
    public void The_caption_is_drawn_only_when_there_is_a_view_above_it()
    {
        var alone = new FakeCanvas();
        Screen.Paint(alone);
        Assert.DoesNotContain("Settings", alone.Drawn);
        Assert.DoesNotContain("Now", alone.Drawn);

        Screen.SetNow(new FakeNow());
        var together = new FakeCanvas();
        Screen.Paint(together);

        Assert.Contains("Settings", together.Drawn);
        Assert.Contains("Now", together.Drawn);
        Assert.Equal(Theme.Faded, together.TextColors["Settings"]);
        Assert.Equal(["Now", "Hotkeys", "Durations", "Audio", "Export", "General"], together.Drawn.Where(new HashSet<string> { "Now", "Hotkeys", "Durations", "Audio", "Export", "General" }.Contains).Take(6));
    }

    [Fact]
    public void The_line_of_the_view_on_screen_is_the_one_lit()
    {
        Screen.SetNow(new FakeNow());
        var onNow = new FakeCanvas();
        Screen.Paint(onNow);
        Assert.Equal(Theme.Text, onNow.TextColors["Now"]);
        Assert.Equal(Theme.Muted, onNow.TextColors["Hotkeys"]);

        Screen.ShowSection(Section.Export);
        var onExport = new FakeCanvas();
        Screen.Paint(onExport);
        Assert.Equal(Theme.Muted, onExport.TextColors["Now"]);
        Assert.Equal(Theme.Text, onExport.TextColors["Export"]);
    }

    [Fact]
    public void A_click_below_the_last_line_goes_nowhere()
    {
        Screen.SetNow(new FakeNow());

        Click(NavX, NavTop + 260 + 4);

        Assert.Equal(Section.Now, Screen.Section);
    }

    [Fact]
    public void A_page_with_a_field_gives_the_keyboard_back_when_the_side_bar_leaves_it()
    {
        Screen.SetNow(new FakeNow());
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.template");
        Assert.True(_rig.Host.EditVisible);

        Click(NavX, NavTop + 18);

        Assert.Equal(Section.Now, Screen.Section);
        Assert.False(_rig.Host.EditVisible);
        Assert.NotEqual("export.template", Screen.FocusId);
    }

    [Fact]
    public void A_gesture_that_leaves_the_page_ends_a_hotkey_capture()
    {
        Screen.SetNow(new FakeNow());
        Screen.ShowSection(Section.Hotkeys);
        _rig.FocusOn(Screen.StopIds().First(id => id.StartsWith("hotkey.", StringComparison.Ordinal)));
        _rig.Press(Vk.Enter);
        Assert.True(Screen.Listening);

        // Hotkeys again, as a click on a refused hotkey would ask: nothing to leave.
        Screen.ShowSection(Section.Hotkeys);
        Assert.True(Screen.Listening);

        Screen.ShowSection(Section.Now);
        Assert.False(Screen.Listening);
    }

    [Fact]
    public void Now_has_nothing_to_scroll_after_a_long_page()
    {
        Screen.SetNow(new FakeNow());
        Screen.ShowSection(Section.General);
        Assert.True(Screen.MaxScroll > 0);

        Screen.ShowSection(Section.Now);

        Assert.Equal(0, Screen.MaxScroll);
        Assert.Equal(0, Screen.Scroll);
    }

    // ---------------------------------------------------------------- the last page seen

    [Fact]
    public void The_app_hears_each_page_of_the_settings_and_not_the_rest()
    {
        var seen = new List<Section>();
        Screen.SettingsPageSeen = seen.Add;

        Screen.SetNow(new FakeNow());
        Assert.Empty(seen);

        Screen.ShowSection(Section.Audio);
        Screen.ShowSection(Section.Audio);
        Screen.ShowSection(Section.Now);
        _rig.Press(Vk.Down);

        Assert.Equal([Section.Audio, Section.Hotkeys], seen);
    }

    [Fact]
    public void The_updates_section_counts_as_a_page_seen_and_the_card_does_not()
    {
        var seen = new List<Section>();
        Screen.SettingsPageSeen = seen.Add;
        Screen.SetUpdates(new FakePanel { View = UpdatesSectionTests.ReadyView() });
        Screen.SetNow(new FakeNow());

        Screen.ShowUpdateCard();
        Assert.Empty(seen);

        Screen.ShowUpdates();
        Assert.Equal([Section.General], seen);
    }

    // ---------------------------------------------------------------- the cards

    [Fact]
    public void The_panel_does_not_take_the_window_off_the_first_launch_card()
    {
        using var rig = new ScreenRig(firstRun: true);

        rig.Screen.SetNow(new FakeNow());

        Assert.Equal(Section.FirstRun, rig.Screen.Section);
        Assert.Equal(0, rig.Screen.FirstRunStep);
    }

    [Fact]
    public void The_end_of_the_first_launch_card_lands_on_now()
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.Screen.SetNow(new FakeNow());

        for (var step = 0; rig.Screen.Section == Section.FirstRun && step < 5; step++)
        {
            rig.FocusOn("run.next");
            rig.Press(Vk.Enter);
        }

        Assert.Equal(Section.Now, rig.Screen.Section);
        Assert.Equal("nav", rig.Screen.FocusId);
        Assert.Equal(0, rig.Screen.FirstRunStep);
    }

    [Fact]
    public void The_end_of_the_first_launch_card_stays_on_hotkeys_without_a_panel()
    {
        using var rig = new ScreenRig(firstRun: true);

        for (var step = 0; rig.Screen.Section == Section.FirstRun && step < 5; step++)
        {
            rig.FocusOn("run.next");
            rig.Press(Vk.Enter);
        }

        Assert.Equal(Section.Hotkeys, rig.Screen.Section);
    }

    [Fact]
    public void The_update_card_still_opens_from_now_and_closes_on_the_updates_section()
    {
        var panel = new FakePanel { View = UpdatesSectionTests.ReadyView() };
        Screen.SetUpdates(panel);
        Screen.SetNow(new FakeNow());

        Screen.ShowUpdateCard();
        Assert.Equal(Section.UpdateCard, Screen.Section);

        _rig.Press(Vk.Escape);

        Assert.Equal(Section.General, Screen.Section);
    }
}
