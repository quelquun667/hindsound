using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Input;

/// <summary>The Response of a portal request: its code (0 success, 1 cancelled, 2 other) and the results the app reads.</summary>
internal sealed record PortalResponse(uint Code, string? SessionHandle, List<PortalShortcut>? Shortcuts, string? Error = null)
{
    public static PortalResponse Failed { get; } = new(2, null, null);
}

/// <summary>
/// The bodies of the GlobalShortcuts calls and signals, written and read with the types the portal's documentation gives
/// (https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html).
/// </summary>
internal static class PortalMessages
{
    private const string ShortcutsSignature = "a(sa{sv})";

    /// <summary>Registry.Register(s app_id, a{sv} options).</summary>
    public static void WriteRegister(ref MessageWriter writer)
    {
        writer.WriteString(DBusNames.AppId);
        writer.WriteDictionaryEnd(writer.WriteDictionaryStart());
    }

    /// <summary>CreateSession(a{sv} options) with handle_token and session_handle_token.</summary>
    public static void WriteCreateSession(ref MessageWriter writer, string handleToken, string sessionToken)
    {
        var options = writer.WriteDictionaryStart();
        WriteEntry(ref writer, "handle_token", handleToken);
        WriteEntry(ref writer, "session_handle_token", sessionToken);
        writer.WriteDictionaryEnd(options);
    }

    /// <summary>
    /// BindShortcuts(o session_handle, a(sa{sv}) shortcuts, s parent_window, a{sv} options): each shortcut with its
    /// description and, when the key has words, its preferred_trigger; no parent window, as the app has none.
    /// </summary>
    public static void WriteBindShortcuts(ref MessageWriter writer, string session, IReadOnlyList<PortalOffer> offers, string handleToken)
    {
        writer.WriteObjectPath(session);
        var shortcuts = writer.WriteArrayStart(DBusType.Struct);
        foreach (var offer in offers)
        {
            writer.WriteStructureStart();
            writer.WriteString(offer.Id);
            var properties = writer.WriteDictionaryStart();
            WriteEntry(ref writer, "description", offer.Description);
            if (offer.PreferredTrigger is { } trigger)
            {
                WriteEntry(ref writer, "preferred_trigger", trigger);
            }

            writer.WriteDictionaryEnd(properties);
        }

        writer.WriteArrayEnd(shortcuts);
        writer.WriteString("");
        var options = writer.WriteDictionaryStart();
        WriteEntry(ref writer, "handle_token", handleToken);
        writer.WriteDictionaryEnd(options);
    }

    /// <summary>
    /// Request.Response(u response, a{sv} results). The results read: session_handle (a string for older portals' sake,
    /// an object path accepted too) and shortcuts; any other key is read and left.
    /// </summary>
    public static PortalResponse ReadResponse(Message message)
    {
        var reader = message.GetBodyReader();
        var code = reader.ReadUInt32();
        string? session = null;
        List<PortalShortcut>? shortcuts = null;
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var key = reader.ReadString();
            if (key == "shortcuts")
            {
                var signature = reader.ReadSignatureAsString();
                if (signature != ShortcutsSignature)
                {
                    throw new FormatException("shortcuts of type " + signature);
                }

                shortcuts = ReadShortcuts(ref reader);
            }
            else
            {
                var value = Unwrap(reader.ReadVariantValue());
                if (key == "session_handle")
                {
                    session = value.Type switch
                    {
                        VariantValueType.String => value.GetString(),
                        VariantValueType.ObjectPath => value.GetObjectPathAsString(),
                        _ => null,
                    };
                }
            }
        }

        return new PortalResponse(code, session, shortcuts);
    }

    /// <summary>Activated(o session_handle, s shortcut_id, t timestamp, a{sv} options): the session and the id.</summary>
    public static (string Session, string Id) ReadActivated(Message message)
    {
        var reader = message.GetBodyReader();
        var session = reader.ReadObjectPathAsString();
        var id = reader.ReadString();
        return (session, id);
    }

    /// <summary>ShortcutsChanged(o session_handle, a(sa{sv}) shortcuts).</summary>
    public static (string Session, List<PortalShortcut> Shortcuts) ReadShortcutsChanged(Message message)
    {
        var reader = message.GetBodyReader();
        var session = reader.ReadObjectPathAsString();
        return (session, ReadShortcuts(ref reader));
    }

    /// <summary>The answer of Properties.Get for the version: a variant holding a u; 0 for anything else.</summary>
    public static uint ReadVersion(Message message)
    {
        var reader = message.GetBodyReader();
        var value = Unwrap(reader.ReadVariantValue());
        return value.Type == VariantValueType.UInt32 ? value.GetUInt32() : 0;
    }

    private static List<PortalShortcut> ReadShortcuts(ref Reader reader)
    {
        var list = new List<PortalShortcut>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var id = reader.ReadString();
            var properties = reader.ReadDictionaryOfStringToVariantValue();
            list.Add(new PortalShortcut(id, Text(properties, "description"), Text(properties, "trigger_description")));
        }

        return list;
    }

    private static string? Text(Dictionary<string, VariantValue> properties, string key) =>
        properties.TryGetValue(key, out var value) && Unwrap(value) is { Type: VariantValueType.String } text ? text.GetString() : null;

    private static VariantValue Unwrap(VariantValue value)
    {
        while (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue();
        }

        return value;
    }

    private static void WriteEntry(ref MessageWriter writer, string key, string value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantString(value);
    }
}
