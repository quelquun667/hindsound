using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tray;

/// <summary>
/// Puts org.freedesktop.DBus.Properties (Get, GetAll; Set refused, every property being read-only) and
/// org.freedesktop.DBus.Introspectable on an object, for one interface whose properties <c>read</c> gives at the time of the call.
/// </summary>
internal static class BusProperties
{
    public const string Interface = "org.freedesktop.DBus.Properties";
    public const string Introspectable = "org.freedesktop.DBus.Introspectable";
    private const string UnknownProperty = "org.freedesktop.DBus.Error.UnknownProperty";
    private const string ReadOnly = "org.freedesktop.DBus.Error.PropertyReadOnly";

    /// <summary>Answers Get(ss) with a variant, GetAll(s) with a{sv}, Set with an error, and Introspect with <paramref name="xml"/>.</summary>
    public static void Serve(Action<string, string, Action<MethodContext>> on, string @interface, Func<IReadOnlyList<KeyValuePair<string, object>>> read, string xml)
    {
        on(Interface, "Get", context =>
        {
            var reader = context.Request.GetBodyReader();
            var asked = reader.ReadString();
            var name = reader.ReadString();
            if (asked != @interface || !TryFind(read(), name, out var value))
            {
                context.ReplyError(UnknownProperty, "no such property");
                return;
            }

            TrayWire.Reply(context, "v", (ref MessageWriter writer) => TrayWire.WriteVariant(ref writer, value));
        });
        on(Interface, "GetAll", context =>
        {
            var asked = context.Request.GetBodyReader().ReadString();
            IReadOnlyList<KeyValuePair<string, object>> properties = asked == @interface ? read() : [];
            TrayWire.Reply(context, "a{sv}", (ref MessageWriter writer) => TrayWire.WriteProperties(ref writer, properties));
        });
        on(Interface, "Set", context => context.ReplyError(ReadOnly, "read-only"));
        on(Introspectable, "Introspect", context => TrayWire.Reply(context, "s", (ref MessageWriter writer) => writer.WriteString(xml)));
    }

    private static bool TryFind(IReadOnlyList<KeyValuePair<string, object>> properties, string name, out object value)
    {
        foreach (var property in properties)
        {
            if (property.Key == name)
            {
                value = property.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>The introspection of an object with these interfaces, as the DTD of the D-Bus specification lays it out.</summary>
    public static string Xml(params string[] interfaces) =>
        "<!DOCTYPE node PUBLIC \"-//freedesktop//DTD D-BUS Object Introspection 1.0//EN\" \"http://www.freedesktop.org/standards/dbus/1.0/introspect.dtd\">\n<node>\n"
        + "<interface name=\"" + Introspectable + "\"><method name=\"Introspect\"><arg type=\"s\" direction=\"out\"/></method></interface>\n"
        + "<interface name=\"" + Interface + "\">"
        + "<method name=\"Get\"><arg type=\"s\" direction=\"in\"/><arg type=\"s\" direction=\"in\"/><arg type=\"v\" direction=\"out\"/></method>"
        + "<method name=\"GetAll\"><arg type=\"s\" direction=\"in\"/><arg type=\"a{sv}\" direction=\"out\"/></method>"
        + "<method name=\"Set\"><arg type=\"s\" direction=\"in\"/><arg type=\"s\" direction=\"in\"/><arg type=\"v\" direction=\"in\"/></method>"
        + "</interface>\n"
        + string.Join("\n", interfaces)
        + "\n</node>\n";
}
