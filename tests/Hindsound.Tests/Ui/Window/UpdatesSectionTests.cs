using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The Updates section of General and the card that confirms an update, with no window and no updater.</summary>
public sealed class UpdatesSectionTests : IDisposable
{
    private static readonly UpdateVersion Next = new(0, 2, 0);

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    internal static UpdatePanelView ReadyView(bool skipEnabled = true) =>
        new("Version 0.1.0", "Version 0.2.0 is ready.", Active: true, CheckEnabled: true, Next, UpdateEnabled: true, ShowWhatsNew: true, ShowSkip: true, ShowUpdate: true)
        {
            SkipEnabled = skipEnabled,
        };

    private static UpdatePanelView UpToDateView() =>
        new("Version 0.1.0", "Up to date. Checked today 14:02", Active: true, CheckEnabled: true, null, UpdateEnabled: false, ShowWhatsNew: false, ShowSkip: false, ShowUpdate: false);

    private FakePanel WithPanel(UpdatePanelView? view = null)
    {
        var panel = new FakePanel { View = view ?? ReadyView() };
        Screen.SetUpdates(panel);
        return panel;
    }

    private static string Painted(RecordingCanvas canvas) => string.Join(" ", canvas.Texts);

    private RecordingCanvas Paint()
    {
        var canvas = new RecordingCanvas();
        Screen.SetCanvas(canvas);
        Screen.Paint(canvas);
        return canvas;
    }

    private void OpenCard(FakePanel panel)
    {
        Screen.ShowUpdates();
        _rig.FocusOn("updates.now");
        _rig.Press(Vk.Enter);
        Assert.Equal(Section.UpdateCard, Screen.Section);
    }

    // ---------------------------------------------------------------- without a panel

    [Fact]
    public void Without_a_panel_general_is_drawn_call_for_call_as_before()
    {
        using var never = new ScreenRig();
        never.Screen.ShowSection(Section.General);
        var before = new RecordingCanvas();
        never.Screen.SetCanvas(before);
        never.Screen.Paint(before);

        Screen.SetUpdates(null);
        Screen.ShowSection(Section.General);
        var withNull = Paint();

        Screen.SetUpdates(new FakePanel { View = ReadyView() });
        Screen.SetUpdates(null);
        var afterRemoval = Paint();

        Assert.Equal(before.Calls, withNull.Calls);
        Assert.Equal(before.Calls, afterRemoval.Calls);
        Assert.Equal(never.Screen.StopIds(), Screen.StopIds());
        Assert.Equal(never.Screen.MaxScroll, Screen.MaxScroll);
        Assert.DoesNotContain("Updates", before.Texts);
    }

    [Fact]
    public void Without_a_panel_the_card_does_not_open()
    {
        Screen.ShowSection(Section.General);
        Screen.ShowUpdateCard();

        Assert.Equal(Section.General, Screen.Section);
    }

    // ---------------------------------------------------------------- the section

    [Fact]
    public void Section_has_heading_version_list_status_buttons_and_the_github_note()
    {
        WithPanel();
        Screen.ShowUpdates();
        var painted = Painted(Paint());

        Assert.Contains("Updates", painted);
        Assert.Contains("Version 0.1.0", painted);
        Assert.Contains("Version 0.2.0 is ready.", painted);
        Assert.Contains(SettingsScreen.GitHubNote, painted);
        var stops = Screen.StopIds().ToList();
        var test = stops.IndexOf("general.test");
        Assert.Equal(["general.updates", "updates.check", "updates.now", "updates.whatsnew", "updates.skip"], stops.Skip(test + 1).Take(5));
    }

    [Fact]
    public void Version_is_drawn_right_aligned_on_the_heading_line()
    {
        WithPanel();
        Screen.ShowUpdates();
        var canvas = Paint();

        var heading = canvas.TextAt("Updates");
        var version = canvas.TextAt("Version 0.1.0");
        Assert.Equal(728, version.X + canvas.Measure("Version 0.1.0", Face.Body, 13), 1);
        Assert.InRange(version.Y, heading.Y, heading.Y + 22);
    }

