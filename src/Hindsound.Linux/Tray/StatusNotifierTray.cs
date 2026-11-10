using Hindsound.App;
using Hindsound.Linux.DBus;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tray;

/// <summary>
/// The icon and its menu, through StatusNotifierItem and com.canonical.dbusmenu: the item is put on the session bus under a
/// name of its own and registered with the StatusNotifierWatcher, which shows it. A desktop without a watcher (GNOME without
/// the AppIndicator extension) shows no icon: the log says so, and the app is still there through its shortcut and
/// <c>hindsound --save</c>. A watcher that comes, or comes back, is told <see cref="Lost"/>, and the app adds the icon again.
/// </summary>
internal sealed class StatusNotifierTray : ITrayIcon
{
    public const string WatcherName = "org.kde.StatusNotifierWatcher";
    public const string WatcherPath = "/StatusNotifierWatcher";

    /// <summary>How long adding the icon may hold the loop: the calls to the bus take a few milliseconds when it answers.</summary>
    internal const int AddWaitMs = 2000;

    /// <summary>How long the host waits for the app's menu when it opens; past that it shows the lines it already has.</summary>
    internal const int MenuWaitMs = 300;

    private const uint DoNotQueue = 4;
    private const uint PrimaryOwner = 1;
    private const uint AlreadyOwner = 4;

    private static readonly BusMethod Register = new(WatcherName, WatcherPath, WatcherName, "RegisterStatusNotifierItem");
    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusSignal OwnerChanged = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameOwnerChanged");

    // The specification names an item by its process and a number of its own: two icons of one process never share a name.
    private static int s_items;

    private readonly IAppLoop _loop;
    private readonly Action<string> _log;
    private readonly SessionBus? _bus;
    private readonly MenuLayout _layout = new();
    private readonly StatusNotifierItemObject _item;
    private readonly DBusMenuObject _menu;
    private readonly object _gate = new();
    private Task<bool>? _registering;
    private TaskCompletionSource? _menuAsked;
    private DBusConnection? _exportedOn;
    private IDisposable? _ownerWatch;
    private volatile bool _registered;
    private volatile bool _disposed;
    private bool _absenceSaid;

    /// <param name="bus">The session bus; the app's shared one when null.</param>
    public StatusNotifierTray(IAppLoop loop, Action<string> log, SessionBus? bus = null)
    {
        _loop = loop;
        _log = log;
        _bus = bus ?? SharedBus();
        ItemName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-{Interlocked.Increment(ref s_items)}";
        _item = new StatusNotifierItemObject(gesture => _loop.Post(() => Gesture?.Invoke(gesture)));
        _menu = new DBusMenuObject(_layout, AskForMenu, command => _loop.Post(() => Chosen?.Invoke(command)));
        if (_bus is not null)
        {
            _bus.Connected += OnConnected;
        }
    }

    /// <summary>The name the item has on the bus, the one it registers with the watcher.</summary>
    public string ItemName { get; }

    public Action<TrayGesture>? Gesture { get; set; }

    public Action<TrayCommand>? Chosen { get; set; }

    public Action? Lost { get; set; }

    public int IconSize => IconPixmaps.Sizes[0];

    public bool IconIsStale => false;

    /// <summary>The lines of the menu as the host reads them.</summary>
    internal MenuLayout Layout => _layout;

    public bool Add(TrayState state, uint accent, string tip)
    {
        _item.Look = _item.Look.With(state, accent, tip);
        var registering = Registering();
        return registering.Wait(AddWaitMs) && registering.Result;
    }

    public bool Update(TrayState state, uint accent, string tip)
    {
        var before = _item.Look;
        var now = before.With(state, accent, tip);
        _item.Look = now;
        if (!_registered)
        {
            _ = Registering();
            return false;
        }

        if (!ReferenceEquals(now.Pixmaps, before.Pixmaps))
        {
            Emit(StatusNotifierItemObject.ObjectPath, StatusNotifierItemObject.Interface, "NewIcon", null, null);
        }

        if (now.Tip != before.Tip)
        {
            Emit(StatusNotifierItemObject.ObjectPath, StatusNotifierItemObject.Interface, "NewToolTip", null, null);
        }

        return true;
    }

