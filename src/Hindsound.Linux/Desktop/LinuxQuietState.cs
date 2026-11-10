using Hindsound.App;
using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The "do not disturb" of the desktop, from the property Inhibited of org.freedesktop.Notifications, where the server has
/// it: the property is not in the specification, and a server without it means no quiet state. Read off the loop and kept: the first read
/// answers at once, as if notifications were welcome, and the next ones know.
/// </summary>
internal sealed class LinuxQuietState : IQuietState
{
    internal const string Property = "Inhibited";

    internal static readonly BusMethod GetProperty = new("org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.DBus.Properties", "Get");
    internal static readonly BusSignal PropertiesChanged = new("org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.DBus.Properties", "PropertiesChanged");

    private const string NotificationsInterface = "org.freedesktop.Notifications";

    private readonly Func<SessionBus?> _bus;
    private int _started;
    private volatile bool _inhibited;
    private IDisposable? _watch;

    /// <summary>Whether the server has the property, and its changes are watched.</summary>
    internal bool Watching => _watch is not null;

    public LinuxQuietState()
        : this(SharedBus)
    {
    }

    internal LinuxQuietState(Func<SessionBus?> bus) => _bus = bus;

    public UserNotificationState Read()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = Task.Run(StartAsync);
        }

        return StateOf(_inhibited);
    }

    /// <summary>What the app makes of the property: an inhibited server is the quiet hours of Windows.</summary>
    public static UserNotificationState StateOf(bool inhibited) =>
        inhibited ? UserNotificationState.QuietTime : UserNotificationState.AcceptsNotifications;

    /// <summary>The value of <see cref="Property"/> in the changes of a PropertiesChanged signal, or null when it is not there.</summary>
    public static bool? InhibitedIn(string @interface, Dictionary<string, VariantValue> changed) =>
        @interface == NotificationsInterface && changed.TryGetValue(Property, out var value) && value.Type == VariantValueType.Bool ? value.GetBool() : null;

    private async Task StartAsync()
    {
        if (_bus() is not { } bus)
        {
            return;
        }

        var answer = await bus.CallAsync(GetProperty, "ss", (ref MessageWriter writer) =>
        {
            writer.WriteString(NotificationsInterface);
            writer.WriteString(Property);
        }, (Message message, object? _) => message.GetBodyReader().ReadVariantValue()).ConfigureAwait(false);
        if (!answer.Ok || answer.Value.Type != VariantValueType.Bool)
        {
            // The server has no such property: nothing to watch either.
            return;
        }

        _inhibited = answer.Value.GetBool();
        _watch = await bus.WatchSignalAsync(PropertiesChanged, (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            return InhibitedIn(reader.ReadString(), reader.ReadDictionaryOfStringToVariantValue());
        }, inhibited =>
        {
            if (inhibited is { } value)
            {
                _inhibited = value;
            }
        }).ConfigureAwait(false);
    }

    private static SessionBus? SharedBus()
    {
        try
        {
            return SessionBus.Shared;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