    [Fact]
    public void Buttons_sit_on_one_line_inside_the_page_without_overlapping()
    {
        WithPanel();
        Screen.ShowSection(Section.General);
        var boxes = new[] { "updates.check", "updates.now", "updates.whatsnew", "updates.skip" }.Select(id => Screen.WidgetById(id)!.Bounds).ToList();

        Assert.All(boxes, b => Assert.Equal(boxes[0].Y, b.Y));
        Assert.True(boxes[0].X >= 200);
        Assert.True(boxes[^1].Right <= 728);
        for (var i = 1; i < boxes.Count; i++)
        {
            Assert.True(boxes[i].X >= boxes[i - 1].Right + 8);
        }
    }

    [Fact]
    public void Github_note_and_legal_note_do_not_overlap()
    {
        WithPanel();
        Screen.ShowUpdates();
        var canvas = Paint();

        var github = canvas.TextAt(canvas.Texts.First(t => SettingsScreen.GitHubNote.StartsWith(t, StringComparison.Ordinal)));
        var legal = canvas.TextAt(canvas.Texts.First(t => SettingsScreen.LegalNote.StartsWith(t, StringComparison.Ordinal)));
        var githubLines = canvas.Texts.Count(t => SettingsScreen.GitHubNote.Contains(t, StringComparison.Ordinal));
        Assert.True(legal.Y >= github.Y + githubLines * canvas.LineHeight(Face.Body, 13));
    }

    [Fact]
    public void Copy_that_does_not_update_shows_the_version_and_one_line_only()
    {
        WithPanel(UpdatePanel.Inactive(new UpdateVersion(0, 1, 0)));
        Screen.ShowUpdates();
        var painted = Painted(Paint());

        Assert.Contains("Version 0.1.0", painted);
        Assert.Contains(UpdatePanel.NotInstalled, painted);
        Assert.DoesNotContain("GitHub", painted);
        Assert.DoesNotContain(Screen.StopIds(), id => id.StartsWith("updates.", StringComparison.Ordinal) || id == "general.updates");
    }

    [Fact]
    public void Without_a_known_version_only_check_now_shows()
    {
        WithPanel(UpToDateView());
        Screen.ShowSection(Section.General);

        Assert.NotNull(Screen.WidgetById("updates.check"));
        Assert.Null(Screen.WidgetById("updates.now"));
        Assert.Null(Screen.WidgetById("updates.whatsnew"));
        Assert.Null(Screen.WidgetById("updates.skip"));
    }

    [Fact]
    public void Greyed_buttons_are_shown_but_skipped_by_tab()
    {
        WithPanel(ReadyView(skipEnabled: false) with { CheckEnabled = false, UpdateEnabled = false });
        Screen.ShowSection(Section.General);

        foreach (var id in new[] { "updates.check", "updates.now", "updates.skip" })
        {
            Assert.False(Screen.WidgetById(id)!.Enabled);
            Assert.DoesNotContain(id, Screen.StopIds());
        }

        Assert.True(Screen.WidgetById("updates.whatsnew")!.Enabled);
    }

    [Fact]
    public void Buttons_call_the_panel()
    {
        var panel = WithPanel();
        Screen.ShowSection(Section.General);

        _rig.FocusOn("updates.check");
        _rig.Press(Vk.Enter);
        _rig.FocusOn("updates.whatsnew");
        _rig.Press(Vk.Space);
        _rig.FocusOn("updates.skip");
        _rig.Press(Vk.Enter);

        Assert.Equal(1, panel.Checks);
        Assert.Equal(1, panel.WhatsNews);
        Assert.Equal(1, panel.Skips);
        Assert.Empty(panel.Confirms);
    }

    [Fact]
    public void List_shows_the_three_modes_and_writes_the_one_picked()
    {
        WithPanel();
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.updates");
        _rig.Press(Vk.Space);
        Assert.True(Screen.PopupOpen);
        var painted = Painted(Paint());
        Assert.Contains("Install automatically", painted);
        Assert.Contains("Ask me", painted);
        Assert.Contains("Off", painted);

        _rig.Press(Vk.Down, Vk.Enter);

        Assert.Equal(UpdateSettings.Ask, _rig.Settings.General.Updates.Mode);
        Assert.Contains(_rig.Backend.Changes, c => (c & SettingsChange.Updates) != 0);
    }

