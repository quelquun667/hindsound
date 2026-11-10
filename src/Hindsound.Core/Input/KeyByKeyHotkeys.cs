namespace Hindsound.Input;

/// <summary>
/// The batch of <see cref="IHotkeyNative"/> over a layer that grabs one combination at a time under an id (RegisterHotKey).
/// It never loses a combination that works: releases are settled first, then the changes are tried again as long as one
/// succeeds, because a combination freed by one action can be what another one waits for (shifting keys); last, a ring of
/// actions that hold each other's wanted keys (swapping two keys) is exchanged in one step, and put back if any of them fails.
/// Such a layer answers at once, so nothing is ever pending.
/// </summary>
public sealed class KeyByKeyHotkeys : IHotkeyNative
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly Dictionary<HotkeyAction, (int Id, HotkeyGesture Gesture)> _active = [];
    private readonly Dictionary<int, HotkeyAction> _byId = [];
    private readonly object _idLock = new();
    private int _nextId = 1;
    private bool _disposed;

    public KeyByKeyHotkeys(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.HotkeyPressed += OnRegistrarPressed;
    }

    public event Action<HotkeyAction>? Pressed;

    public event Action? BindingsChanged;

    /// <summary>The layer underneath, for a test that checks which one a run got.</summary>
    public IHotkeyRegistrar Registrar => _registrar;

    public bool Pending => false;

    public HotkeyGesture? BoundKey(HotkeyAction action) => _active.TryGetValue(action, out var entry) ? entry.Gesture : null;

    /// <summary>The action registered under an id of the layer, while it is registered.</summary>
    public bool TryGetAction(int id, out HotkeyAction action)
    {
        lock (_idLock)
        {
            return _byId.TryGetValue(id, out action);
        }
    }

    public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var before = _active.ToDictionary(kv => kv.Key, kv => kv.Value.Gesture);
        var results = new Dictionary<HotkeyAction, HotkeyBinding>();
        var pending = new List<(HotkeyAction Action, HotkeyGesture Gesture)>();
        foreach (var request in requests.Where(r => r.Proposed is null))
        {
            Release(request.Action);
            results[request.Action] = new HotkeyBinding(request.Action, null, HotkeyBindingState.Unbound);
        }

        foreach (var request in requests)
        {
            if (request.Proposed is not { } gesture)
            {
                continue;
            }

            if (_active.TryGetValue(request.Action, out var current) && current.Gesture == gesture)
            {
                results[request.Action] = Bound(request.Action, gesture);
            }
            else
            {
                pending.Add((request.Action, gesture));
            }
        }

        var timedOut = false;
        bool progress;
        do
        {
            progress = false;
            foreach (var wish in pending.ToList())
            {
                var outcome = TryBind(wish.Action, wish.Gesture);
                if (outcome == HotkeyRegistration.Refused)
                {
                    continue;
                }

                pending.Remove(wish);
                progress = true;
                results[wish.Action] = outcome == HotkeyRegistration.Registered
                    ? Bound(wish.Action, wish.Gesture)
                    : new HotkeyBinding(wish.Action, BoundKey(wish.Action), HotkeyBindingState.Unavailable);
                timedOut |= outcome == HotkeyRegistration.TimedOut;
            }
        }
        while (progress && pending.Count > 0 && !timedOut);

        if (timedOut)
        {
            // The hotkey thread is stuck: do not wait for it again once per remaining action.
            foreach (var wish in pending)
            {
                results[wish.Action] = new HotkeyBinding(wish.Action, BoundKey(wish.Action), HotkeyBindingState.Unavailable);
            }

            pending.Clear();
        }
        else
        {
            var ring = FindRing(pending);
            if (ring.Count > 1 && TryExchange(ring))
            {
                foreach (var wish in ring)
                {
                    pending.Remove(wish);
                    results[wish.Action] = Bound(wish.Action, wish.Gesture);
                }
            }
        }

        foreach (var wish in pending)
        {
            results[wish.Action] = new HotkeyBinding(wish.Action, BoundKey(wish.Action), HotkeyBindingState.Refused);
        }

        if (before.Count != _active.Count || before.Any(kv => BoundKey(kv.Key) != kv.Value))
        {
            BindingsChanged?.Invoke();
        }

        return requests.Select(r => results[r.Action]).ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registrar.HotkeyPressed -= OnRegistrarPressed;
        _registrar.Dispose();
    }

    private static HotkeyBinding Bound(HotkeyAction action, HotkeyGesture gesture) => new(action, gesture, HotkeyBindingState.Bound);

    // The id is resolved here, on the hotkey thread, while it is still known.
    private void OnRegistrarPressed(int id)
    {
        if (TryGetAction(id, out var action))
        {
            Pressed?.Invoke(action);
        }
    }

    /// <summary>
    /// The actions that wait for each other: each wants a combination another one of them holds right now, so no
    /// order of single changes can satisfy them. Anything that waits on something outside (another app) is left out.
    /// </summary>
    private List<(HotkeyAction Action, HotkeyGesture Gesture)> FindRing(List<(HotkeyAction Action, HotkeyGesture Gesture)> pending)
    {
        var ring = pending.ToList();
        bool removed;
        do
        {
            removed = ring.RemoveAll(w => !ring.Any(o => o != w && _active.TryGetValue(o.Action, out var held) && held.Gesture == w.Gesture)) > 0;
        }
        while (removed);

        return ring;
    }

    /// <summary>
    /// Releases every action of the ring, registers the wanted combinations, and if any one is refused puts every
    /// previous registration back, so the exchange is all or nothing.
    /// </summary>
    private bool TryExchange(List<(HotkeyAction Action, HotkeyGesture Gesture)> ring)
    {
        var previous = ring.ToDictionary(w => w.Action, w => _active[w.Action].Gesture);
        foreach (var wish in ring)
        {
            Release(wish.Action);
        }

        var bound = new List<HotkeyAction>();
        var ok = true;
        foreach (var wish in ring)
        {
            if (TryBind(wish.Action, wish.Gesture) != HotkeyRegistration.Registered)
            {
                ok = false;
                break;
            }

            bound.Add(wish.Action);
        }

        if (ok)
        {
            return true;
        }

        foreach (var action in bound)
        {
            Release(action);
        }

        foreach (var wish in ring)
        {
            TryBind(wish.Action, previous[wish.Action]);
        }

        return false;
    }

    /// <summary>Registers a combination for the action; the action's old one is released only once the new one is in place.</summary>
    private HotkeyRegistration TryBind(HotkeyAction action, HotkeyGesture gesture)
    {
        var id = _nextId++;
        // The id must be known before registering: a press can arrive on the hotkey thread right after.
        lock (_idLock)
        {
            _byId[id] = action;
        }

        var outcome = _registrar.TryRegister(id, gesture.Modifiers, gesture.VirtualKey);
        if (outcome != HotkeyRegistration.Registered)
        {
            lock (_idLock)
            {
                _byId.Remove(id);
            }

            // A timeout is not another app holding the combination, and the native layer left nothing registered.
            return outcome;
        }

        Release(action);
        _active[action] = (id, gesture);
        return HotkeyRegistration.Registered;
    }

    private void Release(HotkeyAction action)
    {
        if (_active.Remove(action, out var entry))
        {
            _registrar.Unregister(entry.Id);
            lock (_idLock)
            {
                _byId.Remove(entry.Id);
            }
        }
    }
}
