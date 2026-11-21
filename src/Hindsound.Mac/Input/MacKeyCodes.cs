using Hindsound.Input;

namespace Hindsound.Mac.Input;

/// <summary>
/// The keys of the Core (<see cref="HotkeyGesture"/>: the virtual keys and modifiers of Win32) as Carbon names them: the
/// virtual key codes of macOS (kVK_*, places on the keyboard, as Events.h lists them) and the modifiers of
/// RegisterEventHotKey. Pure: tested on every system.
/// </summary>
/// <remarks>
/// A letter is the key at its place on a US keyboard (kVK_ANSI_*), as RegisterEventHotKey takes it: on another layout the
/// same place may carry another letter. F21 to F24 and Pause have no key on a Mac: they are refused.
/// </remarks>
internal static class MacKeyCodes
{
    /// <summary>Carbon's cmdKey, shiftKey, optionKey and controlKey.</summary>
    public const uint CmdKey = 0x0100;
    public const uint ShiftKey = 0x0200;
    public const uint OptionKey = 0x0800;
    public const uint ControlKey = 0x1000;

    private static readonly Dictionary<uint, ushort> Codes = Build();

    /// <summary>The key code of macOS for a virtual key of the Core, or null when a Mac has no such key.</summary>
    public static ushort? ToKeyCode(uint virtualKey) => Codes.TryGetValue(virtualKey, out var code) ? code : null;

    /// <summary>Ctrl is Control, Alt is Option, Win is Command (the key SDL calls GUI), Shift is Shift.</summary>
    public static uint ToModifiers(uint modifiers)
    {
        var carbon = 0u;
        if ((modifiers & HotkeyGesture.ModControl) != 0)
        {
            carbon |= ControlKey;
        }

        if ((modifiers & HotkeyGesture.ModAlt) != 0)
        {
            carbon |= OptionKey;
        }

        if ((modifiers & HotkeyGesture.ModShift) != 0)
        {
            carbon |= ShiftKey;
        }

        if ((modifiers & HotkeyGesture.ModWin) != 0)
        {
            carbon |= CmdKey;
        }

        return carbon;
    }

    private static Dictionary<uint, ushort> Build()
    {
        var codes = new Dictionary<uint, ushort>();

        // kVK_ANSI_A ... kVK_ANSI_Z, in the order of the alphabet.
        ushort[] letters =
        [
            0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E,
            0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06,
        ];
        for (var i = 0; i < letters.Length; i++)
        {
            codes['A' + (uint)i] = letters[i];
        }

        // kVK_ANSI_0 ... kVK_ANSI_9.
        ushort[] digits = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19];
        for (var i = 0; i < digits.Length; i++)
        {
            codes['0' + (uint)i] = digits[i];
        }

        // kVK_F1 ... kVK_F20 (VK_F1 is 0x70).
        ushort[] functions =
        [
            0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D,
            0x67, 0x6F, 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A,
        ];
        for (var i = 0; i < functions.Length; i++)
        {
            codes[0x70 + (uint)i] = functions[i];
        }

        // Space, Tab, Return, Escape, Help (where Insert is on a PC keyboard), forward Delete, Home, End, Page Up and Down,
        // the arrows.
        (uint Vk, ushort Code)[] named =
        [
            (0x20, 0x31), (0x09, 0x30), (0x0D, 0x24), (0x1B, 0x35), (0x2D, 0x72), (0x2E, 0x75),
            (0x24, 0x73), (0x23, 0x77), (0x21, 0x74), (0x22, 0x79),
            (0x25, 0x7B), (0x26, 0x7E), (0x27, 0x7C), (0x28, 0x7D),
        ];
        foreach (var (vk, code) in named)
        {
            codes[vk] = code;
        }

        return codes;
    }
}
