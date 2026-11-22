using Hindsound.Mac.Input;

namespace Hindsound.Mac.Tests.Input;

/// <summary>
/// What a change of keyboard layout does to the registered shortcuts, over a fake table of registrations in which another
/// app can hold a combination. Pure: runs on every system.
/// </summary>
public sealed class MacHotkeyTableTests
{
    private const uint Mods = 3;

    // Where two letters sit on a US keyboard (L, P) and on a Dvorak one, which moves L onto the key P leaves and P onto a key
    // no shortcut of the app holds.
    private static readonly MacKeyLayout Us = Layout(("L", 0x25), ("P", 0x23));
    private static readonly MacKeyLayout Dvorak = Layout(("L", 0x23), ("P", 0x0F));

    // The registrations of the system: the app's (by reference) and those of another app (Outside).
    private sealed class Registrations
    {
        private readonly Dictionary<nint, (ushort Code, uint Modifiers)> _held = [];
        private nint _next = 1;

        public HashSet<(ushort Code, uint Modifiers)> Outside { get; } = [];

        public int Frees { get; private set; }

        public List<string> Log { get; } = [];

        public int Held => _held.Count;

        public nint? Hold(ushort code, uint modifiers, int id)
        {
            if (Outside.Contains((code, modifiers)) || _held.ContainsValue((code, modifiers)))
            {
                return null;
            }

            _held[_next] = (code, modifiers);
            return _next++;
        }

        public void Free(nint reference)
        {
            Assert.True(_held.Remove(reference), "a reference is freed once");
            Frees++;
        }

        public MacHotkeyTable Table() => new(Hold, Free, Log.Add);
    }

    private static MacKeyLayout Layout(params (string Letter, ushort Code)[] keys) =>
        MacKeyLayout.From("test", (code, shift) => !shift && keys.FirstOrDefault(k => k.Code == code).Letter is { } letter ? letter.ToLowerInvariant() : null);

    private static (Registrations Registrations, MacHotkeyTable Table) Opened()
    {
        var system = new Registrations();
        var table = system.Table();
        table.Use(Us);
        Assert.True(table.Add(1, Mods, 'L', 0x25));
        Assert.True(table.Add(2, Mods, 'P', 0x23));
        return (system, table);
    }

    [Fact]
    public void A_shortcut_whose_new_key_is_held_by_another_app_and_whose_old_key_went_to_another_shortcut_is_kept_in_waiting()
    {
        var (system, table) = Opened();
        system.Outside.Add((0x0F, Mods));

        var moved = table.Relayout(Dvorak, out var heldChanged);

        Assert.Equal(1, moved);
        Assert.True(heldChanged);
        Assert.Equal([1, 2], table.Ids.Order());
        Assert.True(table.IsHeld(1));
        Assert.Equal((ushort)0x23, table.CodeOf(1));
        Assert.False(table.IsHeld(2));
        Assert.Equal((ushort)0x0F, table.CodeOf(2));
        Assert.Equal(1, system.Held);
        Assert.Contains(system.Log, line => line.Contains("2 waiting"));
        Assert.DoesNotContain(system.Log, line => line.Contains("lost"));
    }

    [Fact]
    public void A_shortcut_in_waiting_is_registered_again_when_the_layout_changes_back()
    {
        var (system, table) = Opened();
        system.Outside.Add((0x0F, Mods));
        table.Relayout(Dvorak, out _);

        table.Relayout(Us, out var heldChanged);

        Assert.True(heldChanged);
        Assert.True(table.IsHeld(1));
        Assert.True(table.IsHeld(2));
        Assert.Equal((ushort)0x25, table.CodeOf(1));
        Assert.Equal((ushort)0x23, table.CodeOf(2));
        Assert.Equal(2, system.Held);
        Assert.Contains(system.Log, line => line.Contains("2 works again"));
    }

