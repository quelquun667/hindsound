using Hindsound.Settings;

namespace Hindsound.Input;

/// <summary>
/// Owns every global shortcut: reads them from the settings, binds them as one batch through the platform's layer, and says
/// for each one what came of it. Apply, Suspend and Resume are meant to be called from one thread at a time (the UI thread).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private static readonly HotkeyAction[] Actions = Enum.GetValues<HotkeyAction>();

    private readonly IHotkeyNative _native;
    private readonly Dictionary<HotkeyAction, string?> _wanted = [];
    private bool _suspended;
    private bool _disposed;
    private int _syncThread;

    /// <summary>Real manager: the platform's own shortcuts, which start their thread.</summary>
    public HotkeyManager()
        : this(App.PlatformDefaults.Make(App.PlatformDefaults.Hotkeys, "global shortcuts"))
    {
    }

    /// <summary>Manager over a native layer: the platform's own, from <see cref="App.AppOptions.NewHotkeyLayer"/>, or a fake in tests.</summary>
    public HotkeyManager(IHotkeyNative native)
    {
        _native = native;
        _native.Pressed += OnNativePressed;
        _native.BindingsChanged += OnNativeChanged;
    }

    /// <summary>Manager over a layer that grabs one combination at a time, made into a batch by <see cref="KeyByKeyHotkeys"/>.</summary>
    public HotkeyManager(IHotkeyRegistrar registrar)
        : this(new KeyByKeyHotkeys(registrar))
    {
    }

    /// <summary>
    /// Raised when a registered shortcut is pressed. It is raised on the hotkey thread, not the UI thread:
    /// the handler must switch back to its own UI thread (for example Dispatcher.BeginInvoke) before touching any UI.
    /// </summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>
    /// Raised when the system changed the bound keys on its own, or answered a batch it had left pending (the user accepted a
    /// dialog): <see cref="Refresh"/> gives the new results. It may be raised on the layer's own thread, so the handler must
    /// switch back to the UI thread before calling <see cref="Refresh"/>. A change that one of our own calls caused is not
    /// raised: that call returns its results itself.
    /// </summary>
    public event Action? ResultsChanged;

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
        ReleaseAll();
    }

    /// <summary>Registers the shortcuts again after <see cref="Suspend"/>.</summary>
    public IReadOnlyList<HotkeyResult> Resume()
    {
        ThrowIfDisposed();
        _suspended = false;
        return Sync();
    }

    /// <summary>
    /// What the shortcuts come to now, from what the layer says without binding anything again: the same results as
    /// <see cref="Apply"/>, for the wanted shortcuts as they are in the system.
    /// </summary>
    public IReadOnlyList<HotkeyResult> Refresh()
    {
        ThrowIfDisposed();
        return Sync(bind: false);
    }

    /// <summary>
    /// Handles a pressed shortcut id of a key-by-key layer; returns true if it is one of ours. <see cref="Pressed"/> is raised
    /// on the calling thread, each subscriber on its own: one that throws is logged and does not stop the next, and nothing
    /// propagates to the caller. A layer without ids has nothing to resolve: false.
    /// </summary>
    public bool Dispatch(int id)
    {
        if (_native is not KeyByKeyHotkeys keys || !keys.TryGetAction(id, out var action))
        {
            return false;
        }

        RaisePressed(action);
        return true;
    }

    // The handlers run on the thread pool, so that a slow one never keeps the layer's thread from reading the next press.
    private void OnNativePressed(HotkeyAction action) =>
        ThreadPool.QueueUserWorkItem(static state => state.Manager.RaisePressed(state.Action), (Manager: this, Action: action), preferLocal: false);

    private void OnNativeChanged()
    {
        // Raised inside our own Bind, on this thread: the call that is running returns the results.
        if (Volatile.Read(ref _syncThread) == Environment.CurrentManagedThreadId)
        {
            return;
        }

        ResultsChanged?.Invoke();
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
        ReleaseAll();
        _native.Pressed -= OnNativePressed;
        _native.BindingsChanged -= OnNativeChanged;
        _native.Dispose();
    }

    private sealed record Wish(HotkeyAction Action, HotkeyGesture Gesture, string Text);

    /// <summary>
    /// Brings the bound shortcuts to the wanted ones without ever losing one that works: removed and invalid ones are settled
    /// first; a shortcut that appears twice in the settings is flagged (the one already in place keeps it); the rest goes to the
    /// layer as one batch, and what it answers for each action becomes that action's state.
    /// </summary>
    private List<HotkeyResult> Sync(bool bind = true)
    {
        var results = new Dictionary<HotkeyAction, HotkeyResult>();
        var requests = new List<HotkeyRequest>();
        var wishes = new List<Wish>();
        foreach (var action in Actions)
        {
            _wanted.TryGetValue(action, out var text);
            if (string.IsNullOrWhiteSpace(text))
            {
                requests.Add(new HotkeyRequest(action, null));
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
        requests.AddRange(wishes.Select(w => new HotkeyRequest(w.Action, w.Gesture)));

        // A refresh binds nothing: it reads again what the layer holds for the wanted shortcuts.
        var answers = bind
            ? BindBatch(requests).ToDictionary(b => b.Action)
            : wishes.ToDictionary(w => w.Action, w => _native.Current(w.Action));
        foreach (var wish in wishes)
        {
            var answer = answers[wish.Action];
            results[wish.Action] = answer.State switch
            {
                // The key the system really gave, in its own words when it has them, as it may not be the one proposed.
                HotkeyBindingState.Bound => new HotkeyResult(wish.Action, answer.KeyText ?? answer.Key?.ToString() ?? wish.Text, HotkeyStatus.Registered, null),
                HotkeyBindingState.Refused => new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.InUse, HotkeyResult.InUseMessage),

                // Set, not in force yet, and no error to show while the system asks the user.
                HotkeyBindingState.Pending => new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.Suspended, null),
                _ => new HotkeyResult(wish.Action, wish.Text, HotkeyStatus.Unavailable, HotkeyResult.UnavailableMessage),
            };
        }

        return Actions.Select(a => results[a]).ToList();
    }

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
            var winner = members.FirstOrDefault(w => _native.BoundKey(w.Action) == w.Gesture) ?? members[0];
            kept.Add(winner);
            foreach (var loser in members.Where(w => w != winner))
            {
                results[loser.Action] = new HotkeyResult(loser.Action, loser.Text, HotkeyStatus.Duplicate, HotkeyResult.DuplicateMessage);
            }
        }

        return kept.OrderBy(w => w.Action).ToList();
    }

    private void ReleaseAll()
    {
        var bound = Actions.Where(a => _native.BoundKey(a) is not null).Select(a => new HotkeyRequest(a, null)).ToList();
        if (bound.Count > 0)
        {
            BindBatch(bound);
        }
    }

    // The layer may raise its change event inside Bind, on this thread; OnNativeChanged tells it from a later one.
    private IReadOnlyList<HotkeyBinding> BindBatch(IReadOnlyList<HotkeyRequest> requests)
    {
        Volatile.Write(ref _syncThread, Environment.CurrentManagedThreadId);
        try
        {
            return _native.Bind(requests);
        }
        finally
        {
            Volatile.Write(ref _syncThread, 0);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
