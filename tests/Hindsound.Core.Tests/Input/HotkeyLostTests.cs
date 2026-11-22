using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.Input;

/// <summary>
/// A layer that can lose a shortcut it had registered (macOS moves a letter's key with the keyboard layout, and the new key
/// may belong to another app) must not be believed bound: the page shows it as in use, and a new batch tries it again.
/// </summary>
public class HotkeyLostTests
{
    /// <summary>Registers everything; <see cref="Lose"/> and <see cref="Regain"/> play what the layer does by itself.</summary>
    private sealed class Losing : IHotkeyRegistrar
    {
        private readonly HashSet<int> _lost = [];

        public List<int> Registered { get; } = [];

        public event Action<int>? HotkeyPressed { add { } remove { } }

        public event Action? HeldChanged;

        public bool Register(int id, uint modifiers, uint virtualKey)
        {
            Registered.Add(id);
            return true;
        }

        public void Unregister(int id)
        {
            Registered.Remove(id);
            _lost.Remove(id);
        }

        public bool IsHeld(int id) => !_lost.Contains(id);

        public void Lose(int id)
        {
            _lost.Add(id);
            HeldChanged?.Invoke();
        }

        public void Regain(int id)
        {
            _lost.Remove(id);
            HeldChanged?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    private static readonly HotkeyGesture F9 = new(0, 0x78);

    private static HotkeyRequest[] Wish() => [new(HotkeyAction.SaveDefault, F9)];

    [Fact]
    public void A_shortcut_the_layer_lost_is_refused_and_the_change_is_told_then_it_is_bound_again_when_regained()
    {
        var layer = new Losing();
        using var keys = new KeyByKeyHotkeys(layer);
        var changes = 0;
        keys.BindingsChanged += () => changes++;
        Assert.Equal(HotkeyBindingState.Bound, keys.Bind(Wish())[0].State);
        changes = 0;

        layer.Lose(layer.Registered.Single());

        Assert.Equal(1, changes);
        var lost = keys.Current(HotkeyAction.SaveDefault);
        Assert.Equal(HotkeyBindingState.Refused, lost.State);
        Assert.Equal(F9, lost.Key);

        layer.Regain(layer.Registered.Single());

        Assert.Equal(2, changes);
        Assert.Equal(HotkeyBindingState.Bound, keys.Current(HotkeyAction.SaveDefault).State);
    }

    [Fact]
    public void The_settings_page_shows_a_lost_shortcut_as_in_use_and_not_registered()
    {
        var layer = new Losing();
        using var manager = new HotkeyManager(layer);
        var settings = new HotkeySettings { SaveDefault = F9.ToString() };
        Assert.Equal(HotkeyStatus.Registered, manager.Apply(settings)[(int)HotkeyAction.SaveDefault].Status);

        layer.Lose(layer.Registered.Single());

        Assert.Equal(HotkeyStatus.InUse, manager.Refresh()[(int)HotkeyAction.SaveDefault].Status);
    }

    [Fact]
    public void A_new_batch_with_the_same_key_tries_a_lost_shortcut_again_and_lets_the_waiting_one_go()
    {
        var layer = new Losing();
        using var keys = new KeyByKeyHotkeys(layer);
        keys.Bind(Wish());
        var first = layer.Registered.Single();
        layer.Lose(first);

        var again = keys.Bind(Wish());

        Assert.Equal(HotkeyBindingState.Bound, again[0].State);
        var second = Assert.Single(layer.Registered);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Releasing_a_lost_shortcut_unregisters_it()
    {
        var layer = new Losing();
        using var keys = new KeyByKeyHotkeys(layer);
        keys.Bind(Wish());
        layer.Lose(layer.Registered.Single());

        keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, null)]);

        Assert.Empty(layer.Registered);
        Assert.Equal(HotkeyBindingState.Unbound, keys.Current(HotkeyAction.SaveDefault).State);
    }

    [Fact]
    public void A_layer_that_never_loses_anything_is_held_everywhere_by_default()
    {
        IHotkeyRegistrar viaInterface = new Defaults();
        Assert.True(viaInterface.IsHeld(7));
    }

    private sealed class Defaults : IHotkeyRegistrar
    {
        public event Action<int>? HotkeyPressed { add { } remove { } }

        public bool Register(int id, uint modifiers, uint virtualKey) => true;

        public void Unregister(int id)
        {
        }

        public void Dispose()
        {
        }
    }
}
