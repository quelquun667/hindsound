namespace Hindsound.Input;

/// <summary>Outcome of one registration attempt at a layer that grabs one combination at a time.</summary>
public enum HotkeyRegistration
{
    /// <summary>The shortcut is registered under the given id.</summary>
    Registered,

    /// <summary>The system refused it (another app holds the combination).</summary>
    Refused,

    /// <summary>
    /// The native layer did not answer in time. The request was cancelled, or undone if it had already run: nothing
    /// stays registered under the id.
    /// </summary>
    TimedOut,
}

/// <summary>Where one action of a batch stands once the shortcut layer has answered.</summary>
public enum HotkeyBindingState
{
    /// <summary>The action has a key: <see cref="HotkeyBinding.Key"/> is the one really bound, which a system may choose itself.</summary>
    Bound,

    /// <summary>Nothing is bound for the action, as asked.</summary>
    Unbound,

    /// <summary>The proposed key is held elsewhere (another app); whatever the action had before stays.</summary>
    Refused,

    /// <summary>The layer did not answer: nothing new is bound for the action.</summary>
    Unavailable,

    /// <summary>The system has not answered yet (a dialog asks the user); <see cref="IHotkeyNative.BindingsChanged"/> tells the end.</summary>
    Pending,
}

/// <summary>One action of a batch and the key proposed for it; null asks for no key at all.</summary>
public readonly record struct HotkeyRequest(HotkeyAction Action, HotkeyGesture? Proposed);

/// <summary>
/// What the layer did with one action of a batch. <paramref name="KeyText"/> is the key as the system words it, for a
/// system that picks the key itself and tells it only as text for people (the portal's trigger description), which cannot
/// be turned into a <see cref="HotkeyGesture"/>; null when <paramref name="Key"/> says it all.
/// </summary>
public readonly record struct HotkeyBinding(HotkeyAction Action, HotkeyGesture? Key, HotkeyBindingState State, string? KeyText = null);

/// <summary>
/// The system's global shortcuts, bound as a batch: the app proposes a key per action, and the system says which key each
/// action really has. A system that asks the user first answers <see cref="HotkeyBindingState.Pending"/> and tells the end
/// with <see cref="BindingsChanged"/>. Called from one thread at a time.
/// </summary>
public interface IHotkeyNative : IDisposable
{
    /// <summary>Raised with the action of a pressed shortcut, on the layer's own thread.</summary>
    event Action<HotkeyAction>? Pressed;

    /// <summary>Raised when the bound keys change: after a batch that changed them, or when the system changes them itself.</summary>
    event Action? BindingsChanged;

    /// <summary>Whether a batch still waits for the system's answer.</summary>
    bool Pending { get; }

    /// <summary>
    /// Binds the listed actions, in their order: a proposed key is bound, a null one is released. Actions left out keep what
    /// they have. Answers one binding per request, in the same order.
    /// </summary>
    IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests);

    /// <summary>The key the action really has now; null when it has none.</summary>
    HotkeyGesture? BoundKey(HotkeyAction action);

    /// <summary>
    /// Where the action stands now, without asking the system for anything: read again after <see cref="BindingsChanged"/>.
    /// The default knows only the bound key; a layer that can be pending, or that words the key itself, overrides it.
    /// </summary>
    HotkeyBinding Current(HotkeyAction action) =>
        BoundKey(action) is { } key
            ? new HotkeyBinding(action, key, HotkeyBindingState.Bound)
            : new HotkeyBinding(action, null, HotkeyBindingState.Unbound);
}

/// <summary>
/// A layer that grabs one combination at a time under an id, as RegisterHotKey does. <see cref="KeyByKeyHotkeys"/> turns it
/// into the batch of <see cref="IHotkeyNative"/>.
/// </summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Raised with the id of a pressed shortcut, on the native layer's own thread.</summary>
    event Action<int>? HotkeyPressed;

    bool Register(int id, uint modifiers, uint virtualKey);

    /// <summary>
    /// Like <see cref="Register"/> but tells a refusal from a timeout. The default maps <see cref="Register"/>, so
    /// existing implementations keep working.
    /// </summary>
    HotkeyRegistration TryRegister(int id, uint modifiers, uint virtualKey) =>
        Register(id, modifiers, virtualKey) ? HotkeyRegistration.Registered : HotkeyRegistration.Refused;

    void Unregister(int id);
}

/// <summary><c>--no-hotkeys</c>: says yes to every shortcut and registers none. Nothing on the keyboard can reach this run.</summary>
public sealed class UnregisteredHotkeys : IHotkeyNative
{
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _bound = [];

    public event Action<HotkeyAction>? Pressed { add { } remove { } }

    public event Action? BindingsChanged { add { } remove { } }

    public bool Pending => false;

    public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
    {
        var answers = new List<HotkeyBinding>(requests.Count);
        foreach (var request in requests)
        {
            if (request.Proposed is { } key)
            {
                _bound[request.Action] = key;
                answers.Add(new HotkeyBinding(request.Action, key, HotkeyBindingState.Bound));
            }
            else
            {
                _bound.Remove(request.Action);
                answers.Add(new HotkeyBinding(request.Action, null, HotkeyBindingState.Unbound));
            }
        }

        return answers;
    }

    public HotkeyGesture? BoundKey(HotkeyAction action) => _bound.TryGetValue(action, out var key) ? key : null;

    public void Dispose()
    {
    }
}
