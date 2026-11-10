using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.Input;

/// <summary>
/// The shortcuts as one batch: the manager proposes a key per action and reads back the key each action really has, whether
/// the layer grabs keys one by one (Windows) or answers later (a system that asks the user first).
/// </summary>
public class HotkeyBatchTests
{
    private static readonly HotkeyGesture F9 = new(0, 0x78);
    private static readonly HotkeyGesture F10 = new(0, 0x79);

    /// <summary>Grabs one combination at a time; a combination in <see cref="Taken"/> belongs to another app.</summary>
    private sealed class OneByOne : IHotkeyRegistrar
    {
        public HashSet<uint> Taken { get; } = [];

        public Dictionary<int, uint> Held { get; } = [];

        public event Action<int>? HotkeyPressed;

        public void Press(int id) => HotkeyPressed?.Invoke(id);

        public bool Register(int id, uint modifiers, uint virtualKey)
        {
            if (Taken.Contains(virtualKey) || Held.ContainsValue(virtualKey))
            {
                return false;
            }

            Held[id] = virtualKey;
            return true;
        }

        public void Unregister(int id) => Held.Remove(id);

        public void Dispose()
        {
        }
    }

    /// <summary>A system that asks the user before it binds anything: every batch waits.</summary>
    private sealed class AsksFirst : IHotkeyNative
    {
        public List<IReadOnlyList<HotkeyRequest>> Batches { get; } = [];

        public event Action<HotkeyAction>? Pressed;

        public event Action? BindingsChanged { add { } remove { } }

        public bool Pending => Batches.Count > 0;

        public void Press(HotkeyAction action) => Pressed?.Invoke(action);

        public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
        {
            Batches.Add(requests);
            return requests.Select(r => new HotkeyBinding(r.Action, null, r.Proposed is null ? HotkeyBindingState.Unbound : HotkeyBindingState.Pending)).ToList();
        }

        public HotkeyGesture? BoundKey(HotkeyAction action) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A system that picks the key itself and words it as text for people. It answers a batch as pending, and settles it
    /// later with <see cref="Settle"/>, as a portal does once its dialog is closed.
    /// </summary>
    private sealed class ChoosesItself : IHotkeyNative
    {
        private readonly Dictionary<HotkeyAction, string> _chosen = [];
        private readonly bool _answersAtOnce;
        private bool _pending;

        public ChoosesItself(bool answersAtOnce) => _answersAtOnce = answersAtOnce;

        public event Action<HotkeyAction>? Pressed { add { } remove { } }

        public event Action? BindingsChanged;

        public bool Pending => _pending;

        public string Choice { get; set; } = "Super+F9";

        public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
        {
            foreach (var request in requests.Where(r => r.Proposed is not null))
            {
                _chosen[request.Action] = Choice;
            }

            _pending = !_answersAtOnce;
            return requests.Select(r => Current(r.Action) with { Action = r.Action }).ToList();
        }

        public void Settle()
        {
            _pending = false;
            BindingsChanged?.Invoke();
        }

        public HotkeyGesture? BoundKey(HotkeyAction action) => null;

        public HotkeyBinding Current(HotkeyAction action) =>
            !_chosen.TryGetValue(action, out var text)
                ? new HotkeyBinding(action, null, HotkeyBindingState.Unbound)
                : _pending
                    ? new HotkeyBinding(action, null, HotkeyBindingState.Pending)
                    : new HotkeyBinding(action, null, HotkeyBindingState.Bound, text);

        public void Dispose()
        {
        }
    }

    [Fact]
    public void A_key_the_system_chose_and_worded_itself_is_the_one_shown()
    {
        using var manager = new HotkeyManager(new ChoosesItself(answersAtOnce: true));

        var result = manager.Apply(new HotkeySettings { SaveDefault = "Ctrl+Alt+S" })[(int)HotkeyAction.SaveDefault];

        Assert.Equal(HotkeyStatus.Registered, result.Status);
        Assert.Equal("Super+F9", result.Gesture);
    }

    [Fact]
    public void A_key_bound_by_a_key_by_key_layer_is_shown_as_it_was_proposed()
    {
        using var manager = new HotkeyManager(new OneByOne());

        var result = manager.Apply(new HotkeySettings { SaveDefault = "F9" })[(int)HotkeyAction.SaveDefault];

        Assert.Equal(HotkeyStatus.Registered, result.Status);
        Assert.Equal(F9.ToString(), result.Gesture);
    }

