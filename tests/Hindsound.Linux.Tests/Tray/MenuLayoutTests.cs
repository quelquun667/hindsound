using Hindsound.Linux.Tray;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Tray;

/// <summary>The menu of the app as com.canonical.dbusmenu lays it out. Pure: runs on every system.</summary>
public sealed class MenuLayoutTests
{
    private static IReadOnlyList<TrayMenuItem> Menu(bool paused = false, bool startsWithSession = true) =>
        TrayMenu.WindowlessItems(paused, startsWithSession, clipSeconds: 30, closingAfterClip: false);

    private static object? Property(MenuEntry entry, string name) =>
        entry.Properties.Where(property => property.Key == name).Select(property => property.Value).SingleOrDefault();

    [Fact]
    public void The_root_says_its_children_are_a_menu_and_has_id_0()
    {
        Assert.Equal(0, MenuLayout.Root.Id);
        Assert.Equal("submenu", Property(MenuLayout.Root, "children-display"));
    }

    [Fact]
    public void Each_line_becomes_a_child_with_its_own_id_in_the_order_of_the_menu()
    {
        var layout = new MenuLayout();

        layout.Replace(Menu());

        var entries = layout.Now.Entries;
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], entries.Select(entry => entry.Id));
        Assert.Equal(
            ["Save clip", null, "Pause capture", "Open clips folder", "Start with the session", null, "Quit"],
            entries.Select(entry => Property(entry, "label")));
        Assert.Equal("separator", Property(entries[1], "type"));
        Assert.Equal("separator", Property(entries[5], "type"));
        Assert.Null(Property(entries[0], "type"));
    }

    [Fact]
    public void Start_with_the_session_is_a_checkmark_that_shows_the_setting()
    {
        var on = MenuLayout.EntryOf(5, new(TrayCommand.StartWithSession, "Start with the session", Checked: true));
        var off = MenuLayout.EntryOf(5, new(TrayCommand.StartWithSession, "Start with the session", Checked: false));

        Assert.Equal("checkmark", Property(on, "toggle-type"));
        Assert.Equal(1, Property(on, "toggle-state"));
        Assert.Equal(0, Property(off, "toggle-state"));
    }

    [Fact]
    public void A_plain_line_has_no_toggle_and_says_nothing_of_what_is_already_the_default()
    {
        var entry = MenuLayout.EntryOf(1, new(TrayCommand.OpenFolder, "Open clips folder"));

        Assert.Equal(["label"], entry.Properties.Select(property => property.Key));
    }

    [Fact]
    public void A_grayed_line_says_so_and_a_click_on_it_does_nothing()
    {
        var layout = new MenuLayout();
        layout.Replace([new(TrayCommand.SaveDefault, "Save clip", Enabled: false), new(TrayCommand.Quit, "Quit")]);

        Assert.Equal(false, Property(layout.Now.Entries[0], "enabled"));
        Assert.Equal(TrayCommand.None, layout.Now.CommandOf(1));
        Assert.Equal(TrayCommand.Quit, layout.Now.CommandOf(2));
    }

    [Fact]
    public void An_underscore_of_a_label_is_doubled_so_it_marks_no_access_key()
    {
        var entry = MenuLayout.EntryOf(1, new(TrayCommand.UpdateNow, "Update to 1.0.0_beta..."));

        Assert.Equal("Update to 1.0.0__beta...", Property(entry, "label"));
    }

    [Fact]
    public void The_revision_goes_up_only_when_the_lines_change()
    {
        var layout = new MenuLayout();
        Assert.False(layout.HasMenu);
        Assert.Equal(0u, layout.Revision);

        Assert.True(layout.Replace(Menu()));
        Assert.Equal(1u, layout.Revision);
        Assert.False(layout.Replace(Menu()));
        Assert.Equal(1u, layout.Revision);
        Assert.True(layout.Replace(Menu(paused: true)));
        Assert.Equal(2u, layout.Revision);
        Assert.Equal("Resume capture", Property(layout.Now.Entries[2], "label"));
        Assert.True(layout.Replace(Menu(paused: true, startsWithSession: false)));
        Assert.Equal(3u, layout.Revision);
    }

    [Fact]
    public void A_click_tells_the_command_of_its_line_and_nothing_else_does()
    {
        var layout = new MenuLayout();
        layout.Replace(Menu());
        var menu = layout.Now;

        Assert.Equal(TrayCommand.SaveDefault, DBusMenuObject.CommandOfEvent(menu, 1, "clicked"));
        Assert.Equal(TrayCommand.TogglePause, DBusMenuObject.CommandOfEvent(menu, 3, "clicked"));
        Assert.Equal(TrayCommand.StartWithSession, DBusMenuObject.CommandOfEvent(menu, 5, "clicked"));
        Assert.Equal(TrayCommand.Quit, DBusMenuObject.CommandOfEvent(menu, 7, "clicked"));
        Assert.Equal(TrayCommand.None, DBusMenuObject.CommandOfEvent(menu, 3, "hovered"));
        Assert.Equal(TrayCommand.None, DBusMenuObject.CommandOfEvent(menu, 2, "clicked"));
        Assert.Equal(TrayCommand.None, DBusMenuObject.CommandOfEvent(menu, 0, "clicked"));
        Assert.Equal(TrayCommand.None, DBusMenuObject.CommandOfEvent(menu, 99, "clicked"));
    }

    [Fact]
    public void A_snapshot_keeps_its_lines_when_the_menu_changes_after_it_was_read()
    {
        var layout = new MenuLayout();
        layout.Replace(Menu());
        var before = layout.Now;

        layout.Replace([new(TrayCommand.Quit, "Quit")]);

        Assert.Equal(1u, before.Revision);
        Assert.Equal(TrayCommand.TogglePause, before.CommandOf(3));
        Assert.Equal(TrayCommand.None, layout.Now.CommandOf(3));
        Assert.Null(layout.Now.Find(3));
    }

    [Fact]
    public void The_properties_asked_for_are_the_only_ones_given_and_none_asked_gives_all()
    {
        var entry = MenuLayout.EntryOf(5, new(TrayCommand.StartWithSession, "Start with the session", Checked: true));

        Assert.Equal(["label", "toggle-type", "toggle-state"], entry.Only([]).Select(property => property.Key));
        Assert.Equal(["toggle-state"], entry.Only(["toggle-state", "icon-name"]).Select(property => property.Key));
    }
}
