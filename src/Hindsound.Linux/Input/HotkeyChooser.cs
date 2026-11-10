using Hindsound.Input;

namespace Hindsound.Linux.Input;

/// <summary>
/// The shortcut layer the desktop allows, picked at start (<see cref="HotkeyChoice.First"/>) and stood in for until it is
/// known: XGrabKey answers at once, the portal only once the bus has answered. Meanwhile the actions asked for are
/// pending, and are handed to the layer when it comes. With no layer at all, each one is unavailable, the log says why,
/// and the user is told once. Pure logic over the layers it is given, so it is tested on every system.
/// </summary>
internal class HotkeyChooser : IHotkeyNative
{
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly Action _announceNone;
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _wanted = [];
    private IHotkeyNative? _layer;
    private bool _deciding = true;
    private bool _disposed;

    /// <param name="openX11">Opens the X11 layer; null when the display cannot be opened.</param>
    /// <param name="openPortal">Opens the portal layer; null when the desktop has none.</param>
    /// <param name="announceNone">Tells the user, once, that no layer can grab a key.</param>
    public HotkeyChooser(Action<string> log, bool wayland, bool xDisplay, Func<IHotkeyNative?> openX11, Func<Task<IHotkeyNative?>> openPortal, Action announceNone)
    {
        _log = log;
        _announceNone = announceNone;
        if (HotkeyChoice.First(wayland, xDisplay) == HotkeyLayer.X11 && Open(openX11) is { } x11)
        {
            Decide(x11);
            Decided = Task.CompletedTask;
            return;
        }

        Decided = DecideAsync(openPortal);
    }

    /// <summary>Done once the layer is known, or known to be none.</summary>
    internal Task Decided { get; }

    public event Action<HotkeyAction>? Pressed;

    public event Action? BindingsChanged;

    /// <summary>The layer in use; null while deciding, or when there is none.</summary>
    public IHotkeyNative? Layer
    {
        get
        {
            lock (_gate)
            {
                return _layer;
            }
        }
    }

    public bool Pending
    {
        get
        {
            lock (_gate)
            {
                return _deciding || (_layer?.Pending ?? false);
            }
        }
    }

    public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
    {
        lock (_gate)
        {
            foreach (var request in requests)
            {
                if (request.Proposed is { } key)
                {
                    _wanted[request.Action] = key;
                }
                else
                {
                    _wanted.Remove(request.Action);
                }
            }

            if (_layer is not null)
            {
                return _layer.Bind(requests);
            }

            return requests.Select(r => Waiting(r.Action)).ToList();
        }
    }

    public HotkeyGesture? BoundKey(HotkeyAction action)
    {
        lock (_gate)
        {
            return _layer?.BoundKey(action);
        }
    }

    public HotkeyBinding Current(HotkeyAction action)
    {
        lock (_gate)
        {
            return _layer?.Current(action) ?? Waiting(action);
        }
    }

    public void Dispose()
    {
        IHotkeyNative? layer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            layer = _layer;
            _layer = null;
        }

        if (layer is not null)
        {
            layer.Pressed -= OnPressed;
            layer.BindingsChanged -= OnChanged;
            layer.Dispose();
        }
    }

    // Without a layer: pending while deciding, unavailable once there is none.
    private HotkeyBinding Waiting(HotkeyAction action) =>
        !_wanted.ContainsKey(action) ? new HotkeyBinding(action, null, HotkeyBindingState.Unbound)
        : new HotkeyBinding(action, null, _deciding && !_disposed ? HotkeyBindingState.Pending : HotkeyBindingState.Unavailable);

    private IHotkeyNative? Open(Func<IHotkeyNative?> open)
    {
        try
        {
            return open();
        }
        catch (Exception ex)
        {
            _log("hotkeys: " + ex.GetType().Name);
            return null;
        }
    }

    private async Task DecideAsync(Func<Task<IHotkeyNative?>> openPortal)
    {
        IHotkeyNative? portal = null;
        try
        {
            portal = await openPortal().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log("hotkeys: the portal failed: " + ex.GetType().Name);
        }

        Decide(portal);
        BindingsChanged?.Invoke();
    }

    private void Decide(IHotkeyNative? layer)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                layer?.Dispose();
                return;
            }

            _deciding = false;
            _layer = layer;
            if (layer is not null)
            {
                layer.Pressed += OnPressed;
                layer.BindingsChanged += OnChanged;

                // What was asked while deciding goes to the layer now, in the order of the actions.
                var asked = _wanted.OrderBy(pair => pair.Key).Select(pair => new HotkeyRequest(pair.Key, pair.Value)).ToList();
                if (asked.Count > 0)
                {
                    layer.Bind(asked);
                }

                return;
            }
        }

        _log("hotkeys: " + HotkeyChoice.NoShortcutText);
        try
        {
            _announceNone();
        }
        catch (Exception ex)
        {
            _log("hotkeys: the notice failed: " + ex.GetType().Name);
        }
    }

    private void OnPressed(HotkeyAction action) => Pressed?.Invoke(action);

    private void OnChanged() => BindingsChanged?.Invoke();
}