    [Fact]
    public void An_answer_that_comes_later_is_announced_and_refreshes_the_results_without_binding_again()
    {
        var layer = new ChoosesItself(answersAtOnce: false);
        using var manager = new HotkeyManager(layer);
        var changes = 0;
        manager.ResultsChanged += () => changes++;
        var first = manager.Apply(new HotkeySettings { SaveDefault = "Ctrl+Alt+S" })[(int)HotkeyAction.SaveDefault];
        Assert.Equal(HotkeyStatus.Suspended, first.Status);
        Assert.Equal(0, changes);

        layer.Settle();
        var again = manager.Refresh()[(int)HotkeyAction.SaveDefault];

        Assert.Equal(1, changes);
        Assert.Equal(HotkeyStatus.Registered, again.Status);
        Assert.Equal("Super+F9", again.Gesture);
    }

    [Fact]
    public void A_change_caused_by_our_own_batch_is_not_announced()
    {
        var registrar = new OneByOne();
        using var manager = new HotkeyManager(registrar);
        var changes = 0;
        manager.ResultsChanged += () => changes++;

        manager.Apply(new HotkeySettings { SaveDefault = "F9" });
        manager.Suspend();
        manager.Resume();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_batch_answers_each_request_in_its_order_with_the_key_really_bound()
    {
        using var keys = new KeyByKeyHotkeys(new OneByOne());

        var answers = keys.Bind([new HotkeyRequest(HotkeyAction.Save15s, F9), new HotkeyRequest(HotkeyAction.SaveDefault, F10)]);

        Assert.Equal(
            [new HotkeyBinding(HotkeyAction.Save15s, F9, HotkeyBindingState.Bound), new HotkeyBinding(HotkeyAction.SaveDefault, F10, HotkeyBindingState.Bound)],
            answers);
        Assert.Equal(F9, keys.BoundKey(HotkeyAction.Save15s));
        Assert.Null(keys.BoundKey(HotkeyAction.Save1m));
        Assert.False(keys.Pending);
    }

    [Fact]
    public void A_refused_key_leaves_the_action_with_the_key_it_had()
    {
        var registrar = new OneByOne();
        using var keys = new KeyByKeyHotkeys(registrar);
        keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, F9)]);
        registrar.Taken.Add(F10.VirtualKey);

        var answer = Assert.Single(keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, F10)]));

        Assert.Equal(new HotkeyBinding(HotkeyAction.SaveDefault, F9, HotkeyBindingState.Refused), answer);
        Assert.Equal(F9, keys.BoundKey(HotkeyAction.SaveDefault));
    }

    [Fact]
    public void The_change_event_comes_once_per_batch_that_changes_the_keys_and_never_for_one_that_does_not()
    {
        using var keys = new KeyByKeyHotkeys(new OneByOne());
        var changes = 0;
        keys.BindingsChanged += () => changes++;

        keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, F9), new HotkeyRequest(HotkeyAction.Save15s, F10)]);
        Assert.Equal(1, changes);

        keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, F9)]);
        Assert.Equal(1, changes);

        var released = Assert.Single(keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, null)]));
        Assert.Equal(new HotkeyBinding(HotkeyAction.SaveDefault, null, HotkeyBindingState.Unbound), released);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_press_comes_out_as_the_action_of_its_id()
    {
        var registrar = new OneByOne();
        using var keys = new KeyByKeyHotkeys(registrar);
        keys.Bind([new HotkeyRequest(HotkeyAction.TogglePause, F9)]);
        var pressed = new List<HotkeyAction>();
        keys.Pressed += pressed.Add;

        registrar.Press(registrar.Held.Single().Key);
        registrar.Press(999);

        Assert.Equal([HotkeyAction.TogglePause], pressed);
    }

    [Fact]
    public void A_layer_that_asks_the_user_first_leaves_the_shortcuts_set_without_an_error()
    {
        var layer = new AsksFirst();
        using var manager = new HotkeyManager(layer);

        var results = manager.Apply(new HotkeySettings { SaveDefault = "F9", Save15s = null });

        Assert.Equal(HotkeyStatus.Suspended, results[(int)HotkeyAction.SaveDefault].Status);
        Assert.Null(results[(int)HotkeyAction.SaveDefault].Message);
        Assert.Equal(HotkeyStatus.NotSet, results[(int)HotkeyAction.Save15s].Status);
        Assert.True(layer.Pending);
        Assert.Contains(new HotkeyRequest(HotkeyAction.SaveDefault, F9), Assert.Single(layer.Batches));
    }

    [Fact]
    public async Task A_press_from_a_layer_without_ids_reaches_the_manager()
    {
        var layer = new AsksFirst();
        using var manager = new HotkeyManager(layer);
        var pressed = new TaskCompletionSource<HotkeyAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Pressed += action => pressed.TrySetResult(action);

        layer.Press(HotkeyAction.CopyLast);

        Assert.Same(pressed.Task, await Task.WhenAny(pressed.Task, Task.Delay(5000)));
        Assert.Equal(HotkeyAction.CopyLast, await pressed.Task);
        Assert.False(manager.Dispatch(1));
    }
}
