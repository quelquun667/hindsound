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

    private List<HotkeyResult> Sync()
    {
        var results = new List<HotkeyResult>();
        foreach (var action in Actions)
        {
            _wanted.TryGetValue(action, out var text);
            results.Add(SyncOne(action, text));
        }

        return results;
    }

    private HotkeyResult SyncOne(HotkeyAction action, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Release(action);
            return new HotkeyResult(action, null, HotkeyStatus.NotSet, null);
        }

        if (!HotkeyGesture.TryParse(text, out var gesture))
        {
            // Keep whatever worked before: a typo must not silently kill a working shortcut.
            return new HotkeyResult(action, text, HotkeyStatus.Invalid, HotkeyResult.InvalidMessage);
        }

        var canonical = gesture.ToString();
        if (_suspended)
        {
            return new HotkeyResult(action, canonical, HotkeyStatus.Suspended, null);
        }

        if (_active.TryGetValue(action, out var current) && current.Gesture == gesture)
        {
            return new HotkeyResult(action, canonical, HotkeyStatus.Registered, null);
        }

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
            return outcome == HotkeyRegistration.TimedOut
                ? new HotkeyResult(action, canonical, HotkeyStatus.Unavailable, HotkeyResult.UnavailableMessage)
                : new HotkeyResult(action, canonical, HotkeyStatus.InUse, HotkeyResult.InUseMessage);
        }

        Release(action);
        _active[action] = (id, gesture);
        return new HotkeyResult(action, canonical, HotkeyStatus.Registered, null);
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
