using Hindsound.Linux.DBus;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tray;

/// <summary>
/// The menu of the icon on the session bus: com.canonical.dbusmenu at /MenuBar. The host reads the lines with GetLayout,
/// says the menu is about to open with AboutToShow, and tells a click with Event. Every call runs on the thread of the
/// connection.
/// </summary>
internal sealed class DBusMenuObject : BusObject
{
    public const string ObjectPath = "/MenuBar";
    public const string Interface = "com.canonical.dbusmenu";

    /// <summary>The version of the protocol this menu speaks.</summary>
    public const uint Version = 3;

    private const string InvalidArgs = "org.freedesktop.DBus.Error.InvalidArgs";

    private static readonly string IntrospectionXml = BusProperties.Xml(
        "<interface name=\"" + Interface + "\">"
        + "<method name=\"GetLayout\"><arg type=\"i\" direction=\"in\"/><arg type=\"i\" direction=\"in\"/><arg type=\"as\" direction=\"in\"/><arg type=\"u\" direction=\"out\"/><arg type=\"(ia{sv}av)\" direction=\"out\"/></method>"
        + "<method name=\"GetGroupProperties\"><arg type=\"ai\" direction=\"in\"/><arg type=\"as\" direction=\"in\"/><arg type=\"a(ia{sv})\" direction=\"out\"/></method>"
        + "<method name=\"GetProperty\"><arg type=\"i\" direction=\"in\"/><arg type=\"s\" direction=\"in\"/><arg type=\"v\" direction=\"out\"/></method>"
        + "<method name=\"Event\"><arg type=\"i\" direction=\"in\"/><arg type=\"s\" direction=\"in\"/><arg type=\"v\" direction=\"in\"/><arg type=\"u\" direction=\"in\"/></method>"
        + "<method name=\"EventGroup\"><arg type=\"a(isvu)\" direction=\"in\"/><arg type=\"ai\" direction=\"out\"/></method>"
        + "<method name=\"AboutToShow\"><arg type=\"i\" direction=\"in\"/><arg type=\"b\" direction=\"out\"/></method>"
        + "<method name=\"AboutToShowGroup\"><arg type=\"ai\" direction=\"in\"/><arg type=\"ai\" direction=\"out\"/><arg type=\"ai\" direction=\"out\"/></method>"
        + "<signal name=\"ItemsPropertiesUpdated\"><arg type=\"a(ia{sv})\"/><arg type=\"a(ias)\"/></signal>"
        + "<signal name=\"LayoutUpdated\"><arg type=\"u\"/><arg type=\"i\"/></signal>"
        + "<signal name=\"ItemActivationRequested\"><arg type=\"i\"/><arg type=\"u\"/></signal>"
        + "<property name=\"Version\" type=\"u\" access=\"read\"/><property name=\"TextDirection\" type=\"s\" access=\"read\"/>"
        + "<property name=\"Status\" type=\"s\" access=\"read\"/><property name=\"IconThemePath\" type=\"as\" access=\"read\"/>"
        + "</interface>");

    private static readonly IReadOnlyList<KeyValuePair<string, object>> MenuProperties =
    [
        new("Version", Version),
        new("TextDirection", "ltr"),
        new("Status", "normal"),
        new("IconThemePath", Array.Empty<string>()),
    ];

    private readonly MenuLayout _layout;
    private readonly Func<bool> _refresh;
    private readonly Action<TrayCommand> _clicked;

    /// <param name="layout">The lines of the menu.</param>
    /// <param name="refresh">Asks the app for its menu as it is now and waits a little for it. Whether the lines changed.</param>
    /// <param name="clicked">A line was clicked; never <see cref="TrayCommand.None"/>.</param>
    public DBusMenuObject(MenuLayout layout, Func<bool> refresh, Action<TrayCommand> clicked)
        : base(ObjectPath)
    {
        _layout = layout;
        _refresh = refresh;
        _clicked = clicked;
        On(Interface, "GetLayout", GetLayout);
        On(Interface, "GetGroupProperties", GetGroupProperties);
        On(Interface, "GetProperty", GetProperty);
        On(Interface, "Event", Event);
        On(Interface, "EventGroup", EventGroup);
        On(Interface, "AboutToShow", AboutToShow);
        On(Interface, "AboutToShowGroup", AboutToShowGroup);
        BusProperties.Serve(On, Interface, () => MenuProperties, IntrospectionXml);
    }

