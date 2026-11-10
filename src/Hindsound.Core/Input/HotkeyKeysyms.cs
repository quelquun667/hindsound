namespace Hindsound.Input;

/// <summary>
/// The keys of a <see cref="HotkeyGesture"/> (Win32 modifier flags and virtual-key codes, as settings.json keeps them) in the
/// words of the Linux desktops, and back: the trigger text of the GlobalShortcuts portal and the keysyms and modifier
/// masks of X11. Pure logic, so it is tested on every system.
/// </summary>
/// <remarks>
/// The trigger text follows the XDG shortcuts specification: modifiers among CTRL, ALT, SHIFT, NUM and LOGO, then a key
/// named as in xkbcommon-keysyms.h without its XKB_KEY_ prefix, joined by '+', from the base layer of the keyboard
/// (so "CTRL+SHIFT+h", not "CTRL+SHIFT+H"). The keysym values are those of X11's keysymdef.h.
/// </remarks>
public static class HotkeyKeysyms
{
    /// <summary>X11 modifier masks (X.h): Shift, Lock (Caps Lock), Control, Mod1 (Alt), Mod4 (Super, the Windows key).</summary>
    public const uint X11ShiftMask = 1 << 0;
    public const uint X11LockMask = 1 << 1;
    public const uint X11ControlMask = 1 << 2;
    public const uint X11Mod1Mask = 1 << 3;
    public const uint X11Mod2Mask = 1 << 4;
    public const uint X11Mod4Mask = 1 << 6;

    /// <summary>The keysym of Num Lock, to find which modifier mask it sets on this keyboard.</summary>
    public const uint KeysymNumLock = 0xff7f;

    private const uint VkF1 = 0x70;
    private const uint VkF24 = 0x87;
    private const uint KeysymF1 = 0xffbe;

    // Modifier flag, trigger word, X11 mask; in the order a trigger is written.
    private static readonly (uint Flag, string Word, uint Mask)[] Modifiers =
    [
        (HotkeyGesture.ModControl, "CTRL", X11ControlMask),
        (HotkeyGesture.ModAlt, "ALT", X11Mod1Mask),
        (HotkeyGesture.ModShift, "SHIFT", X11ShiftMask),
        (HotkeyGesture.ModWin, "LOGO", X11Mod4Mask),
    ];

    // The named keys HotkeyGesture knows, with their keysym name and value. Aliases are read, never written.
    private static readonly (uint VirtualKey, string Name, uint Keysym)[] NamedKeys =
    [
        (0x20, "space", 0x0020), (0x09, "Tab", 0xff09), (0x0D, "Return", 0xff0d), (0x1B, "Escape", 0xff1b),
        (0x2D, "Insert", 0xff63), (0x2E, "Delete", 0xffff), (0x24, "Home", 0xff50), (0x23, "End", 0xff57),
        (0x21, "Page_Up", 0xff55), (0x22, "Page_Down", 0xff56),
        (0x25, "Left", 0xff51), (0x26, "Up", 0xff52), (0x27, "Right", 0xff53), (0x28, "Down", 0xff54), (0x13, "Pause", 0xff13),
    ];

    // keysymdef.h names Page_Up and Page_Down also Prior and Next.
    private static readonly (string Alias, string Name)[] KeyAliases = [("Prior", "Page_Up"), ("Next", "Page_Down")];

