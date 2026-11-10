using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.Core.Tests.Ui;

/// <summary>The menu of the icon where the app has no window: save, pause, the folder, starting with the session, quit.</summary>
public class WindowlessMenuTests
{
    [Fact]
    public void The_menu_offers_only_what_works_without_a_window_in_this_order()
    {
        var items = TrayMenu.WindowlessItems(paused: false, startsWithSession: false, clipSeconds: 30, closingAfterClip: false);

        Assert.Equal(
            [TrayCommand.SaveDefault, TrayCommand.None, TrayCommand.TogglePause, TrayCommand.OpenFolder, TrayCommand.StartWithSession, TrayCommand.None, TrayCommand.Quit],
            items.Select(item => item.Command));
        Assert.Equal(["Save clip", "", "Pause capture", "Open clips folder", "Start with the session", "", "Quit"], items.Select(item => item.Text));
        Assert.All(items, item => Assert.True(item.Enabled));
        Assert.Equal(TrayCommand.None, TrayMenu.DefaultOf(items));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Start_with_the_session_is_checked_as_the_setting_is_and_no_other_line_is_a_toggle(bool on)
    {
        var items = TrayMenu.WindowlessItems(paused: false, startsWithSession: on, clipSeconds: 30, closingAfterClip: false);

        Assert.Equal(on, items.Single(item => item.Command == TrayCommand.StartWithSession).Checked);
        Assert.All(items.Where(item => item.Command != TrayCommand.StartWithSession), item => Assert.Null(item.Checked));
    }

    [Fact]
    public void Paused_the_line_offers_to_resume()
    {
        var items = TrayMenu.WindowlessItemsFor(new AudioEngineStatus(CaptureActivity.Paused, true, false, false, null, null, true, TimeSpan.FromMinutes(5), 0), startsWithSession: true, clipSeconds: 30, closingAfterClip: false);

        Assert.Equal("Resume capture", items.Single(item => item.Command == TrayCommand.TogglePause).Text);
    }

    [Fact]
    public void While_a_clip_is_written_after_Quit_only_Quit_is_left()
    {
        var items = TrayMenu.WindowlessItems(paused: false, startsWithSession: true, clipSeconds: 30, closingAfterClip: true);

        Assert.Equal([new TrayMenuItem(TrayCommand.Quit, "Quit")], items);
    }

    [Fact]
    public void A_memory_shorter_than_a_clip_says_so_on_the_save_line_as_on_Windows()
    {
        var items = TrayMenu.WindowlessItems(paused: false, startsWithSession: true, clipSeconds: 30, closingAfterClip: false, buffered: TimeSpan.FromSeconds(12));
        var windows = TrayMenu.Items(paused: false, hasLastClip: false, closingAfterClip: false, buffered: TimeSpan.FromSeconds(12));

        var save = items.Single(item => item.Command == TrayCommand.SaveDefault);
        var save30 = windows.Single(item => item.Command == TrayCommand.Save30s);
        Assert.Equal("Save clip" + save30.Text["Save up to 30s".Length..], save.Text);
        Assert.Equal(save30.Enabled, save.Enabled);
    }

    [Fact]
    public void The_new_commands_come_after_the_old_ones_so_their_numbers_never_move()
    {
        Assert.Equal(13u, (uint)TrayCommand.SaveDefault);
        Assert.Equal(14u, (uint)TrayCommand.StartWithSession);
        Assert.Equal(12u, (uint)TrayCommand.Library);
    }
}
