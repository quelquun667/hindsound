using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Input;

/// <summary>
/// The global shortcuts through the desktop portal, org.freedesktop.portal.GlobalShortcuts (versions 1 and 2 alike: the
/// app uses nothing of version 2). The app proposes a key per action, the desktop asks the user, and the answer comes
/// later: the actions are pending until then. <see cref="PortalBindings"/> keeps the state; this class talks to the bus.
/// </summary>
/// <remarks>
/// Every call returns a request handle at once and answers with the Response signal of org.freedesktop.portal.Request on
/// that handle. The handle is known before the call (<see cref="DBusNames.PortalRequestPath"/>), so the signal is watched
/// first and cannot be missed. The signals are matched on any sender: the path and the session handle say they are ours.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class PortalHotkeys : IHotkeyNative
{
    public const string PortalName = "org.freedesktop.portal.Desktop";
    public const string PortalPath = "/org/freedesktop/portal/desktop";
    public const string ShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";
    public const string RequestInterface = "org.freedesktop.portal.Request";
    public const string SessionInterface = "org.freedesktop.portal.Session";
    public const string RegistryInterface = "org.freedesktop.host.portal.Registry";

    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

    // The user may leave the dialog open; past this the batch is given up and the next one asks again.
    private static readonly TimeSpan BindTimeout = TimeSpan.FromMinutes(10);

    // A portal that closes each new session at once must not be asked again in a loop: one new session per this much.
    private static readonly TimeSpan ReopenSpacing = TimeSpan.FromMinutes(1);

    private static readonly BusSignal OwnerChanged = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameOwnerChanged");

    private readonly SessionBus _bus;
    private readonly string _portal;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private readonly PortalBindings _state = new();
    private readonly SemaphoreSlim _turn = new(1, 1);
    private DBusConnection? _connection;
    private string? _session;
    private bool _sessionBound;
    private IDisposable? _activated;
    private IDisposable? _changed;
    private IDisposable? _owner;
    private IDisposable? _closed;
    private long _reopenedAt = long.MinValue;
    private bool _disposed;

    private PortalHotkeys(SessionBus bus, string portal, Action<string> log)
    {
        _bus = bus;
        _portal = portal;
        _log = log;
    }

    public event Action<HotkeyAction>? Pressed;

    public event Action? BindingsChanged;

    public bool Pending
    {
        get
        {
            lock (_gate)
            {
                return _state.Pending;
            }
        }
    }

    /// <summary>
    /// Opens the layer: registers the app's id with the portal when the desktop has that interface, reads the version of
    /// GlobalShortcuts, watches its signals and opens a session. Null when the bus or the portal is missing, or refuses.
    /// </summary>
    /// <param name="portal">The bus name of the portal; a test puts its own there.</param>
    public static async Task<PortalHotkeys?> OpenAsync(SessionBus bus, Action<string> log, string portal = PortalName)
    {
        if (await bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            log("hotkeys: no session bus, so no portal");
            return null;
        }

        var layer = new PortalHotkeys(bus, portal, log);
        if (!await layer.StartAsync(connection, announce: true).ConfigureAwait(false))
        {
            layer.Dispose();
            return null;
        }

        bus.Connected += layer.OnConnected;
        return layer;
    }

    public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
    {
        IReadOnlyList<HotkeyBinding> answers;
        PortalBatch? batch;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            (answers, batch) = _state.Take(requests);
        }

        if (batch is not null)
        {
            _ = Task.Run(() => SendAsync(batch));
        }

        return answers;
    }

    public HotkeyGesture? BoundKey(HotkeyAction action)
    {
        lock (_gate)
        {
            return _state.BoundKey(action);
        }
    }

    public HotkeyBinding Current(HotkeyAction action)
    {
        lock (_gate)
        {
            return _state.Current(action);
        }
    }

    public void Dispose()
    {
        string? session;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            session = _session;
            _session = null;
        }

        _bus.Connected -= OnConnected;
        DropWatches();
        if (session is not null)
        {
            _ = Task.Run(() => CloseAsync(session));
        }
    }

    // A new connection after a loss: the old session and its keys went with the old one.
    private void OnConnected(DBusConnection connection)
    {
        if (ReferenceEquals(connection, Volatile.Read(ref _connection)))
        {
            return;
        }

        Rebuild(connection);
    }

    // The portal ended and started again (a crash, or its service restarted), the bus staying up: the app's id, the
    // session and its keys went with the old one. "Applications should ideally listen for the D-Bus NameOwnerChanged
    // signal to re-register if the portal service is restarted" (org.freedesktop.host.portal.Registry).
    private void OnOwnerChanged((string Name, string OldOwner, string NewOwner) change)
    {
        if (change.Name != _portal || _disposed)
        {
            return;
        }

        if (change.NewOwner.Length == 0)
        {
            _log("hotkeys: the portal left");
            return;
        }

        if (Volatile.Read(ref _connection) is { } connection)
        {
            _log("hotkeys: the portal started again, the shortcuts are bound again");
            Rebuild(connection);
        }
    }

    // The portal closed the session itself: its keys are gone, the portal and the app's id are not. A new session binds
    // the same keys again, at most once per ReopenSpacing.
    private void OnSessionClosed(string path)
    {
        TimeSpan wait;
        lock (_gate)
        {
            if (_disposed || path != _session)
            {
                return;
            }

            _session = null;
            _sessionBound = false;
            var now = Environment.TickCount64;
            var next = _reopenedAt == long.MinValue ? now : _reopenedAt + (long)ReopenSpacing.TotalMilliseconds;
            _reopenedAt = Math.Max(now, next);
            wait = TimeSpan.FromMilliseconds(Math.Max(0, next - now));
        }

        _log("hotkeys: the portal closed the session of the shortcuts, they are bound again in a new one");
        _ = Task.Run(async () =>
        {
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait).ConfigureAwait(false);
            }

            PortalBatch? batch;
            lock (_gate)
            {
                batch = _disposed || _session is not null ? null : _state.Lost();
            }

            if (batch is not null)
            {
                BindingsChanged?.Invoke();
                await SendAsync(batch).ConfigureAwait(false);
            }
        });
    }

    private void DropWatches()
    {
        _activated?.Dispose();
        _changed?.Dispose();
        _owner?.Dispose();
        _closed?.Dispose();
    }

    // Everything again on a connection: the app's id, the watches, a session, and the keys wanted.
    private void Rebuild(DBusConnection connection)
    {
        _ = Task.Run(async () =>
        {
            DropWatches();
            lock (_gate)
            {
                _session = null;
                _sessionBound = false;
            }

            if (!await StartAsync(connection, announce: false).ConfigureAwait(false))
            {
                return;
            }

            PortalBatch? batch;
            lock (_gate)
            {
                batch = _disposed ? null : _state.Lost();
            }

            if (batch is not null)
            {
                await SendAsync(batch).ConfigureAwait(false);
            }

            BindingsChanged?.Invoke();
        });
    }

    private async Task<bool> StartAsync(DBusConnection connection, bool announce)
    {
        Volatile.Write(ref _connection, connection);

        // Before any other call to the portal, or it is refused; a desktop without the interface answers an error, ignored.
        // "The application ID must be able to match the basename of a .desktop file that describes the application"
        // (org.freedesktop.host.portal.Registry): a launch from an AppImage or from the folder of the archive has written it
        // before the hotkeys open (ApplicationEntry, from the entry point); a copy installed by hand has the one of its package.
        var registered = await _bus.CallAsync(new BusMethod(_portal, PortalPath, RegistryInterface, "Register"), "sa{sv}", PortalMessages.WriteRegister).ConfigureAwait(false);
        if (!registered.Ok && announce)
        {
            _log(UnregisteredLine(registered.Error));
        }

        var version = await _bus.CallAsync(
            new BusMethod(_portal, PortalPath, "org.freedesktop.DBus.Properties", "Get"),
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(ShortcutsInterface);
                writer.WriteString("version");
            },
            (Message message, object? _) => PortalMessages.ReadVersion(message)).ConfigureAwait(false);
        // Watched even when the portal does not answer here: a portal that comes back later is bound from there. A portal
        // the calls above started on demand was already up before this watch, so it does not count as a restart.
        _owner = await _bus.WatchSignalAsync(OwnerChanged, ReadOwnerChange, OnOwnerChanged).ConfigureAwait(false);
        if (!version.Ok || version.Value < 1)
        {
            _log("hotkeys: no GlobalShortcuts portal (" + (version.Error ?? "version " + version.Value) + ")");
            return false;
        }

        if (announce)
        {
            _log($"hotkeys: GlobalShortcuts portal, version {version.Value}");
        }

        _activated = await _bus.WatchSignalAsync(new BusSignal(null, PortalPath, ShortcutsInterface, "Activated"), (Message message, object? _) => PortalMessages.ReadActivated(message), OnActivated).ConfigureAwait(false);
        _changed = await _bus.WatchSignalAsync(new BusSignal(null, PortalPath, ShortcutsInterface, "ShortcutsChanged"), (Message message, object? _) => PortalMessages.ReadShortcutsChanged(message), OnShortcutsChanged).ConfigureAwait(false);
        _closed = await _bus.WatchSignalAsync(new BusSignal(null, null, SessionInterface, "Closed"), (Message message, object? _) => message.PathAsString ?? string.Empty, OnSessionClosed).ConfigureAwait(false);
        if (_activated is null || _changed is null)
        {
            return false;
        }

        var session = await CreateSessionAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _session = session;
            _sessionBound = false;
        }

        return session is not null;
    }

    private void OnActivated((string Session, string Id) activated)
    {
        lock (_gate)
        {
            if (_disposed || activated.Session != _session)
            {
                return;
            }
        }

        if (PortalBindings.TryActionOf(activated.Id, out var action))
        {
            Pressed?.Invoke(action);
        }
    }

    private void OnShortcutsChanged((string Session, List<PortalShortcut> Shortcuts) changed)
    {
        bool any;
        lock (_gate)
        {
            any = !_disposed && changed.Session == _session && _state.Changed(changed.Shortcuts);
        }

        if (any)
        {
            BindingsChanged?.Invoke();
        }
    }

    // One batch at a time, in order; a batch overtaken by a newer one is dropped.
    private async Task SendAsync(PortalBatch batch)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            string? session;
            lock (_gate)
            {
                if (_disposed || (batch.Offers.Count > 0 && batch.Generation != _state.Waiting))
                {
                    return;
                }

                session = _sessionBound ? null : _session;
            }

            if (session is null)
            {
                // A session binds once: the keys of the old one are given back by closing it, and a new one is opened.
                string? old;
                lock (_gate)
                {
                    old = _session;
                    _session = null;
                }

                if (old is not null)
                {
                    await CloseAsync(old).ConfigureAwait(false);
                }

                if (batch.Offers.Count == 0)
                {
                    return;
                }

                session = await CreateSessionAsync().ConfigureAwait(false);
                lock (_gate)
                {
                    _session = session;
                    _sessionBound = false;
                }
            }

            if (batch.Offers.Count == 0)
            {
                // Nothing wanted, and the session was never bound: it holds nothing to give back.
                return;
            }

            var answer = session is null
                ? PortalResponse.Failed
                : await RequestAsync(
                    new BusMethod(_portal, PortalPath, ShortcutsInterface, "BindShortcuts"),
                    "oa(sa{sv})sa{sv}",
                    token => (ref MessageWriter writer) => PortalMessages.WriteBindShortcuts(ref writer, session, batch.Offers, token),
                    BindTimeout).ConfigureAwait(false);

            bool changed;
            lock (_gate)
            {
                _sessionBound = session is not null;
                changed = _state.Answered(batch.Generation, answer.Code, answer.Shortcuts);
            }

            if (answer.Code != PortalBindings.Success)
            {
                _log($"hotkeys: the portal did not bind the shortcuts (answer {answer.Code}{(answer.Error is null ? "" : ", " + answer.Error)})");
            }

            if (changed)
            {
                BindingsChanged?.Invoke();
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task<string?> CreateSessionAsync()
    {
        var sessionToken = DBusNames.NewToken();
        var answer = await RequestAsync(
            new BusMethod(_portal, PortalPath, ShortcutsInterface, "CreateSession"),
            "a{sv}",
            token => (ref MessageWriter writer) => PortalMessages.WriteCreateSession(ref writer, token, sessionToken),
            SessionTimeout).ConfigureAwait(false);
        if (answer.Code != PortalBindings.Success || answer.SessionHandle is null)
        {
            _log($"hotkeys: the portal opened no session (answer {answer.Code}{(answer.Error is null ? "" : ", " + answer.Error)})");
            return null;
        }

        return answer.SessionHandle;
    }

    /// <summary>The log line of a Register the portal refused: what the portal needs to know the app, never a guess at why.</summary>
    internal static string UnregisteredLine(string? error) =>
        "hotkeys: the portal did not register the app id (" + error + "); it knows the app only by an installed "
        + DBusNames.AppId + ".desktop file, and the shortcuts may not work without one";

    private static (string Name, string OldOwner, string NewOwner) ReadOwnerChange(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadString(), reader.ReadString(), reader.ReadString());
    }

    private Task<BusAnswer<bool>> CloseAsync(string session) =>
        _bus.CallAsync(new BusMethod(_portal, session, SessionInterface, "Close"), null, null);

    /// <summary>Calls a method of the portal and waits for the Response on its request handle, up to <paramref name="timeout"/>.</summary>
    private async Task<PortalResponse> RequestAsync(BusMethod method, string signature, Func<string, BodyWriter> body, TimeSpan timeout)
    {
        if (await _bus.ConnectionAsync().ConfigureAwait(false) is not { UniqueName: { } uniqueName })
        {
            return PortalResponse.Failed with { Error = "no session bus" };
        }

        var token = DBusNames.NewToken();
        var handle = DBusNames.PortalRequestPath(uniqueName, token);
        var answered = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        MessageValueReader<PortalResponse> read = (Message message, object? _) => PortalMessages.ReadResponse(message);
        using var watch = await _bus.WatchSignalAsync(new BusSignal(null, handle, RequestInterface, "Response"), read, response => answered.TrySetResult(response)).ConfigureAwait(false);
        if (watch is null)
        {
            return PortalResponse.Failed with { Error = "the answer could not be watched" };
        }

        var call = await _bus.CallAsync(method, signature, body(token), (Message message, object? _) => message.GetBodyReader().ReadObjectPathAsString()).ConfigureAwait(false);
        if (!call.Ok)
        {
            return PortalResponse.Failed with { Error = call.Error };
        }

        // A portal older than the fixed handle answers on the one it returns.
        using var other = call.Value is { } returned && returned != handle
            ? await _bus.WatchSignalAsync(new BusSignal(null, returned, RequestInterface, "Response"), read, response => answered.TrySetResult(response)).ConfigureAwait(false)
            : null;

        var done = await Task.WhenAny(answered.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return done == answered.Task ? answered.Task.Result : PortalResponse.Failed with { Error = "no answer in time" };
    }
}