    /// <summary>Every virtual key a gesture can name, for tests that walk them all.</summary>
    public static IEnumerable<uint> KnownVirtualKeys()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            yield return c;
        }

        for (var c = '0'; c <= '9'; c++)
        {
            yield return c;
        }

        for (var vk = VkF1; vk <= VkF24; vk++)
        {
            yield return vk;
        }

        foreach (var key in NamedKeys)
        {
            yield return key.VirtualKey;
        }
    }

    /// <summary>The keysym of a virtual key, as the base layer of a keyboard gives it (a lowercase letter); null when unknown.</summary>
    public static uint? ToKeysym(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z')
        {
            return virtualKey + ('a' - 'A');
        }

        if (virtualKey is >= '0' and <= '9')
        {
            return virtualKey;
        }

        if (virtualKey is >= VkF1 and <= VkF24)
        {
            return KeysymF1 + (virtualKey - VkF1);
        }

        foreach (var key in NamedKeys)
        {
            if (key.VirtualKey == virtualKey)
            {
                return key.Keysym;
            }
        }

        return null;
    }

    /// <summary>The virtual key of a keysym, either case of a letter; null for a key a gesture cannot name.</summary>
    public static uint? ToVirtualKey(ulong keysym)
    {
        if (keysym is >= 'a' and <= 'z')
        {
            return (uint)keysym - ('a' - 'A');
        }

        if (keysym is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return (uint)keysym;
        }

        if (keysym >= KeysymF1 && keysym <= KeysymF1 + (VkF24 - VkF1))
        {
            return VkF1 + (uint)(keysym - KeysymF1);
        }

        foreach (var key in NamedKeys)
        {
            if (key.Keysym == keysym)
            {
                return key.VirtualKey;
            }
        }

        return null;
    }

    /// <summary>The keysym name of a virtual key, as a trigger writes it; null when unknown.</summary>
    public static string? KeysymName(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z')
        {
            return ((char)(virtualKey + ('a' - 'A'))).ToString();
        }

        if (virtualKey is >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= VkF1 and <= VkF24)
        {
            return "F" + (virtualKey - VkF1 + 1);
        }

        foreach (var key in NamedKeys)
        {
            if (key.VirtualKey == virtualKey)
            {
                return key.Name;
            }
        }

        return null;
    }

    /// <summary>The trigger text the portal is offered, such as <c>CTRL+SHIFT+F10</c>; null when a part has no name.</summary>
    public static string? ToPortalTrigger(HotkeyGesture gesture)
    {
        if (KeysymName(gesture.VirtualKey) is not { } key || (gesture.Modifiers & ~AllFlags()) != 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var (flag, word, _) in Modifiers)
        {
            if ((gesture.Modifiers & flag) != 0)
            {
                parts.Add(word);
            }
        }

        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>
    /// Reads a trigger back: the specification's words (<c>CTRL+SHIFT+h</c>), or the app's own (<c>Ctrl+Shift+H</c>),
    /// in any case. False for anything a gesture cannot hold: an unknown key, NUM, a modifier twice, no modifier on a key
    /// that needs one. Never throws.
    /// </summary>
    public static bool TryParseTrigger(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+');
        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = ModifierOf(parts[i].Trim());
            if (flag == 0 || (modifiers & flag) != 0)
            {
                return false;
            }

            modifiers |= flag;
        }

        return KeyOf(parts[^1].Trim()) is { } key && HotkeyGesture.TryCreate(modifiers, key, out gesture);
    }

    /// <summary>The X11 modifier mask of the gesture's modifiers.</summary>
    public static uint ToX11Modifiers(uint modifiers)
    {
        uint mask = 0;
        foreach (var (flag, _, x11) in Modifiers)
        {
            if ((modifiers & flag) != 0)
            {
                mask |= x11;
            }
        }

        return mask;
    }

    /// <summary>The gesture's modifiers in an X11 state, the locks and every other mask left out.</summary>
    public static uint FromX11Modifiers(uint state)
    {
        uint modifiers = 0;
        foreach (var (flag, _, x11) in Modifiers)
        {
            if ((state & x11) != 0)
            {
                modifiers |= flag;
            }
        }

        return modifiers;
    }

    /// <summary>
    /// The lock states one combination is grabbed under, so it works with Caps Lock and Num Lock on or off: X11 matches a
    /// grab on the exact modifier state, locks included.
    /// </summary>
    public static uint[] LockVariants(uint numLockMask) =>
        numLockMask == 0 || numLockMask == X11LockMask
            ? [0, X11LockMask]
            : [0, X11LockMask, numLockMask, X11LockMask | numLockMask];

    /// <summary>Whether a key event's state is a grab's modifiers, whatever the locks are.</summary>
    public static bool StateMatches(uint state, uint grabModifiers, uint numLockMask) =>
        (state & ~(X11LockMask | numLockMask) & RelevantX11Masks()) == grabModifiers;

    private static uint RelevantX11Masks() => X11ShiftMask | X11ControlMask | X11Mod1Mask | X11Mod4Mask;

    private static uint AllFlags() => HotkeyGesture.ModAlt | HotkeyGesture.ModControl | HotkeyGesture.ModShift | HotkeyGesture.ModWin;

    // Key and modifier words are ASCII, read in any case. Not a path: no PathRules here.
    private static bool SameWord(string a, string b) => string.Equals(a.ToUpperInvariant(), b.ToUpperInvariant(), StringComparison.Ordinal);

    private static uint ModifierOf(string word)
    {
        foreach (var (flag, name, _) in Modifiers)
        {
            if (SameWord(name, word))
            {
                return flag;
            }
        }

        // The app's own words, and the names desktops show (Super is the Windows key on Linux).
        return word.ToUpperInvariant() switch
        {
            "CONTROL" => HotkeyGesture.ModControl,
            "SUPER" or "WIN" or "META" => HotkeyGesture.ModWin,
            _ => 0,
        };
    }

    private static uint? KeyOf(string name)
    {
        if (name.Length == 1 && (char.IsAsciiLetter(name[0]) || char.IsAsciiDigit(name[0])))
        {
            return ToVirtualKey(name[0]);
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && name.Skip(1).All(char.IsAsciiDigit))
        {
            var n = int.Parse(name.AsSpan(1));
            return n is >= 1 and <= 24 ? VkF1 + (uint)(n - 1) : null;
        }

        foreach (var (alias, real) in KeyAliases)
        {
            if (SameWord(alias, name))
            {
                name = real;
                break;
            }
        }

        foreach (var key in NamedKeys)
        {
            if (SameWord(key.Name, name))
            {
                return key.VirtualKey;
            }
        }

        // The app's own names (PageUp, Escape...), as HotkeyGesture writes them.
        return HotkeyGesture.TryParse("Ctrl+" + name, out var gesture) ? gesture.VirtualKey : null;
    }
}
