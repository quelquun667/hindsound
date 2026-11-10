using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tray;

/// <summary>An object path, as a property value: the D-Bus type o, not s.</summary>
internal readonly record struct BusPath(string Value);

/// <summary>The tooltip of StatusNotifierItem, D-Bus type (sa(iiay)ss): an icon name, its images, a title and a text.</summary>
internal sealed record ToolTip(string IconName, IReadOnlyList<IconPixmap> Pixmaps, string Title, string Text);

/// <summary>
/// Writes the values of the icon and of its menu into a message. Each value says its own D-Bus type: <see cref="string"/>
/// s, <see cref="bool"/> b, <see cref="int"/> i, <see cref="uint"/> u, <see cref="BusPath"/> o, a string array as, a list of
/// <see cref="IconPixmap"/> a(iiay), and a <see cref="ToolTip"/>.
/// </summary>
internal static class TrayWire
{
    public const string NodeSignature = "(ia{sv}av)";

    /// <summary>The D-Bus signature of a value, as its variant carries it.</summary>
    public static string SignatureOf(object value) => value switch
    {
        string => "s",
        bool => "b",
        int => "i",
        uint => "u",
        BusPath => "o",
        string[] => "as",
        IReadOnlyList<IconPixmap> => "a(iiay)",
        ToolTip => "(sa(iiay)ss)",
        _ => throw new ArgumentException($"no D-Bus type for {value.GetType().Name}", nameof(value)),
    };

    /// <summary>One value inside a variant: its signature, then the value.</summary>
    public static void WriteVariant(ref MessageWriter writer, object value)
    {
        writer.WriteSignature(SignatureOf(value));
        switch (value)
        {
            case string text:
                writer.WriteString(text);
                break;
            case bool flag:
                writer.WriteBool(flag);
                break;
            case int number:
                writer.WriteInt32(number);
                break;
            case uint number:
                writer.WriteUInt32(number);
                break;
            case BusPath path:
                writer.WriteObjectPath(path.Value);
                break;
            case string[] texts:
                writer.WriteArray(texts);
                break;
            case IReadOnlyList<IconPixmap> pixmaps:
                WritePixmaps(ref writer, pixmaps);
                break;
            case ToolTip tip:
                writer.WriteStructureStart();
                writer.WriteString(tip.IconName);
                WritePixmaps(ref writer, tip.Pixmaps);
                writer.WriteString(tip.Title);
                writer.WriteString(tip.Text);
                break;
        }
    }

    /// <summary>A dictionary a{sv} of properties, in their order.</summary>
    public static void WriteProperties(ref MessageWriter writer, IEnumerable<KeyValuePair<string, object>> properties)
    {
        var dictionary = writer.WriteDictionaryStart();
        foreach (var (name, value) in properties)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteVariant(ref writer, value);
        }

        writer.WriteDictionaryEnd(dictionary);
    }

    /// <summary>A node of the menu (ia{sv}av): its id, its properties, and its children, each in a variant.</summary>
    public static void WriteNode(ref MessageWriter writer, MenuEntry entry, IReadOnlyList<MenuEntry> children, IReadOnlyCollection<string> names)
    {
        writer.WriteStructureStart();
        writer.WriteInt32(entry.Id);
        WriteProperties(ref writer, entry.Only(names));
        var array = writer.WriteArrayStart(DBusType.Variant);
        foreach (var child in children)
        {
            writer.WriteSignature(NodeSignature);
            WriteNode(ref writer, child, [], names);
        }

        writer.WriteArrayEnd(array);
    }

    /// <summary>Answers a call with nothing.</summary>
    public static void ReplyEmpty(MethodContext context) => Reply(context, null, (ref MessageWriter _) => { });

    /// <summary>Answers a call with the values <paramref name="body"/> writes, of type <paramref name="signature"/>.</summary>
    public static void Reply(MethodContext context, string? signature, DBus.BodyWriter body)
    {
        var writer = context.CreateReplyWriter(signature!);
        try
        {
            body(ref writer);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void WritePixmaps(ref MessageWriter writer, IReadOnlyList<IconPixmap> pixmaps)
    {
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var pixmap in pixmaps)
        {
            writer.WriteStructureStart();
            writer.WriteInt32(pixmap.Width);
            writer.WriteInt32(pixmap.Height);
            writer.WriteArray(pixmap.Argb);
        }

        writer.WriteArrayEnd(array);
    }
}
