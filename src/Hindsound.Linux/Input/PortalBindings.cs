using Hindsound.Input;

namespace Hindsound.Linux.Input;

/// <summary>A shortcut as the portal words it: its id, and what the user is told about it.</summary>
internal readonly record struct PortalShortcut(string Id, string? Description, string? TriggerDescription);

/// <summary>One shortcut offered to the portal: its id, the text the user sees, and the key the app proposes.</summary>
internal readonly record struct PortalOffer(string Id, string Description, string? PreferredTrigger);

/// <summary>
/// One BindShortcuts to send: every shortcut the app wants now, as the portal binds a session only once. An empty list
/// only closes the session that holds the old ones.
/// </summary>
internal sealed record PortalBatch(int Generation, IReadOnlyList<PortalOffer> Offers);

/// <summary>
/// What the GlobalShortcuts portal holds for the app, without the bus: the keys asked for, the answer of the last bind,
/// and the batch waiting for the user's answer. Pure logic; the caller holds one lock around it.
/// </summary>
/// <remarks>
/// The portal binds a session's shortcuts once, and the user chooses the keys (the app only proposes them). So every
/// change of the wanted keys sends all of them again, in a new session; the ids stay the same, so the desktop keeps the
/// keys the user gave them. The answer may come much later: until then the actions are pending.
/// </remarks>
internal sealed class PortalBindings
{
    /// <summary>The portal's answer codes (org.freedesktop.portal.Request.Response): 0 success, 1 cancelled, 2 other.</summary>
    public const uint Success = 0;

    private readonly Dictionary<HotkeyAction, HotkeyGesture> _wanted = [];
    private readonly Dictionary<HotkeyAction, HotkeyBinding> _current = [];

    // The wanted keys the live session was (or is being) bound with; null when the next batch must bind again.
    private Dictionary<HotkeyAction, HotkeyGesture>? _sent;
    private int _generation;

    /// <summary>The generation of the batch waiting for its answer; 0 when none waits.</summary>
    public int Waiting { get; private set; }

    public bool Pending => Waiting != 0;

