using Hindsound.Linux.DBus;
using Hindsound.Ui;

namespace Hindsound.Linux.Tray;

/// <summary>What the icon shows: its shape, its colour and its tooltip, with the images drawn once for them.</summary>
internal sealed record ItemLook(TrayState State, uint Accent, string Tip, IReadOnlyList<IconPixmap> Pixmaps)
{
    public static ItemLook Of(TrayState state, uint accent, string tip) => new(state, accent, tip, IconPixmaps.Of(state, accent));

    /// <summary>The same look with these values; the images are drawn again only when the shape or the colour changed.</summary>
    public ItemLook With(TrayState state, uint accent, string tip) =>
        state == State && accent == Accent ? this with { Tip = tip } : Of(state, accent, tip);
}

/// <summary>
/// The icon on the session bus: org.kde.StatusNotifierItem at /StatusNotifierItem, with its properties and the methods a host
/// calls on a click. Each gesture goes to <c>gesture</c>, on the thread of the connection.
/// </summary>
internal sealed class StatusNotifierItemObject : BusObject
{
    public const string ObjectPath = "/StatusNotifierItem";
    public const string Interface = "org.kde.StatusNotifierItem";
    public const string Title = "Hindsound";

    private static readonly string IntrospectionXml = BusProperties.Xml(
        "<interface name=\"" + Interface + "\">"
        + "<method name=\"ContextMenu\"><arg type=\"i\" direction=\"in\"/><arg type=\"i\" direction=\"in\"/></method>"
        + "<method name=\"Activate\"><arg type=\"i\" direction=\"in\"/><arg type=\"i\" direction=\"in\"/></method>"
        + "<method name=\"SecondaryActivate\"><arg type=\"i\" direction=\"in\"/><arg type=\"i\" direction=\"in\"/></method>"
        + "<method name=\"Scroll\"><arg type=\"i\" direction=\"in\"/><arg type=\"s\" direction=\"in\"/></method>"
        + "<signal name=\"NewTitle\"/><signal name=\"NewIcon\"/><signal name=\"NewToolTip\"/><signal name=\"NewStatus\"><arg type=\"s\"/></signal>"
        + "<property name=\"Category\" type=\"s\" access=\"read\"/><property name=\"Id\" type=\"s\" access=\"read\"/>"
        + "<property name=\"Title\" type=\"s\" access=\"read\"/><property name=\"Status\" type=\"s\" access=\"read\"/>"
        + "<property name=\"WindowId\" type=\"i\" access=\"read\"/><property name=\"IconName\" type=\"s\" access=\"read\"/>"
        + "<property name=\"IconPixmap\" type=\"a(iiay)\" access=\"read\"/><property name=\"OverlayIconName\" type=\"s\" access=\"read\"/>"
        + "<property name=\"OverlayIconPixmap\" type=\"a(iiay)\" access=\"read\"/><property name=\"AttentionIconName\" type=\"s\" access=\"read\"/>"
        + "<property name=\"AttentionIconPixmap\" type=\"a(iiay)\" access=\"read\"/><property name=\"AttentionMovieName\" type=\"s\" access=\"read\"/>"
        + "<property name=\"ToolTip\" type=\"(sa(iiay)ss)\" access=\"read\"/><property name=\"ItemIsMenu\" type=\"b\" access=\"read\"/>"
        + "<property name=\"Menu\" type=\"o\" access=\"read\"/>"
        + "</interface>");

    private ItemLook _look = ItemLook.Of(TrayState.Active, TrayIconShape.PlateColor, Title);

    public StatusNotifierItemObject(Action<TrayGesture> gesture)
        : base(ObjectPath)
    {
        // The app has no window here, so the item says it is a menu: a host shows the menu on a left click too.
        On(Interface, "Activate", context => Then(context, () => gesture(TrayGesture.OpenSettings)));
        On(Interface, "ContextMenu", context => Then(context, () => gesture(TrayGesture.Menu)));
        On(Interface, "SecondaryActivate", TrayWire.ReplyEmpty);
        On(Interface, "Scroll", TrayWire.ReplyEmpty);
        BusProperties.Serve(On, Interface, () => PropertiesOf(Look), IntrospectionXml);
    }

    /// <summary>What the icon shows now. Set on the thread of the loop, read on the thread of the connection.</summary>
    public ItemLook Look
    {
        get => Volatile.Read(ref _look);
        set => Volatile.Write(ref _look, value);
    }

    /// <summary>The properties of the item, in the order of the specification. Pure: tested without a bus.</summary>
    public static IReadOnlyList<KeyValuePair<string, object>> PropertiesOf(ItemLook look) =>
    [
        new("Category", "ApplicationStatus"),
        new("Id", DBusNames.AppId),
        new("Title", Title),
        new("Status", "Active"),
        new("WindowId", 0),
        new("IconName", string.Empty),
        new("IconPixmap", look.Pixmaps),
        new("OverlayIconName", string.Empty),
        new("OverlayIconPixmap", Array.Empty<IconPixmap>()),
        new("AttentionIconName", string.Empty),
        new("AttentionIconPixmap", Array.Empty<IconPixmap>()),
        new("AttentionMovieName", string.Empty),
        new("ToolTip", new ToolTip(string.Empty, [], Title, look.Tip)),
        new("ItemIsMenu", true),
        new("Menu", new BusPath(DBusMenuObject.ObjectPath)),
    ];

    private static void Then(Tmds.DBus.Protocol.MethodContext context, Action gesture)
    {
        TrayWire.ReplyEmpty(context);
        gesture();
    }
}
