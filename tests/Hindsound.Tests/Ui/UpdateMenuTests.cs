using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>The tray menu offers a ready version just above Quit, and only then. The numbers of the commands never move: the --offscreen tests post them.</summary>
public class UpdateMenuTests
{
    [Fact]
    public void Without_a_version_the_menu_is_as_it_was()
    {
        var items = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: false);

        Assert.Equal(11, items.Count);
        Assert.DoesNotContain(items, item => item.Command == TrayCommand.UpdateNow);
        Assert.Equal(new TrayMenuItem(TrayCommand.None), items[^2]);
        Assert.Equal(new TrayMenuItem(TrayCommand.Quit, "Quit"), items[^1]);
        Assert.Equal(items, TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: false, updateText: null));
    }

    [Fact]
    public void The_version_comes_just_above_quit_under_the_last_separator()
    {
        var items = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: false, updateText: "Update to 0.2.0...");

        Assert.Equal(12, items.Count);
        Assert.Equal(new TrayMenuItem(TrayCommand.Quit, "Quit"), items[^1]);
        Assert.Equal(new TrayMenuItem(TrayCommand.UpdateNow, "Update to 0.2.0..."), items[^2]);
        Assert.True(items[^2].Enabled);
        Assert.Equal(new TrayMenuItem(TrayCommand.None), items[^3]);
        Assert.Equal(new TrayMenuItem(TrayCommand.Settings, "Settings"), items[^4]);
        Assert.Single(items, item => item.Command == TrayCommand.UpdateNow);
    }

    [Fact]
    public void The_rest_of_the_menu_does_not_change_with_a_version()
    {
        var without = TrayMenu.Items(paused: true, hasLastClip: false, closingAfterClip: false);
        var with = TrayMenu.Items(paused: true, hasLastClip: false, closingAfterClip: false, updateText: "Update to 0.2.0...");

        Assert.Equal(without, with.Where(item => item.Command != TrayCommand.UpdateNow));
    }

    [Fact]
    public void Closing_after_a_clip_leaves_quit_alone()
    {
        var items = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: true, updateText: "Update to 0.2.0...");

        Assert.Equal([new TrayMenuItem(TrayCommand.Quit, "Quit")], items);
    }

    [Fact]
    public void The_numbers_of_the_commands_never_move()
    {
        Assert.Equal(0u, (uint)TrayCommand.None);
        Assert.Equal(1u, (uint)TrayCommand.Save30s);
        Assert.Equal(5u, (uint)TrayCommand.CopyLast);
        Assert.Equal(8u, (uint)TrayCommand.Settings);
        Assert.Equal(9u, (uint)TrayCommand.Quit);
        Assert.Equal(10u, (uint)TrayCommand.UpdateNow);
    }
}