    /// <summary>The id of an action's shortcut. The desktop remembers the user's key under it, so it never changes.</summary>
    public static string IdOf(HotkeyAction action) => action switch
    {
        HotkeyAction.SaveDefault => "save-clip",
        HotkeyAction.Save15s => "save-15s",
        HotkeyAction.Save1m => "save-1m",
        HotkeyAction.Save5m => "save-5m",
        HotkeyAction.TogglePause => "pause",
        HotkeyAction.CopyLast => "copy-last",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static bool TryActionOf(string? id, out HotkeyAction action)
    {
        foreach (var candidate in Enum.GetValues<HotkeyAction>())
        {
            if (IdOf(candidate) == id)
            {
                action = candidate;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>What the desktop shows the user for an action, in its shortcut dialog and settings.</summary>
    public static string DescriptionOf(HotkeyAction action) => action switch
    {
        HotkeyAction.SaveDefault => "Save a clip",
        HotkeyAction.Save15s => "Save the last 15 seconds",
        HotkeyAction.Save1m => "Save the last minute",
        HotkeyAction.Save5m => "Save the last 5 minutes",
        HotkeyAction.TogglePause => "Pause or resume",
        HotkeyAction.CopyLast => "Copy the last clip",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// Takes a batch of <see cref="IHotkeyNative.Bind"/>: the answers to give now, and the portal call to make, if the
    /// wanted keys changed. A released action is unbound at once; a proposed one is pending until the portal answers.
    /// </summary>
    public (IReadOnlyList<HotkeyBinding> Answers, PortalBatch? Batch) Take(IReadOnlyList<HotkeyRequest> requests)
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
                _current.Remove(request.Action);
            }
        }

        PortalBatch? batch = null;
        if (_sent is null || !SameKeys(_sent, _wanted))
        {
            batch = NewBatch();
        }

        return (requests.Select(r => Current(r.Action)).ToList(), batch);
    }

    /// <summary>
    /// The session was lost with the bus connection: the keys it held are gone. The batch that binds them again in a new
    /// session, or null when nothing was wanted.
    /// </summary>
    public PortalBatch? Lost()
    {
        _sent = null;
        return _wanted.Count == 0 ? null : NewBatch();
    }

    /// <summary>
    /// The portal's answer to a batch. False for an answer that came too late (a newer batch was sent since), which
    /// changes nothing. Each wanted shortcut the answer lists is bound; one it leaves out (the user declined it), or every
    /// one when the user cancelled or the call failed, is unavailable, and the next batch asks again.
    /// </summary>
    public bool Answered(int generation, uint code, IReadOnlyList<PortalShortcut>? shortcuts)
    {
        if (generation != Waiting)
        {
            return false;
        }

        Waiting = 0;
        var given = code == Success && shortcuts is not null ? shortcuts.ToDictionary(s => s.Id, StringComparer.Ordinal) : [];
        if (code != Success)
        {
            _sent = null;
        }

        foreach (var action in _wanted.Keys)
        {
            _current[action] = given.TryGetValue(IdOf(action), out var shortcut)
                ? BoundFrom(action, shortcut.TriggerDescription)
                : new HotkeyBinding(action, null, HotkeyBindingState.Unavailable);
        }

        return true;
    }

    /// <summary>The desktop changed keys itself (ShortcutsChanged): the new words of each wanted one it lists. Whether any changed.</summary>
    public bool Changed(IReadOnlyList<PortalShortcut> shortcuts)
    {
        var changed = false;
        foreach (var shortcut in shortcuts)
        {
            if (TryActionOf(shortcut.Id, out var action) && _wanted.ContainsKey(action) && !Pending)
            {
                var binding = BoundFrom(action, shortcut.TriggerDescription);
                if (!_current.TryGetValue(action, out var old) || old != binding)
                {
                    _current[action] = binding;
                    changed = true;
                }
            }
        }

        return changed;
    }

    public HotkeyBinding Current(HotkeyAction action)
    {
        if (!_wanted.ContainsKey(action))
        {
            return new HotkeyBinding(action, null, HotkeyBindingState.Unbound);
        }

        return _current.TryGetValue(action, out var binding) ? binding : new HotkeyBinding(action, null, HotkeyBindingState.Pending);
    }

    /// <summary>The key of an action, when the portal's words for it can be read back as one.</summary>
    public HotkeyGesture? BoundKey(HotkeyAction action) =>
        Current(action) is { State: HotkeyBindingState.Bound, Key: { } key } ? key : null;

    /// <summary>
    /// A bound action from the portal's description of its trigger. Words the app can read back become the key; any other
    /// words (another language, a description that is not a key) are kept as the text people are shown.
    /// </summary>
    internal static HotkeyBinding BoundFrom(HotkeyAction action, string? triggerDescription)
    {
        if (HotkeyKeysyms.TryParseTrigger(triggerDescription, out var key))
        {
            return new HotkeyBinding(action, key, HotkeyBindingState.Bound);
        }

        return new HotkeyBinding(action, null, HotkeyBindingState.Bound, string.IsNullOrWhiteSpace(triggerDescription) ? null : triggerDescription.Trim());
    }

    private PortalBatch NewBatch()
    {
        _generation++;
        _sent = new Dictionary<HotkeyAction, HotkeyGesture>(_wanted);
        var offers = _wanted
            .OrderBy(pair => pair.Key)
            .Select(pair => new PortalOffer(IdOf(pair.Key), DescriptionOf(pair.Key), HotkeyKeysyms.ToPortalTrigger(pair.Value)))
            .ToList();

        // Nothing to wait for when nothing is asked: the session is only closed.
        Waiting = offers.Count == 0 ? 0 : _generation;
        foreach (var action in _wanted.Keys)
        {
            _current.Remove(action);
        }

        return new PortalBatch(_generation, offers);
    }

    private static bool SameKeys(Dictionary<HotkeyAction, HotkeyGesture> a, Dictionary<HotkeyAction, HotkeyGesture> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var key) && key == pair.Value);
}
