namespace Hindsound.Mac.Input;

/// <summary>
/// Which key of a Mac keyboard types each letter and digit in one keyboard layout: the inverse of what the layout types,
/// read key by key (UCKeyTranslate on macOS, a fixed table in a test). Pure: tested on every system.
/// </summary>
/// <remarks>
/// It mirrors how the window names a key it is given (SdlKeys, over SDL's default keycode options): a letter is the key
/// that types it without Shift, in either case; a digit is the key that types it without Shift, or else with Shift (the
/// top row of AZERTY, which SDL names by its digit). Only the block of the character keys is read: a digit of the keypad
/// is not the key of the top row. A letter or a digit that no key types has no key here; the caller then takes its place
/// on a US keyboard, as SDL does for a layout without Latin letters.
/// <para>
/// Turkish Q: the key at the US place of I types the dotless letter (U+0131) and the key that types the Latin i sits where a
/// US keyboard has the quote. The shortcut I goes to the key that types the Latin letter, the quote key (the rule above: a
/// key is the letter it types, and the dotless one is another letter), not to the place of I. The window names both keys I
/// when the shortcut is captured (SDL names the dotless key by its place), so pressing that one while choosing "I" registers
/// the other: the choice is the Latin letter's key, and a test fixes it.
/// </para>
/// </remarks>
internal sealed class MacKeyLayout
{
    /// <summary>
    /// The character keys of a Mac keyboard (kVK_ANSI_* and kVK_ISO_Section), in the order of their places on a US keyboard:
    /// the letters A to Z, the digits 0 to 9, then the punctuation. A character typed by two keys goes to the first.
    /// </summary>
    public static readonly ushort[] CharacterKeys =
    [
        0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E,
        0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06,
        0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19,
        0x1B, 0x18, 0x21, 0x1E, 0x2A, 0x29, 0x27, 0x32, 0x2B, 0x2F, 0x2C, 0x0A,
    ];

    private readonly Dictionary<uint, ushort> _keys;

    private MacKeyLayout(string name, Dictionary<uint, ushort> keys)
    {
        Name = name;
        _keys = keys;
    }

    /// <summary>The layout's name, as macOS gives it (com.apple.keylayout.French), for the log.</summary>
    public string Name { get; }

    /// <summary>The number of letters and digits some key types.</summary>
    public int Count => _keys.Count;

    /// <summary>
    /// The layout as <paramref name="type"/> reads it: what a key code types without Shift (false) or with it (true), or
    /// null when it types nothing (a dead key gives its own character).
    /// </summary>
    public static MacKeyLayout From(string name, Func<ushort, bool, string?> type)
    {
        var keys = new Dictionary<uint, ushort>();
        foreach (var code in CharacterKeys)
        {
            if (KeyOf(type(code, false)) is { } key)
            {
                keys.TryAdd(key, code);
            }
        }

        foreach (var code in CharacterKeys)
        {
            if (KeyOf(type(code, true)) is { } key and >= '0' and <= '9')
            {
                keys.TryAdd(key, code);
            }
        }

        return new MacKeyLayout(name, keys);
    }

    /// <summary>The key code that types the letter or digit <paramref name="virtualKey"/> (a virtual key of the Core), or null.</summary>
    public ushort? CodeOf(uint virtualKey) => _keys.TryGetValue(virtualKey, out var code) ? code : null;

    /// <summary>Whether a virtual key of the Core names a character, which a layout moves: a letter or a digit.</summary>
    public static bool IsCharacter(uint virtualKey) => virtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9';

    // The virtual key of what a key types: a Latin letter (either case) or a digit; anything else names no key here.
    private static uint? KeyOf(string? typed) => typed is { Length: 1 } ? typed[0] switch
    {
        >= 'a' and <= 'z' => (uint)(typed[0] - 'a' + 'A'),
        >= 'A' and <= 'Z' or >= '0' and <= '9' => typed[0],
        _ => null,
    } : null;
}
