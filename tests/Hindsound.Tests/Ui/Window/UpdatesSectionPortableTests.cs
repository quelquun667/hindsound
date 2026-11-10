using Hindsound.Settings;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The Updates section of the portable copy, which only tells: Ask me and Off, Check now, Download and Skip this version.
/// Download opens the page of the release through the panel, never the card.
/// </summary>
public sealed class UpdatesSectionPortableTests : IDisposable
{
    private static readonly UpdateVersion Installed = new(0, 1, 0);
    private static readonly UpdateVersion Next = new(0, 2, 0);

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static UpdatePanelView AnnouncingView() => UpdatePanel.Compute(new UpdatePanelFacts
    {
        Installed = Installed,
        Mode = UpdateMode.Ask,
        Now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero),
        LastCheck = new DateTimeOffset(2026, 11, 10, 11, 0, 0, TimeSpan.Zero),
        HighestSeen = Next,
        Manifest = Next,
        AnnouncesOnly = true,
    });

    private FakePanel WithPanel(UpdatePanelView view)
    {
        var panel = new FakePanel { View = view };
        Screen.SetUpdates(panel);
        Screen.ShowSection(Section.General);
        return panel;
    }

    private Dropdown List => Assert.IsType<Dropdown>(Screen.WidgetById("general.updates"));

    [Fact]
    public void The_list_offers_ask_me_and_off_only()
    {
        WithPanel(AnnouncingView());

        Assert.Equal(["ask", "off"], List.Items.Select(i => i.Value));
        Assert.Equal(["Ask me", "Off"], List.Items.Select(i => i.Label));
    }

    [Theory]
    [InlineData(UpdateSettings.Auto, 0)]
    [InlineData(UpdateSettings.Ask, 0)]
    [InlineData(UpdateSettings.Off, 1)]
    public void A_saved_mode_reads_as_ask_me_or_off(string saved, int shown)
    {
        _rig.Settings.General.Updates.Mode = saved;
        WithPanel(AnnouncingView());

        Assert.Equal(shown, List.SelectedIndex);
    }

    [Fact]
    public void Picking_off_writes_off()
    {
        WithPanel(AnnouncingView());

        List.Pick(1);

        Assert.Equal(UpdateSettings.Off, _rig.Settings.General.Updates.Mode);
    }

    [Fact]
    public void The_buttons_are_check_now_download_and_skip()
    {
        WithPanel(AnnouncingView());

        var stops = Screen.StopIds().ToList();
        var test = stops.IndexOf("general.test");
        Assert.Equal(["general.updates", "updates.check", "updates.download", "updates.skip"], stops.Skip(test + 1).Take(4));
        Assert.Null(Screen.WidgetById("updates.now"));
        Assert.Null(Screen.WidgetById("updates.whatsnew"));
        var canvas = new RecordingCanvas();
        Screen.SetCanvas(canvas);
        Screen.Paint(canvas);
        Assert.Contains("Download", canvas.Texts);
        Assert.Contains("Version 0.2.0 is available.", canvas.Texts);
        Assert.DoesNotContain("Update now", canvas.Texts);
        Assert.DoesNotContain("What's new", canvas.Texts);
        Assert.DoesNotContain("Install automatically", canvas.Texts);
    }

    [Fact]
    public void Download_opens_the_page_through_the_panel_and_never_the_card()
    {
        var panel = WithPanel(AnnouncingView());

        _rig.FocusOn("updates.download");
        _rig.Press(Vk.Enter);

        Assert.Equal(1, panel.WhatsNews);
        Assert.Empty(panel.Confirms);
        Assert.Equal(Section.General, Screen.Section);
    }

    [Fact]
    public void Check_now_and_skip_still_call_the_panel()
    {
        var panel = WithPanel(AnnouncingView());

        _rig.FocusOn("updates.check");
        _rig.Press(Vk.Enter);
        _rig.FocusOn("updates.skip");
        _rig.Press(Vk.Enter);

        Assert.Equal(1, panel.Checks);
        Assert.Equal(1, panel.Skips);
    }

    [Fact]
    public void The_installed_copy_keeps_its_three_modes_and_update_now()
    {
        WithPanel(UpdatesSectionTests.ReadyView());

        Assert.Equal(["auto", "ask", "off"], List.Items.Select(i => i.Value));
        Assert.NotNull(Screen.WidgetById("updates.now"));
        Assert.NotNull(Screen.WidgetById("updates.whatsnew"));
        Assert.Null(Screen.WidgetById("updates.download"));
    }
}
