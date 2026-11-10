namespace Hindsound.Input;

/// <summary>A keyboard shortcut as Win32 sees it: modifier flags plus a virtual-key code. Text form: "Ctrl+Shift+F10".</summary>
public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private static readonly (string Name, uint Flag)[] ModifierNames =
    [
        ("Ctrl", ModControl), ("Alt", ModAlt), ("Shift", ModShift), ("Win", ModWin),
    ];

    // Non-alphanumeric keys that can be named in text, with their virtual-key codes.
    private static readonly (string Name, uint Code)[] NamedKeys =
    [
        ("Space", 0x20), ("Tab", 0x09), ("Enter", 0x0D), ("Escape", 0x1B), ("Insert", 0x2D), ("Delete", 0x2E),
        ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28), ("Pause", 0x13),
    ];

    private const uint VkF1 = 0x70;
    private const uint VkF24 = 0x87;

    /// <summary>Parses text such as "Ctrl+Alt+H". Never throws; returns false for anything invalid.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
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
            var flag = ModifierFlag(parts[i].Trim());
            if (flag == 0 || (modifiers & flag) != 0)
            {
                return false;
            }

            modifiers |= flag;
        }

        if (!TryParseKey(parts[^1].Trim(), out var key))
        {
            return false;
        }

        return TryCreate(modifiers, key, out gesture);
    }

    /// <summary>Builds a gesture from parts: known key, known modifiers, and a modifier unless the key is an F key.</summary>
    public static bool TryCreate(uint modifiers, uint virtualKey, out HotkeyGesture gesture)
    {
        gesture = default;
        if ((modifiers & ~(ModAlt | ModControl | ModShift | ModWin)) != 0 || KeyName(virtualKey) is null)
        {
            return false;
        }

        if (modifiers == 0 && !IsFunctionKey(virtualKey))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    /// <summary>Canonical text: modifiers in the order Ctrl, Alt, Shift, Win, then the key.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        foreach (var (name, flag) in ModifierNames)
        {
            if ((Modifiers & flag) != 0)
            {
                parts.Add(name);
            }
        }

        parts.Add(KeyName(VirtualKey) ?? $"0x{VirtualKey:X2}");
        return string.Join('+', parts);
    }

    private static bool IsFunctionKey(uint key) => key >= VkF1 && key <= VkF24;

    private static uint ModifierFlag(string name)
    {
        foreach (var (n, flag) in ModifierNames)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                return flag;
            }
        }

        return string.Equals(name, "Control", StringComparison.OrdinalIgnoreCase) ? ModControl : 0;
    }

    private static bool TryParseKey(string name, out uint key)
    {
        key = 0;
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            key = char.ToUpperInvariant(name[0]);
            return true;
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            key = name[0];
            return true;
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && name.Skip(1).All(char.IsAsciiDigit))
        {
            var n = int.Parse(name.AsSpan(1));
            if (n is >= 1 and <= 24)
            {
                key = VkF1 + (uint)(n - 1);
                return true;
            }

            return false;
        }

        foreach (var (named, code) in NamedKeys)
        {
            if (string.Equals(named, name, StringComparison.OrdinalIgnoreCase))
            {
                key = code;
                return true;
            }
        }

        return false;
    }

    private static string? KeyName(uint key)
    {
        if (key is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)key).ToString();
        }

        if (IsFunctionKey(key))
        {
            return "F" + (key - VkF1 + 1);
        }

        foreach (var (name, code) in NamedKeys)
        {
            if (code == key)
            {
                return name;
            }
        }

        return null;
    }
}