    [Fact]
    public void Refresh_keeps_the_focus_on_the_same_button()
    {
        var panel = WithPanel();
        Screen.ShowSection(Section.General);
        _rig.FocusOn("updates.skip");

        panel.View = panel.View with { StatusLine = "Checking...", CheckEnabled = false };
        Screen.RefreshUpdates();

        Assert.Equal("updates.skip", Screen.FocusId);
        Assert.Contains("Checking...", Painted(Paint()));
    }

    [Fact]
    public void Refresh_that_removes_the_focused_button_sends_the_focus_to_the_side_bar()
    {
        var panel = WithPanel();
        Screen.ShowSection(Section.General);
        _rig.FocusOn("updates.skip");

        panel.View = UpToDateView();
        Screen.RefreshUpdates();

        Assert.Equal("nav", Screen.FocusId);
    }

    [Fact]
    public void Show_updates_opens_general_scrolled_to_the_section()
    {
        WithPanel();
        Screen.ShowUpdates();

        Assert.Equal(Section.General, Screen.Section);
        Assert.True(Screen.Scroll > 0);
        var check = Screen.WidgetById("updates.check")!.Bounds;
        Assert.InRange(check.Y - Screen.Scroll, SettingsScreen.BandHeight, SettingsScreen.ViewBottom - check.H);
    }

    // ---------------------------------------------------------------- the card

    [Fact]
    public void Update_now_opens_the_card_without_side_bar_foot_or_scroll()
    {
        var panel = WithPanel();
        OpenCard(panel);

        var stops = Screen.StopIds();
        Assert.DoesNotContain("nav", stops);
        Assert.DoesNotContain("foot.copy", stops);
        Assert.Equal(0, Screen.MaxScroll);
        Assert.Empty(panel.Confirms);
    }

