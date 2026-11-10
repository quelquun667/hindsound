using Hindsound.Ui;

namespace Hindsound.Linux.Tray;

/// <summary>One line of the menu as com.canonical.dbusmenu shows it: its id, the command it stands for, and its properties.</summary>
/// <param name="Properties">The values are <see cref="string"/>, <see cref="bool"/> or <see cref="int"/>, the D-Bus types s, b and i.</param>
internal sealed record MenuEntry(int Id, TrayCommand Command, bool Enabled, IReadOnlyList<KeyValuePair<string, object>> Properties)
{
    /// <summary>The properties named in <paramref name="names"/>; all of them when none is named, as the protocol asks.</summary>
    public IReadOnlyList<KeyValuePair<string, object>> Only(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? Properties : Properties.Where(property => names.Contains(property.Key)).ToList();
}

/// <summary>The menu at one revision. Read whole, so a revision always goes with its own lines.</summary>
internal sealed record MenuSnapshot(uint Revision, IReadOnlyList<TrayMenuItem> Items, IReadOnlyList<MenuEntry> Entries)
{
    /// <summary>The root or one of its children; null for an id the menu does not have.</summary>
    public MenuEntry? Find(int id) => id == MenuLayout.RootId ? MenuLayout.Root : Entries.FirstOrDefault(entry => entry.Id == id);

    /// <summary>The command of a line; <see cref="TrayCommand.None"/> for the root, a separator, a grayed line or an id the menu does not have.</summary>
    public TrayCommand CommandOf(int id) => Find(id) is { Id: not MenuLayout.RootId, Enabled: true } entry ? entry.Command : TrayCommand.None;
}

/// <summary>
/// The menu of the app, as com.canonical.dbusmenu lays it out: a root of id 0 whose children are the lines of the menu, and a
/// revision that goes up each time the lines change. Pure: tested without a bus. Changed on the thread of the loop, read on
/// the thread of the connection: each change replaces the whole snapshot.
/// </summary>
internal sealed class MenuLayout
{
    public const int RootId = 0;

    private MenuSnapshot _now = new(0, [], []);

    /// <summary>The menu now. Revision 0 until the app has given one.</summary>
    public MenuSnapshot Now => Volatile.Read(ref _now);

    public uint Revision => Now.Revision;

    /// <summary>Whether the app has given a menu yet.</summary>
    public bool HasMenu => Now.Revision > 0;

    /// <summary>The root: it only says that its children are a menu.</summary>
    public static MenuEntry Root { get; } = new(RootId, TrayCommand.None, true, [new("children-display", "submenu")]);

    /// <summary>Takes the lines of the app. Whether they changed, which raised the revision.</summary>
    public bool Replace(IReadOnlyList<TrayMenuItem> items)
    {
        var current = Now;
        if (current.Revision > 0 && current.Items.SequenceEqual(items))
        {
            return false;
        }

        var entries = new List<MenuEntry>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            entries.Add(EntryOf(i + 1, items[i]));
        }

        Volatile.Write(ref _now, new MenuSnapshot(current.Revision + 1, [.. items], entries));
        return true;
    }

    /// <summary>
    /// The properties of one line, leaving out those at their default (type standard, enabled, visible), as the protocol
    /// allows. An underscore in a label marks the access key: a plain one is doubled.
    /// </summary>
    public static MenuEntry EntryOf(int id, TrayMenuItem item)
    {
        if (item.Command == TrayCommand.None)
        {
            return new(id, TrayCommand.None, false, [new("type", "separator")]);
        }

        List<KeyValuePair<string, object>> properties = [new("label", item.Text.Replace("_", "__", StringComparison.Ordinal))];
        if (!item.Enabled)
        {
            properties.Add(new("enabled", false));
        }

        if (item.Checked is { } on)
        {
            properties.Add(new("toggle-type", "checkmark"));
            properties.Add(new("toggle-state", on ? 1 : 0));
        }

        return new(id, item.Command, item.Enabled, properties);
    }
}