    [Fact]
    public void A_shortcut_in_waiting_is_tried_again_at_every_change_and_the_Core_is_told_only_when_it_works_again()
    {
        var (system, table) = Opened();
        system.Outside.Add((0x0F, Mods));
        table.Relayout(Dvorak, out _);

        table.Relayout(Dvorak, out var stillWaiting);
        Assert.False(stillWaiting);
        Assert.False(table.IsHeld(2));
        Assert.Contains(system.Log, line => line.Contains("2 still waiting"));

        system.Outside.Clear();
        table.Relayout(Dvorak, out var worksAgain);

        Assert.True(worksAgain);
        Assert.True(table.IsHeld(2));
        Assert.Equal((ushort)0x0F, table.CodeOf(2));
    }

    [Fact]
    public void A_shortcut_in_waiting_is_let_go_with_nothing_to_release()
    {
        var (system, table) = Opened();
        system.Outside.Add((0x0F, Mods));
        table.Relayout(Dvorak, out _);
        var freed = system.Frees;

        table.Remove(2);

        Assert.Equal(freed, system.Frees);
        Assert.Equal([1], table.Ids);
        Assert.False(table.IsHeld(2));
        Assert.Null(table.CodeOf(2));
    }

    [Fact]
    public void Clearing_the_table_releases_only_what_is_registered()
    {
        var (system, table) = Opened();
        system.Outside.Add((0x0F, Mods));
        table.Relayout(Dvorak, out _);

        table.Clear();

        Assert.Equal(0, system.Held);
        Assert.Empty(table.Ids);
    }

    [Fact]
    public void A_shortcut_refused_on_its_new_key_goes_back_to_its_old_one_when_that_one_is_free()
    {
        var system = new Registrations();
        var table = system.Table();
        table.Use(Us);
        Assert.True(table.Add(1, Mods, 'P', 0x23));
        system.Outside.Add((0x0F, Mods));

        var moved = table.Relayout(Dvorak, out var heldChanged);

        Assert.Equal(0, moved);
        Assert.False(heldChanged);
        Assert.True(table.IsHeld(1));
        Assert.Equal((ushort)0x23, table.CodeOf(1));
        Assert.DoesNotContain(system.Log, line => line.Contains("waiting"));
    }

    [Fact]
    public void Two_shortcuts_that_trade_keys_both_find_theirs_free_whatever_the_order()
    {
        var system = new Registrations();
        var table = system.Table();
        var qwerty = Layout(("A", 0x00), ("Q", 0x0C));
        var azerty = Layout(("A", 0x0C), ("Q", 0x00));
        table.Use(qwerty);
        Assert.True(table.Add(5, Mods, 'Q', 0x0C));
        Assert.True(table.Add(3, Mods, 'A', 0x00));

        var moved = table.Relayout(azerty, out var heldChanged);

        Assert.Equal(2, moved);
        Assert.False(heldChanged);
        Assert.Equal((ushort)0x0C, table.CodeOf(3));
        Assert.Equal((ushort)0x00, table.CodeOf(5));
    }

    [Fact]
    public void A_change_that_comes_off_the_main_thread_touches_nothing_and_says_so()
    {
        var (system, table) = Opened();
        var read = 0;

        var taken = table.Retake(false, () =>
        {
            read++;
            return Dvorak;
        }, out var heldChanged);

        Assert.False(taken);
        Assert.False(heldChanged);
        Assert.Equal(0, read);
        Assert.Same(Us, table.Layout);
        Assert.Equal((ushort)0x25, table.CodeOf(1));
        Assert.Contains(system.Log, line => line.Contains("off the main thread"));
    }

    [Fact]
    public void A_change_on_the_main_thread_reads_the_layout_and_takes_it()
    {
        var (_, table) = Opened();

        var taken = table.Retake(true, () => Dvorak, out _);

        Assert.True(taken);
        Assert.Same(Dvorak, table.Layout);
        Assert.Equal((ushort)0x23, table.CodeOf(1));
        Assert.Equal((ushort)0x0F, table.CodeOf(2));
    }
}