    /// <summary>What a click on a line asks for: the event "clicked" on a line that has a command; anything else is nothing.</summary>
    public static TrayCommand CommandOfEvent(MenuSnapshot menu, int id, string eventId) =>
        eventId == "clicked" ? menu.CommandOf(id) : TrayCommand.None;

    private void GetLayout(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var parent = reader.ReadInt32();
        var depth = reader.ReadInt32();
        var names = reader.ReadArrayOfString();

        // A host may read the layout before it ever says the menu opens: the app is asked once for its menu.
        if (!_layout.HasMenu)
        {
            _refresh();
        }

        var menu = _layout.Now;
        if (menu.Find(parent) is not { } node)
        {
            context.ReplyError(InvalidArgs, "no such menu item");
            return;
        }

        IReadOnlyList<MenuEntry> children = node.Id == MenuLayout.RootId && depth != 0 ? menu.Entries : [];
        TrayWire.Reply(context, "u" + TrayWire.NodeSignature, (ref MessageWriter writer) =>
        {
            writer.WriteUInt32(menu.Revision);
            TrayWire.WriteNode(ref writer, node, children, names);
        });
    }

    private void GetGroupProperties(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var ids = reader.ReadArrayOfInt32();
        var names = reader.ReadArrayOfString();
        var menu = _layout.Now;
        var found = ids.Select(menu.Find).OfType<MenuEntry>().ToList();
        TrayWire.Reply(context, "a(ia{sv})", (ref MessageWriter writer) =>
        {
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (var entry in found)
            {
                writer.WriteStructureStart();
                writer.WriteInt32(entry.Id);
                TrayWire.WriteProperties(ref writer, entry.Only(names));
            }

            writer.WriteArrayEnd(array);
        });
    }

    private void GetProperty(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var id = reader.ReadInt32();
        var name = reader.ReadString();
        if (_layout.Now.Find(id) is not { } entry || entry.Only([name]) is not [var property])
        {
            context.ReplyError(InvalidArgs, "no such menu item or property");
            return;
        }

        TrayWire.Reply(context, "v", (ref MessageWriter writer) => TrayWire.WriteVariant(ref writer, property.Value));
    }

    private void Event(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var id = reader.ReadInt32();
        var eventId = reader.ReadString();
        var menu = _layout.Now;
        if (menu.Find(id) is null)
        {
            context.ReplyError(InvalidArgs, "no such menu item");
            return;
        }

        TrayWire.ReplyEmpty(context);
        Tell(CommandOfEvent(menu, id, eventId));
    }

    private void EventGroup(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var menu = _layout.Now;
        var unknown = new List<int>();
        var commands = new List<TrayCommand>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var id = reader.ReadInt32();
            var eventId = reader.ReadString();
            reader.ReadVariantValue();
            reader.ReadUInt32();
            if (menu.Find(id) is null)
            {
                unknown.Add(id);
            }
            else
            {
                commands.Add(CommandOfEvent(menu, id, eventId));
            }
        }

        TrayWire.Reply(context, "ai", (ref MessageWriter writer) => writer.WriteArray(unknown.ToArray()));
        commands.ForEach(Tell);
    }

    private void AboutToShow(MethodContext context)
    {
        var id = context.Request.GetBodyReader().ReadInt32();
        if (_layout.Now.Find(id) is null)
        {
            context.ReplyError(InvalidArgs, "no such menu item");
            return;
        }

        // Only the root opens: the lines have no menus of their own.
        var changed = id == MenuLayout.RootId && _refresh();
        TrayWire.Reply(context, "b", (ref MessageWriter writer) => writer.WriteBool(changed));
    }

    private void AboutToShowGroup(MethodContext context)
    {
        var ids = context.Request.GetBodyReader().ReadArrayOfInt32();
        var menu = _layout.Now;
        var unknown = ids.Where(id => menu.Find(id) is null).ToArray();
        int[] updated = ids.Contains(MenuLayout.RootId) && _refresh() ? [MenuLayout.RootId] : [];
        TrayWire.Reply(context, "aiai", (ref MessageWriter writer) =>
        {
            writer.WriteArray(updated);
            writer.WriteArray(unknown);
        });
    }

    private void Tell(TrayCommand command)
    {
        if (command != TrayCommand.None)
        {
            _clicked(command);
        }
    }
}
