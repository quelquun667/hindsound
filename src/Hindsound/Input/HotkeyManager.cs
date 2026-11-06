using Hindsound.Settings;

namespace Hindsound.Input;

/// <summary>
/// Owns every global shortcut. No window and no UI framework: a dedicated background thread registers them with
/// RegisterHotKey (MOD_NOREPEAT) and reads WM_HOTKEY from its own message queue.
/// Apply, Suspend and Resume are meant to be called from one thread at a time (the UI thread).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private static readonly HotkeyAction[] Actions = Enum.GetValues<HotkeyAction>();

    private readonly IHotkeyNative _native;
    private readonly Dictionary<HotkeyAction, string?> _wanted = [];
    private readonly Dictionary<HotkeyAction, (int Id, HotkeyGesture Gesture)> _active = [];
    private readonly Dictionary<int, HotkeyAction> _byId = [];
    private readonly object _idLock = new();
    private int _nextId = 1;
    private bool _suspended;
    private bool _disposed;

    /// <summary>Real manager: starts the hotkey thread.</summary>
    public HotkeyManager()
        : this(new Win32HotkeyNative())
    {
    }

    /// <summary>Manager over a supplied native layer (tests).</summary>
    public HotkeyManager(IHotkeyNative native)
    {
        _native = native;
        _native.HotkeyPressed += OnNativePressed;
    }

    /// <summary>
    /// Raised when a registered shortcut is pressed. It is raised on the hotkey thread, not the UI thread:
    /// the handler must switch back to its own UI thread (for example Dispatcher.BeginInvoke) before touching any UI.
    /// </summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>What the shortcuts come to when the manager could not even be created: every one that is set does not work.</summary>
    public static IReadOnlyList<HotkeyResult> WithoutManager(HotkeySettings settings) => Actions.Select(action => Without(action, Gesture(settings, action))).ToList();

    private static HotkeyResult Without(HotkeyAction action, string? gesture) =>
        string.IsNullOrWhiteSpace(gesture)
            ? new HotkeyResult(action, null, HotkeyStatus.NotSet, null)
            : new HotkeyResult(action, gesture, HotkeyStatus.Unavailable, HotkeyResult.UnavailableMessage);

    private static string? Gesture(HotkeySettings settings, HotkeyAction action) => action switch
    {
        HotkeyAction.SaveDefault => settings.SaveDefault,
        HotkeyAction.Save15s => settings.Save15s,
        HotkeyAction.Save1m => settings.Save1m,
        HotkeyAction.Save5m => settings.Save5m,
        HotkeyAction.TogglePause => settings.TogglePause,
        HotkeyAction.CopyLast => settings.CopyLast,
        _ => null,
    };

    /// <summary>Registers the shortcuts of these settings. An old shortcut is only released once its replacement is in place.</summary>
    public IReadOnlyList<HotkeyResult> Apply(HotkeySettings settings)
    {
        ThrowIfDisposed();
        _wanted[HotkeyAction.SaveDefault] = settings.SaveDefault;
        _wanted[HotkeyAction.Save15s] = settings.Save15s;
        _wanted[HotkeyAction.Save1m] = settings.Save1m;
        _wanted[HotkeyAction.Save5m] = settings.Save5m;
        _wanted[HotkeyAction.TogglePause] = settings.TogglePause;
        _wanted[HotkeyAction.CopyLast] = settings.CopyLast;
        return Sync();
    }

    /// <summary>Releases every shortcut while the user types a new one in the settings.</summary>
    public void Suspend()
    {
        ThrowIfDisposed();
        _suspended = true;
        foreach (var action in _active.Keys.ToList())
        {
            Release(action);
        }
    }

    /// <summary>Registers the shortcuts again after <see cref="Suspend"/>.</summary>
    public IReadOnlyList<HotkeyResult> Resume()
    {
        ThrowIfDisposed();
        _suspended = false;
        return Sync();
    }

    /// <summary>
    /// Handles a pressed shortcut id; returns true if it is one of ours. <see cref="Pressed"/> is raised on the calling
    /// thread, each subscriber on its own: one that throws is logged and does not stop the next, and nothing
    /// propagates to the caller.
    /// </summary>
    public bool Dispatch(int id)
    {
        if (!TryGetAction(id, out var action))
        {
            return false;
        }

        RaisePressed(action);
        return true;
    }

    private bool TryGetAction(int id, out HotkeyAction action)
    {
        lock (_idLock)
        {
            return _byId.TryGetValue(id, out action);
        }
    }

    // The id is resolved here, on the hotkey thread, while it is still known; the handlers run on the thread pool so
    // that a slow one never keeps the message loop from reading the next press.
    private void OnNativePressed(int id)
    {
        if (TryGetAction(id, out var action))
        {
            ThreadPool.QueueUserWorkItem(static state => state.Manager.RaisePressed(state.Action), (Manager: this, Action: action), preferLocal: false);
        }
    }

    private void RaisePressed(HotkeyAction action)
    {
        var handlers = Pressed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<HotkeyAction>>())
        {
            try
            {
                handler(action);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Hindsound hotkey handler failed: " + ex);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var action in _active.Keys.ToList())
        {
            Release(action);
        }

        _native.HotkeyPressed -= OnNativePressed;
        _native.Dispose();
    }

    private sealed record Wish(HotkeyAction Action, HotkeyGesture Gesture, string Text);

    /// <summary>
    /// Brings the registered shortcuts to the wanted ones without ever losing one that works:
    /// removed and invalid ones are settled first; a shortcut that appears twice in the settings is flagged (the one
    /// already in place keeps it); then the changes are tried again as long as one succeeds, because a shortcut freed
    /// by one action can be what another one waits for (shifting keys); last, a ring of actions that hold each
    /// other's wanted keys (swapping two keys) is exchanged in one step, and put back if any of them fails.
    /// </summary>
    private List<HotkeyResult> Sync()
    {
        var results = new Dictionary<HotkeyAction, HotkeyResult>();
        var wishes = new List<Wish>();
        foreach (var action in Actions)
        {
            _wanted.TryGetValue(action, out var text);
            if (string.IsNullOrWhiteSpace(text))
            {
                Release(action);
                results[action] = new HotkeyResult(action, null, HotkeyStatus.NotSet, null);
            }
            else if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                // Keep whatever worked before: a typo must not silently kill a working shortcut.
                results[action] = new HotkeyResult(action, text, HotkeyStatus.Invalid, HotkeyResult.InvalidMessage);
            }
            else if (_suspended)
            {
                results[action] = new HotkeyResult(action, gesture.ToString(), HotkeyStatus.Suspended, null);
            }
            else
            {
                wishes.Add(new Wish(action, gesture, gesture.ToString()));
            }
        }

        wishes = FlagDuplicates(wishes, results);

        var pending = new List<Wish>();
        foreach (var wish in wishes)
        {
            if (_active.TryGetValue(wish.Action, out var current) && current.Gesture == wish.Gesture)
            {
                results[wish.Action] = new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.Registered, null);
            }
            else
            {
                pending.Add(wish);
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
                    ? new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.Registered, null)
                    : Unavailable(wish);
                timedOut |= outcome == HotkeyRegistration.TimedOut;
            }
        }
        while (progress && pending.Count > 0 && !timedOut);

        if (timedOut)
        {
            // The hotkey thread is stuck: do not wait for it again once per remaining action.
            foreach (var wish in pending)
            {
                results[wish.Action] = Unavailable(wish);
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
                    results[wish.Action] = new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.Registered, null);
                }
            }
        }

        foreach (var wish in pending)
        {
            results[wish.Action] = new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.InUse, HotkeyResult.InUseMessage);
        }

        return Actions.Select(a => results[a]).ToList();
    }

    private static HotkeyResult Unavailable(Wish wish) =>
        new(wish.Action, wish.Text, HotkeyStatus.Unavailable, HotkeyResult.UnavailableMessage);

    /// <summary>
    /// Two actions asking for the same combination cannot both have it. The one that holds it already keeps it,
    /// otherwise the first in the enum order; the others are reported as <see cref="HotkeyStatus.Duplicate"/> and keep
    /// whatever they had.
    /// </summary>
    private List<Wish> FlagDuplicates(List<Wish> wishes, Dictionary<HotkeyAction, HotkeyResult> results)
    {
        var kept = new List<Wish>();
        foreach (var group in wishes.GroupBy(w => w.Gesture))
        {
            var members = group.ToList();
            var winner = members.FirstOrDefault(w => _active.TryGetValue(w.Action, out var current) && current.Gesture == w.Gesture)
                ?? members[0];
            kept.Add(winner);
            foreach (var loser in members.Where(w => w != winner))
            {
                results[loser.Action] = new HotkeyResult(loser.Action, loser.Text, HotkeyStatus.Duplicate, HotkeyResult.DuplicateMessage);
            }
        }

        return kept.OrderBy(w => w.Action).ToList();
    }

    /// <summary>
    /// The actions that wait for each other: each wants a combination another one of them holds right now, so no
    /// order of single changes can satisfy them. Anything that waits on something outside (another app) is left out.
    /// </summary>
    private List<Wish> FindRing(List<Wish> pending)
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
    private bool TryExchange(List<Wish> ring)
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

        var outcome = _native.TryRegister(id, gesture.Modifiers, gesture.VirtualKey);
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
            _native.Unregister(entry.Id);
            lock (_idLock)
            {
                _byId.Remove(entry.Id);
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