    [Fact]
    public void Card_says_the_version_and_what_is_lost()
    {
        var panel = WithPanel();
        panel.Buffer = 300;
        Screen.ShowUpdateCard();
        var painted = Painted(Paint());

        Assert.Contains("Update to 0.2.0", painted);
        Assert.Contains("Updating restarts Hindsound. The last 5:00 in memory will be lost.", painted);
        Assert.Equal(["card.save", "card.update", "card.cancel"], Screen.StopIds().Where(id => id.StartsWith("card.", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(90, "1:30")]
    [InlineData(600, "10:00")]
    public void Card_text_follows_the_buffer_setting(int seconds, string shown)
    {
        var panel = WithPanel();
        panel.Buffer = seconds;
        Screen.ShowUpdateCard();

        Assert.Contains($"The last {shown} in memory will be lost.", Painted(Paint()));
    }

    [Fact]
    public void Card_buttons_confirm_with_or_without_a_clip_first()
    {
        var panel = WithPanel();
        Screen.ShowUpdateCard();

        _rig.FocusOn("card.update");
        _rig.Press(Vk.Enter);
        _rig.FocusOn("card.save");
        _rig.Press(Vk.Enter);

        Assert.Equal([false, true], panel.Confirms);
        Assert.Equal(Section.UpdateCard, Screen.Section);
    }

    [Fact]
    public void Update_is_the_accented_button()
    {
        WithPanel();
        Screen.ShowUpdateCard();

        Assert.True(((Button)Screen.WidgetById("card.update")!).Accented);
        Assert.False(((Button)Screen.WidgetById("card.save")!).Accented);
        Assert.False(((Button)Screen.WidgetById("card.cancel")!).Accented);
    }

    [Fact]
    public void Clip_being_saved_greys_save_and_update()
    {
        var panel = WithPanel();
        panel.Busy = true;
        Screen.ShowUpdateCard();

        Assert.False(Screen.WidgetById("card.save")!.Enabled);
        Assert.False(Screen.WidgetById("card.update")!.Enabled);
        Assert.True(Screen.WidgetById("card.cancel")!.Enabled);
        Assert.Contains(SettingsScreen.ClipBusyNote, Painted(Paint()));
        Assert.Equal("card.cancel", Screen.FocusId);

        panel.Busy = false;
        Screen.RefreshUpdates();

        Assert.True(Screen.WidgetById("card.update")!.Enabled);
        Assert.Equal("card.cancel", Screen.FocusId);
        Assert.DoesNotContain(SettingsScreen.ClipBusyNote, Painted(Paint()));
    }

    [Fact]
    public void Download_under_way_leaves_only_cancel()
    {
        var panel = WithPanel();
        Screen.ShowUpdateCard();
        _rig.FocusOn("card.update");
        panel.View = panel.View with { CardBusy = true, CardLine = "Downloading 0.2.0..." };
        Screen.RefreshUpdates();

        Assert.False(Screen.WidgetById("card.save")!.Enabled);
        Assert.False(Screen.WidgetById("card.update")!.Enabled);
        Assert.Equal(["card.cancel"], Screen.StopIds().Where(id => id.StartsWith("card.", StringComparison.Ordinal)));
        Assert.Equal("card.cancel", Screen.FocusId);
        Assert.Contains("Downloading 0.2.0...", Painted(Paint()));
    }

    [Fact]
    public void Card_line_from_the_flow_is_shown()
    {
        var panel = WithPanel();
        panel.View = panel.View with { CardLine = "Not now: a game or a clip is running." };
        Screen.ShowUpdateCard();

        Assert.Contains("Not now: a game or a clip is running.", Painted(Paint()));
        Assert.True(Screen.WidgetById("card.update")!.Enabled);
    }

    [Fact]
    public void Cancel_closes_the_card_and_shows_the_section()
    {
        var panel = WithPanel();
        Screen.ShowUpdateCard();
        _rig.FocusOn("card.cancel");
        _rig.Press(Vk.Enter);

        Assert.Equal(1, panel.Cancels);
        Assert.Equal(Section.General, Screen.Section);
        Assert.True(Screen.Scroll > 0);
        Assert.Empty(panel.Confirms);
    }

    [Fact]
    public void Escape_does_what_cancel_does()
    {
        var panel = WithPanel();
        Screen.ShowUpdateCard();
        _rig.Press(Vk.Escape);

        Assert.Equal(1, panel.Cancels);
        Assert.Equal(Section.General, Screen.Section);
        Assert.True(Screen.Scroll > 0);
    }

    [Fact]
    public void Escape_outside_the_card_cancels_nothing()
    {
        var panel = WithPanel();
        Screen.ShowUpdates();
        _rig.FocusOn("updates.check");
        _rig.Press(Vk.Escape);

        Assert.Equal(0, panel.Cancels);
        Assert.Equal(Section.General, Screen.Section);
    }

    [Fact]
    public void Card_is_not_in_the_side_bar()
    {
        WithPanel();
        Screen.ShowSection(Section.General);
        _rig.FocusOn("nav");
        _rig.Press(Vk.End);
        Assert.Equal(Section.General, Screen.Section);
    }

    // ---------------------------------------------------------------- the card at the keyboard

    [Fact]
    public void Focus_lands_on_the_first_button_of_the_card()
    {
        WithPanel();
        Screen.ShowSection(Section.Hotkeys);
        Screen.ShowUpdateCard();

        Assert.Equal("card.save", Screen.FocusId);
    }

    [Fact]
    public void Tab_stays_in_the_card_both_ways()
    {
        WithPanel();
        Screen.ShowUpdateCard();
        var forward = new List<string?>();
        for (var i = 0; i < 6; i++)
        {
            _rig.Press(Vk.Tab);
            forward.Add(Screen.FocusId);
        }

        Assert.Equal(["card.update", "card.cancel", "card.save", "card.update", "card.cancel", "card.save"], forward);

        Screen.Key(Vk.Tab, Hindsound.Input.HotkeyGesture.ModShift);
        Assert.Equal("card.cancel", Screen.FocusId);
    }

    [Fact]
    public void Closing_the_card_gives_the_focus_back_to_update_now()
    {
        var panel = WithPanel();
        OpenCard(panel);
        _rig.Press(Vk.Escape);

        Assert.Equal("updates.now", Screen.FocusId);
        var now = Screen.WidgetById("updates.now")!.Bounds;
        Assert.InRange(now.Y - Screen.Scroll, SettingsScreen.BandHeight, SettingsScreen.ViewBottom - now.H);
    }

    [Fact]
    public void Closing_with_cancel_also_gives_the_focus_back()
    {
        var panel = WithPanel();
        OpenCard(panel);
        _rig.FocusOn("card.cancel");
        _rig.Press(Vk.Space);

        Assert.Equal("updates.now", Screen.FocusId);
    }

    [Fact]
    public void Card_opened_from_another_page_closes_onto_the_side_bar()
    {
        WithPanel();
        Screen.ShowSection(Section.Export);
        Screen.ShowUpdateCard();
        _rig.Press(Vk.Escape);

        Assert.Equal(Section.General, Screen.Section);
        Assert.Equal("nav", Screen.FocusId);
    }

    [Fact]
    public void Card_buttons_answer_the_mouse()
    {
        var panel = WithPanel();
        Screen.ShowUpdateCard();
        var update = Screen.WidgetById("card.update")!.Bounds;
        Screen.MouseDown(update.MidX, update.MidY);
        Screen.MouseUp(update.MidX, update.MidY);

        Assert.Equal([false], panel.Confirms);
    }

    [Fact]
    public void Removing_the_panel_closes_the_card()
    {
        WithPanel();
        Screen.ShowUpdateCard();
        Screen.SetUpdates(null);

        Assert.Equal(Section.General, Screen.Section);
    }
}

/// <summary>An updater as the window sees it: a view the test writes, and counts of what was asked.</summary>
internal sealed class FakePanel : IUpdatesPanel
{
    public UpdatePanelView View { get; set; } = UpdatePanel.Inactive(new UpdateVersion(0, 1, 0));

    public bool Busy { get; set; }

    public int Buffer { get; set; } = 300;

    public int Checks { get; private set; }

    public int WhatsNews { get; private set; }

    public int Skips { get; private set; }

    public int Cancels { get; private set; }

    public List<bool> Confirms { get; } = [];

    public bool ClipBusy => Busy;

    public int BufferSeconds => Buffer;

    public void CheckNow() => Checks++;

    public void ConfirmUpdate(bool saveFirst) => Confirms.Add(saveFirst);

    public void CancelUpdate() => Cancels++;

    public void WhatsNew() => WhatsNews++;

    public void SkipThisVersion() => Skips++;
}

/// <summary>Every call, in order, so two pages can be compared call for call; and where each text went.</summary>
internal sealed class RecordingCanvas : ICanvas
{
    public List<string> Calls { get; } = [];

    public List<string> Texts { get; } = [];

    /// <summary>Where each text was first drawn.</summary>
    public Dictionary<string, (float X, float Y)> Places { get; } = [];

    public (float X, float Y) TextAt(string text) => Places[text];

    public void SetOrigin(float dy) => Calls.Add($"origin {dy}");

    public void FillRect(Box box, uint rgb) => Calls.Add($"rect {box} {rgb:X}");

    public void FillRound(Box box, float radius, uint rgb) => Calls.Add($"round {box} {radius} {rgb:X}");

    public void Frame(Box box, float radius, uint fill, uint border) => Calls.Add($"frame {box} {radius} {fill:X} {border:X}");

    public void Ring(Box box, float radius, float thickness, uint rgb) => Calls.Add($"ring {box} {radius} {thickness} {rgb:X}");

    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Calls.Add($"stroke {ax} {ay} {bx} {by} {width} {rgb:X}");

    public void Glow(Box box, float sigma, float opacity, uint rgb) => Calls.Add($"glow {box} {sigma} {opacity} {rgb:X}");

    public void Text(string text, Face face, float size, float x, float top, uint rgb)
    {
        Calls.Add($"text {text} {face} {size} {x} {top} {rgb:X}");
        Texts.Add(text);
        Places.TryAdd(text, (x, top));
    }

    public float Measure(string text, Face face, float size) => text.Length * size * 0.5f;

    public float LineHeight(Face face, float size) => size * 1.3f;

    public void WarningIcon(float x, float y, float size, uint rgb) => Calls.Add($"warning {x} {y} {size} {rgb:X}");

    public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => Calls.Add($"notification {content.Title} {x} {y} {accent:X} {mirrored}");
}