    public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_layout.Replace(items) && _registered)
        {
            var revision = _layout.Revision;
            Emit(DBusMenuObject.ObjectPath, DBusMenuObject.Interface, "LayoutUpdated", "ui", (ref MessageWriter writer) =>
            {
                writer.WriteUInt32(revision);
                writer.WriteInt32(MenuLayout.RootId);
            });
        }

        TaskCompletionSource? asked;
        lock (_gate)
        {
            asked = _menuAsked;
            _menuAsked = null;
        }

        asked?.TrySetResult();
    }

    public void Dispose()
    {
        _disposed = true;
        _registered = false;
        if (_bus is not null)
        {
            _bus.Connected -= OnConnected;
        }

        lock (_gate)
        {
            _ownerWatch?.Dispose();
            _ownerWatch = null;
        }
    }

    // On the thread of the connection: the host opens the menu, and the app gives its lines as they are now.
    private bool AskForMenu()
    {
        if (_disposed)
        {
            return false;
        }

        var before = _layout.Revision;
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _menuAsked = asked;
        }

        _loop.Post(() => Gesture?.Invoke(TrayGesture.Menu));
        asked.Task.Wait(MenuWaitMs);
        return _layout.Revision != before;
    }

    private Task<bool> Registering()
    {
        lock (_gate)
        {
            if (_registered)
            {
                return Task.FromResult(true);
            }

            if (_registering is { IsCompleted: false } running)
            {
                return running;
            }

            return _registering = Task.Run(RegisterAsync);
        }
    }

    private async Task<bool> RegisterAsync()
    {
        if (_bus is null || _disposed)
        {
            SayAbsent("no session bus");
            return false;
        }

        if (await _bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            SayAbsent("no session bus");
            return false;
        }

        if (!ReferenceEquals(Volatile.Read(ref _exportedOn), connection))
        {
            try
            {
                connection.AddMethodHandlers([_item, _menu]);
            }
            catch (Exception ex)
            {
                _log($"the icon: putting it on the bus failed: {ex.GetType().Name}");
                return false;
            }

            Volatile.Write(ref _exportedOn, connection);
            var watch = await _bus.WatchSignalAsync(OwnerChanged, ReadOwnerChange, OnOwnerChanged).ConfigureAwait(false);
            lock (_gate)
            {
                _ownerWatch?.Dispose();
                _ownerWatch = watch;
            }
        }

        var owned = await _bus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
        {
            writer.WriteString(ItemName);
            writer.WriteUInt32(DoNotQueue);
        }, (Message message, object? _) => message.GetBodyReader().ReadUInt32()).ConfigureAwait(false);
        if (!owned.Ok || owned.Value is not (PrimaryOwner or AlreadyOwner))
        {
            _log($"the icon: the bus did not give the name of the icon ({owned.Error ?? "answer " + owned.Value})");
            return false;
        }

        var registered = await _bus.CallAsync(Register, "s", (ref MessageWriter writer) => writer.WriteString(ItemName)).ConfigureAwait(false);
        if (!registered.Ok)
        {
            SayAbsent(registered.Error ?? "refused");
            return false;
        }

        _registered = !_disposed;
        lock (_gate)
        {
            _absenceSaid = false;
        }

        _log("the icon: registered with the StatusNotifierWatcher");
        return _registered;
    }

    private void SayAbsent(string why)
    {
        lock (_gate)
        {
            if (_absenceSaid)
            {
                return;
            }

            _absenceSaid = true;
        }

        _log($"the icon: unavailable, no StatusNotifierWatcher took it ({why}); on GNOME the AppIndicator extension gives one. Clips are still saved by the shortcut and by hindsound --save");
    }

    private static (string Name, string OldOwner, string NewOwner) ReadOwnerChange(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadString(), reader.ReadString(), reader.ReadString());
    }

    // On the thread of the connection. A watcher that starts, or starts again, has forgotten the icon.
    private void OnOwnerChanged((string Name, string OldOwner, string NewOwner) change)
    {
        if (change.Name != WatcherName || _disposed)
        {
            return;
        }

        _registered = false;
        if (change.NewOwner.Length == 0)
        {
            _log("the icon: the StatusNotifierWatcher left");
            return;
        }

        _log("the icon: a StatusNotifierWatcher came");
        _loop.Post(() => Lost?.Invoke());
    }

    // A connection after the first one: the objects and the name went with the one before.
    private void OnConnected(DBusConnection connection)
    {
        var before = Volatile.Read(ref _exportedOn);
        if (before is null || ReferenceEquals(before, connection) || _disposed)
        {
            return;
        }

        _registered = false;
        _loop.Post(() => Lost?.Invoke());
    }

    private void Emit(string path, string @interface, string member, string? signature, BodyWriter? body)
    {
        if (_bus is { } bus)
        {
            _ = bus.EmitSignalAsync(path, @interface, member, signature, body);
        }
    }

    private SessionBus? SharedBus()
    {
        try
        {
            return SessionBus.Shared;
        }
        catch (InvalidOperationException)
        {
            _log("the icon: no session bus was made");
            return null;
        }
    }
}
