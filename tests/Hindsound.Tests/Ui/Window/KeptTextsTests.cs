using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The window and the menu say what the memory holds, and offer no clip when it holds nothing.</summary>
public class KeptTextsTests
{
    private static AudioEngineStatus Holding(double seconds, CaptureActivity activity = CaptureActivity.Capturing) =>
        ScreenRig.Capturing(activity: activity) with { Buffered = TimeSpan.FromSeconds(seconds) };

    // ---------------------------------------------------------------- the band

    [Theory]
    [InlineData(0, 300, "0:00 / 5:00")]
    [InlineData(12, 300, "0:12 / 5:00")]
    [InlineData(298, 300, "4:58 / 5:00")]
    [InlineData(299, 300, "5:00")]
    [InlineData(300, 300, "5:00")]
    [InlineData(1790, 1800, "29:50 / 30:00")]
    public void The_duration_cell_fills_up_then_says_the_setting(double seconds, int cap, string cell)
    {
        Assert.Equal(cell, StatusText.From(Holding(seconds), cap).Duration);
    }

    [Fact]
    public void A_pause_keeps_both_figures_while_the_memory_is_not_full()
    {
        Assert.Equal("2:10 / 5:00", StatusText.From(Holding(130, CaptureActivity.Paused), 300).Duration);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.99, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    public void Save_is_grey_under_a_second(double seconds, bool enabled)
    {
        Assert.Equal(enabled, StatusText.From(Holding(seconds), 300).SaveEnabled);
        Assert.Equal(enabled, StatusText.From(Holding(seconds, CaptureActivity.Paused), 300).SaveEnabled);
    }

    [Fact]
    public void Before_any_status_save_stays_there_and_answers_for_itself()
    {
        Assert.True(StatusText.From(null).SaveEnabled);
    }

    [Theory]
    [InlineData(1, "Save up to 1s")]
    [InlineData(45, "Save up to 45s")]
    [InlineData(120, "Save up to 2 min")]
    public void The_big_button_says_up_to_at_every_length(int seconds, string label)
    {
        Assert.Equal(label, StatusText.SaveLabel(seconds));
    }

    [Fact]
    public void The_big_button_keeps_its_words_whatever_the_memory_holds()
    {
        var settings = new AppSettings().Normalize();
        settings.ClipSeconds = 30;
        using var rig = new ScreenRig(settings);

        rig.Screen.UpdateStatus(Holding(0.5));
        var before = new FakeCanvas();
        rig.Screen.Paint(before);
        Assert.Contains("Save up to 30s", before.Drawn);
        Assert.Contains("0:01 / 5:00", before.Drawn);

        rig.Screen.UpdateStatus(Holding(12));
        var after = new FakeCanvas();
        rig.Screen.Paint(after);
        Assert.Contains("Save up to 30s", after.Drawn);
        Assert.Contains("0:12 / 5:00", after.Drawn);
    }

    [Fact]
    public void A_grey_save_takes_neither_the_keyboard_nor_the_mouse_until_there_is_something()
    {
        using var rig = new ScreenRig();

        rig.Screen.UpdateStatus(Holding(0));
        Assert.DoesNotContain("band.save", rig.Screen.StopIds());
        Assert.False(rig.Screen.WidgetById("band.save")!.Enabled);

        rig.Screen.UpdateStatus(Holding(1));
        Assert.Contains("band.save", rig.Screen.StopIds());
        rig.FocusOn("band.save");
        rig.Press(Vk.Enter);
        Assert.Equal(1, rig.Backend.Saves);
    }

    [Fact]
    public void A_grey_save_is_drawn_grey_with_its_words_in_their_place()
    {
        var settings = new AppSettings().Normalize();
        settings.ClipSeconds = 30;
        using var rig = new ScreenRig(settings);

        rig.Screen.UpdateStatus(Holding(0));
        var grey = new FakeCanvas();
        rig.Screen.Paint(grey);
        Assert.Equal(Theme.Faded, grey.TextColors["Save up to 30s"]);
        Assert.Equal(Theme.Faded, grey.TextColors[settings.Hotkeys.SaveDefault!]);

        rig.Screen.UpdateStatus(Holding(1));
        var lit = new FakeCanvas();
        rig.Screen.Paint(lit);
        Assert.NotEqual(Theme.Faded, lit.TextColors["Save up to 30s"]);
    }

    // ---------------------------------------------------------------- the menu

    private static IReadOnlyList<TrayMenuItem> MenuWith(double? seconds) =>
        TrayMenu.Items(false, true, false, buffered: seconds is { } held ? TimeSpan.FromSeconds(held) : null);

    private static string[] SaveTexts(IReadOnlyList<TrayMenuItem> items) =>
        items.Where(item => item.Command is TrayCommand.Save30s or TrayCommand.Save15s or TrayCommand.Save1m or TrayCommand.Save5m).Select(item => item.Text).ToArray();

    [Fact]
    public void An_entry_longer_than_what_is_kept_says_so()
    {
        Assert.Equal(
            ["Save up to 30s (only 0:12)", "Save up to 15s (only 0:12)", "Save up to 1 min (only 0:12)", "Save up to 5 min (only 0:12)"],
            SaveTexts(MenuWith(12)));
    }

    [Fact]
    public void Only_the_entries_longer_than_what_is_kept_say_so()
    {
        Assert.Equal(["Save up to 30s", "Save up to 15s", "Save up to 1 min (only 0:45)", "Save up to 5 min (only 0:45)"], SaveTexts(MenuWith(45)));
        Assert.Equal(["Save up to 30s", "Save up to 15s", "Save up to 1 min", "Save up to 5 min (only 2:10)"], SaveTexts(MenuWith(130)));
    }

    [Fact]
    public void An_entry_exactly_as_long_as_what_is_kept_has_no_suffix()
    {
        Assert.Equal(["Save up to 30s", "Save up to 15s", "Save up to 1 min", "Save up to 5 min"], SaveTexts(MenuWith(300)));
        Assert.Contains("Save up to 30s", SaveTexts(MenuWith(30)));
        Assert.Contains("Save up to 15s", SaveTexts(MenuWith(15)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.99)]
    public void Under_a_second_every_save_entry_is_grey_and_says_nothing_more(double seconds)
    {
        var items = MenuWith(seconds);

        Assert.Equal(["Save up to 30s", "Save up to 15s", "Save up to 1 min", "Save up to 5 min"], SaveTexts(items));
        Assert.All(items.Where(item => SaveTexts([item]).Length == 1), item => Assert.False(item.Enabled));
        Assert.True(items.Single(item => item.Command == TrayCommand.TogglePause).Enabled);
        Assert.True(items.Single(item => item.Command == TrayCommand.Quit).Enabled);
    }

    [Fact]
    public void One_second_of_memory_gives_entries_that_work()
    {
        var items = MenuWith(1);

        Assert.All(items.Where(item => SaveTexts([item]).Length == 1), item => Assert.True(item.Enabled));
        Assert.Contains("Save up to 30s (only 0:01)", SaveTexts(items));
    }

    [Fact]
    public void Before_any_status_the_menu_promises_nothing_more_and_greys_nothing()
    {
        var items = MenuWith(null);

        Assert.Equal(["Save up to 30s", "Save up to 15s", "Save up to 1 min", "Save up to 5 min"], SaveTexts(items));
        Assert.All(items.Where(item => SaveTexts([item]).Length == 1), item => Assert.True(item.Enabled));
    }

    // ---------------------------------------------------------------- the hotkeys page

    [Fact]
    public void The_hotkeys_page_says_up_to()
    {
        Assert.Equal(
            ["Save clip", "Save up to 15s", "Save up to 1 min", "Save up to 5 min", "Pause or resume", "Copy last clip"],
            SettingsEditor.HotkeyRows.Select(row => row.Label));
    }

    // ---------------------------------------------------------------- the cards

    [Theory]
    [InlineData(30, "Saves up to the last 30 seconds, from anywhere, even in a game.")]
    [InlineData(15, "Saves up to the last 15 seconds, from anywhere, even in a game.")]
    [InlineData(60, "Saves up to the last 1 minute, from anywhere, even in a game.")]
    [InlineData(300, "Saves up to the last 5 minutes, from anywhere, even in a game.")]
    [InlineData(90, "Saves up to the last 1:30, from anywhere, even in a game.")]
    public void The_first_launch_card_follows_the_clip_length(int seconds, string note)
    {
        Assert.Equal(note, StatusText.FirstRunHotkeyNote(seconds));

        var settings = new AppSettings().Normalize();
        settings.ClipSeconds = seconds;
        using var rig = new ScreenRig(settings, firstRun: true);
        var canvas = new FakeCanvas();
        rig.Screen.Paint(canvas);

        Assert.Contains(note, canvas.Drawn);
    }

    [Fact]
    public void The_last_step_of_the_first_launch_says_to_wait()
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.FocusOn("run.next");
        rig.Press(Vk.Enter, Vk.Enter);
        var canvas = new FakeCanvas();
        rig.Screen.Paint(canvas);

        Assert.Contains("Wait a few seconds.", canvas.Drawn);
        Assert.Contains("Say something, then press your hotkey.", canvas.Drawn);
    }

    [Theory]
    [InlineData(130, "Updating restarts Hindsound. The last 2:10 in memory will be lost.")]
    [InlineData(5, "Updating restarts Hindsound. The last 0:05 in memory will be lost.")]
    [InlineData(299, "Updating restarts Hindsound. The last 5:00 in memory will be lost.")]
    [InlineData(300, "Updating restarts Hindsound. The last 5:00 in memory will be lost.")]
    [InlineData(4, "Updating restarts Hindsound.")]
    [InlineData(0, "Updating restarts Hindsound.")]
    public void The_update_card_warns_about_what_is_really_there(double seconds, string body)
    {
        Assert.Equal(body, StatusText.UpdateCardBody(Holding(seconds), 300));
    }
}
