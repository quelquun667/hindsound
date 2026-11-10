using Tmds.DBus.Protocol;

namespace Hindsound.Linux.DBus;

/// <summary>A method of an object on the bus. The names are checked when it is made, not when the bus refuses them.</summary>
internal sealed record BusMethod
{
    public BusMethod(string destination, string path, string @interface, string member)
    {
        Destination = BusChecks.Require(destination, DBusNames.IsBusName, "bus name");
        Path = BusChecks.Require(path, DBusNames.IsObjectPath, "object path");
        Interface = BusChecks.Require(@interface, DBusNames.IsInterfaceName, "interface name");
        Member = BusChecks.Require(member, DBusNames.IsMemberName, "member name");
    }

    public string Destination { get; }

    public string Path { get; }

    public string Interface { get; }

    public string Member { get; }

    /// <summary>For the log: what was called, never with what.</summary>
    public override string ToString() => Interface + "." + Member;
}

/// <summary>A signal to watch. A null sender or path matches any.</summary>
internal sealed record BusSignal
{
    public BusSignal(string? sender, string? path, string @interface, string member)
    {
        Sender = sender is null ? null : BusChecks.Require(sender, DBusNames.IsBusName, "bus name");
        Path = path is null ? null : BusChecks.Require(path, DBusNames.IsObjectPath, "object path");
        Interface = BusChecks.Require(@interface, DBusNames.IsInterfaceName, "interface name");
        Member = BusChecks.Require(member, DBusNames.IsMemberName, "member name");
    }

    public string? Sender { get; }

    public string? Path { get; }

    public string Interface { get; }

    public string Member { get; }

    /// <summary>The rule the bus matches the signal by.</summary>
    public MatchRule Rule() => new()
    {
        Type = MessageType.Signal,
        Sender = Sender,
        Path = Path,
        Interface = Interface,
        Member = Member,
    };

    public override string ToString() => Interface + "." + Member;
}

internal static class BusChecks
{
    public static string Require(string value, Func<string, bool> valid, string what) =>
        valid(value) ? value : throw new ArgumentException($"\"{value}\" is not a valid D-Bus {what}");
}
