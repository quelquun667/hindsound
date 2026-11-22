namespace Hindsound.Mac.Input;

/// <summary>
/// The shortcuts registered through Carbon, and what a change of keyboard layout does to them. Pure: the system's
/// RegisterEventHotKey and UnregisterEventHotKey come in as two functions, so the decisions are tested on every system.
/// <para>
/// A letter or a digit is on the key that types it in <see cref="Layout"/> (<see cref="MacKeyCodes.Resolve"/>). On a change
/// of layout every shortcut whose key moved is released first, so that two shortcuts that trade keys (A and Q from QWERTY to
/// AZERTY) both find theirs free, and each is registered on its new key. One refused there (another app holds it) goes back
/// to its old key; if that one was taken in the meantime by another shortcut of the app, the shortcut is not dropped: it
/// stays in the table <i>in waiting</i>, with no registration behind it, and every later change of layout tries it again.
/// A shortcut in waiting is still one of the app's: <see cref="Remove"/> lets it go, <see cref="IsHeld"/> says it does not work.
/// </para>
/// </summary>
internal sealed class MacHotkeyTable(Func<ushort, uint, int, nint?> hold, Action<nint> free, Action<string> log)
{
    private readonly Dictionary<int, Entry> _entries = [];

    /// <summary>The layout the letters and digits are placed in; null when it could not be read (US places).</summary>
    public MacKeyLayout? Layout { get; private set; }

    /// <summary>The ids in the table, the ones in waiting included.</summary>
    public IReadOnlyCollection<int> Ids => _entries.Keys;

    /// <summary>The key code an id is on now (the one it waits for, when in waiting), or null.</summary>
    public ushort? CodeOf(int id) => _entries.TryGetValue(id, out var entry) ? entry.Code : null;

    /// <summary>Whether a press of the id reaches the app: false for an id in waiting, and for one the table does not know.</summary>
    public bool IsHeld(int id) => _entries.TryGetValue(id, out var entry) && entry.Held;

    /// <summary>The key to register for a virtual key in the current layout (a fallback to the US place is logged), or null when a Mac has no such key.</summary>
    public ushort? CodeFor(uint virtualKey)
    {
        if (MacKeyCodes.Resolve(virtualKey, Layout) is not { } key)
        {
            return null;
        }

        if (key.ByPlace)
        {
            log($"hotkeys: no key types {(char)virtualKey} in the layout {Layout?.Name ?? "none"}: its place on a US keyboard, key code 0x{key.Code:X2}");
        }

        return key.Code;
    }

    /// <summary>Takes <paramref name="layout"/> as the current one, without moving anything.</summary>
    public void Use(MacKeyLayout? layout) => Layout = layout;

    /// <summary>Registers the id on <paramref name="code"/>; false (nothing kept) when the system refuses it.</summary>
    public bool Add(int id, uint modifiers, uint virtualKey, ushort code)
    {
        if (hold(code, modifiers, id) is not { } reference)
        {
            return false;
        }

        _entries[id] = new Entry(reference, modifiers, virtualKey, code, true);
        return true;
    }

    /// <summary>Lets an id go: its registration is released, and a shortcut in waiting is dropped with nothing to release.</summary>
    public void Remove(int id)
    {
        if (_entries.Remove(id, out var entry) && entry.Held)
        {
            free(entry.Reference);
        }
    }

    /// <summary>Lets every id go.</summary>
    public void Clear()
    {
        foreach (var id in _entries.Keys.ToArray())
        {
            Remove(id);
        }
    }

    /// <summary>
    /// Takes <paramref name="layout"/> and registers again every shortcut whose key moved, and every one in waiting. The number
    /// registered on a new key; <paramref name="heldChanged"/> is true when a shortcut stopped working or started again.
    /// </summary>
    public int Relayout(MacKeyLayout? layout, out bool heldChanged)
    {
        Layout = layout;
        log($"hotkeys: keyboard layout {layout?.Name ?? "none"}");
        heldChanged = false;
        var moved = new List<(int Id, Entry Old, ushort Code)>();
        foreach (var (id, entry) in _entries.OrderBy(kv => kv.Key))
        {
            if (CodeFor(entry.VirtualKey) is { } code && (!entry.Held || code != entry.Code))
            {
                moved.Add((id, entry, code));
            }
        }

        foreach (var (id, old, _) in moved)
        {
            if (old.Held)
            {
                free(old.Reference);
                _entries[id] = old with { Reference = 0, Held = false };
            }
        }

        var count = 0;
        foreach (var (id, old, code) in moved)
        {
            if (hold(code, old.Modifiers, id) is { } reference)
            {
                _entries[id] = old with { Reference = reference, Code = code, Held = true };
                log(old.Held
                    ? $"hotkeys: {id} moved from key code 0x{old.Code:X2} to 0x{code:X2}"
                    : $"hotkeys: {id} works again, on key code 0x{code:X2}");
                heldChanged |= !old.Held;
                count++;
            }
            else if (old.Held && hold(old.Code, old.Modifiers, id) is { } back)
            {
                _entries[id] = old with { Reference = back, Held = true };
                log($"hotkeys: {id} stays on key code 0x{old.Code:X2}: 0x{code:X2} is held by another app");
            }
            else
            {
                _entries[id] = old with { Reference = 0, Code = code, Held = false };
                log(old.Held
                    ? $"hotkeys: {id} waiting: neither key code 0x{code:X2} nor 0x{old.Code:X2} could be registered, tried again at the next change of layout"
                    : $"hotkeys: {id} still waiting: key code 0x{code:X2} is held by another app");
                heldChanged |= old.Held;
            }
        }

        return count;
    }

    /// <summary>
    /// What a change of layout posted by macOS does, from the thread that received it. Off the main thread nothing is touched
    /// (Carbon and the input sources belong to the main thread) and the line says so: false. Otherwise the layout is read with
    /// <paramref name="read"/> and taken by <see cref="Relayout"/>.
    /// </summary>
    public bool Retake(bool onMainThread, Func<MacKeyLayout?> read, out bool heldChanged)
    {
        heldChanged = false;
        if (!onMainThread)
        {
            log("hotkeys: a change of keyboard layout came off the main thread: not taken");
            return false;
        }

        Relayout(read(), out heldChanged);
        return true;
    }

    private readonly record struct Entry(nint Reference, uint Modifiers, uint VirtualKey, ushort Code, bool Held);
}
