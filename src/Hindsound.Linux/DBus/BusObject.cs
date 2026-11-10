using Tmds.DBus.Protocol;

namespace Hindsound.Linux.DBus;

/// <summary>
/// An object the app puts on the bus, such as the icon of StatusNotifierItem: each method it answers is named with
/// <see cref="On"/>, and the others get the bus's "unknown method" error. Export it with <see cref="BusCalls.ExportAsync"/>.
/// </summary>
internal abstract class BusObject : IPathMethodHandler
{
    private const string Failed = "org.freedesktop.DBus.Error.Failed";

    private readonly Dictionary<(string Interface, string Member), Action<MethodContext>> _methods = [];

    protected BusObject(string path) => Path = BusChecks.Require(path, DBusNames.IsObjectPath, "object path");

    public string Path { get; }

    public bool HandlesChildPaths => false;

    /// <summary>
    /// What a call of <paramref name="member"/> on <paramref name="interface"/> runs. It runs on the thread of the
    /// connection, and answers through the context it gets: the app's own state belongs to the loop.
    /// </summary>
    protected void On(string @interface, string member, Action<MethodContext> handle) =>
        _methods[(BusChecks.Require(@interface, DBusNames.IsInterfaceName, "interface name"), BusChecks.Require(member, DBusNames.IsMemberName, "member name"))] = handle;

    /// <summary>The handler of a call, or null when this object does not answer it.</summary>
    internal Action<MethodContext>? Find(string? @interface, string? member) =>
        @interface is not null && member is not null && _methods.TryGetValue((@interface, member), out var handle) ? handle : null;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (Find(request.InterfaceAsString, request.MemberAsString) is not { } handle)
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        try
        {
            handle(context);
        }
        catch (Exception ex) when (!context.ReplySent)
        {
            // The caller waits for an answer: a failure must still give one.
            context.ReplyError(Failed, ex.GetType().Name);
        }

        return ValueTask.CompletedTask;
    }
}
