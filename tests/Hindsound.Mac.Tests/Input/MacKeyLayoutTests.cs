using Hindsound.Input;
using Hindsound.Mac.Input;

namespace Hindsound.Mac.Tests.Input;

/// <summary>
/// The key that types a letter or a digit, from fixed tables of what the keys of three Mac layouts type (US, French
/// AZERTY, German QWERTZ), and the key code registered for each key of the Core. Pure: runs on every system.
/// </summary>
public sealed class MacKeyLayoutTests
{
    // What each key code types on a US Mac keyboard, without and with Shift, the keypad included.
    private static readonly Dictionary<ushort, (string Plain, string Shift)> Us = new()
    {
        [0x00] = ("a", "A"), [0x01] = ("s", "S"), [0x02] = ("d", "D"), [0x03] = ("f", "F"), [0x04] = ("h", "H"),
        [0x05] = ("g", "G"), [0x06] = ("z", "Z"), [0x07] = ("x", "X"), [0x08] = ("c", "C"), [0x09] = ("v", "V"),
        [0x0A] = ("\u00A7", "\u00B1"), [0x0B] = ("b", "B"), [0x0C] = ("q", "Q"), [0x0D] = ("w", "W"), [0x0E] = ("e", "E"),
        [0x0F] = ("r", "R"), [0x10] = ("y", "Y"), [0x11] = ("t", "T"), [0x12] = ("1", "!"), [0x13] = ("2", "@"),
        [0x14] = ("3", "#"), [0x15] = ("4", "$"), [0x16] = ("6", "^"), [0x17] = ("5", "%"), [0x18] = ("=", "+"),
        [0x19] = ("9", "("), [0x1A] = ("7", "&"), [0x1B] = ("-", "_"), [0x1C] = ("8", "*"), [0x1D] = ("0", ")"),
        [0x1E] = ("]", "}"), [0x1F] = ("o", "O"), [0x20] = ("u", "U"), [0x21] = ("[", "{"), [0x22] = ("i", "I"),
        [0x23] = ("p", "P"), [0x25] = ("l", "L"), [0x26] = ("j", "J"), [0x27] = ("'", "\""), [0x28] = ("k", "K"),
        [0x29] = (";", ":"), [0x2A] = ("\\", "|"), [0x2B] = (",", "<"), [0x2C] = ("/", "?"), [0x2D] = ("n", "N"),
        [0x2E] = ("m", "M"), [0x2F] = (".", ">"), [0x32] = ("`", "~"),
        [0x52] = ("0", "0"), [0x53] = ("1", "1"), [0x54] = ("2", "2"), [0x55] = ("3", "3"), [0x56] = ("4", "4"),
        [0x57] = ("5", "5"), [0x58] = ("6", "6"), [0x59] = ("7", "7"), [0x5B] = ("8", "8"), [0x5C] = ("9", "9"),
    };

    // The French layout of a Mac (AZERTY): the digits of the top row with Shift only; ',' ';' ':' '=' where US has m , . /.
    private static readonly Dictionary<ushort, (string Plain, string Shift)> French = With(Us, new()
    {
        [0x0A] = ("@", "#"), [0x12] = ("&", "1"), [0x13] = ("\u00E9", "2"), [0x14] = ("\"", "3"), [0x15] = ("'", "4"),
        [0x17] = ("(", "5"), [0x16] = ("\u00A7", "6"), [0x1A] = ("\u00E8", "7"), [0x1C] = ("!", "8"), [0x19] = ("\u00E7", "9"),
        [0x1D] = ("\u00E0", "0"), [0x1B] = (")", "\u00B0"), [0x18] = ("-", "_"),
        [0x0C] = ("a", "A"), [0x0D] = ("z", "Z"), [0x21] = ("^", "\u00A8"), [0x1E] = ("$", "*"),
        [0x00] = ("q", "Q"), [0x29] = ("m", "M"), [0x27] = ("\u00F9", "%"), [0x2A] = ("`", "\u00A3"),
        [0x32] = ("<", ">"), [0x06] = ("w", "W"), [0x2E] = (",", "?"), [0x2B] = (";", "."), [0x2F] = (":", "/"),
        [0x2C] = ("=", "+"),
    });

    // The German layout of a Mac (QWERTZ): Y and Z traded, umlauts where US has punctuation.
    private static readonly Dictionary<ushort, (string Plain, string Shift)> German = With(Us, new()
    {
        [0x0A] = ("^", "\u00B0"), [0x12] = ("1", "!"), [0x13] = ("2", "\""), [0x14] = ("3", "\u00A7"), [0x15] = ("4", "$"),
        [0x17] = ("5", "%"), [0x16] = ("6", "&"), [0x1A] = ("7", "/"), [0x1C] = ("8", "("), [0x19] = ("9", ")"),
        [0x1D] = ("0", "="), [0x1B] = ("\u00DF", "?"), [0x18] = ("\u00B4", "`"),
        [0x10] = ("z", "Z"), [0x06] = ("y", "Y"), [0x21] = ("\u00FC", "\u00DC"), [0x1E] = ("+", "*"),
        [0x29] = ("\u00F6", "\u00D6"), [0x27] = ("\u00E4", "\u00C4"), [0x2A] = ("#", "'"), [0x32] = ("<", ">"),
        [0x2B] = (",", ";"), [0x2F] = (".", ":"), [0x2C] = ("-", "_"),
    });

    // A layout without Latin letters (Russian): the letters' keys type Cyrillic, the digits stay.
    private static readonly Dictionary<ushort, (string Plain, string Shift)> Russian = Us.ToDictionary(
        kv => kv.Key,
        kv => kv.Value.Plain is [>= 'a' and <= 'z'] ? ("\u0436", "\u0416") : kv.Value);

    [Theory]
    [InlineData('A', 0x0C)]
    [InlineData('Q', 0x00)]
    [InlineData('Z', 0x0D)]
    [InlineData('W', 0x06)]
    [InlineData('M', 0x29)]
    [InlineData('S', 0x01)]
    [InlineData('1', 0x12)]
    [InlineData('5', 0x17)]
    [InlineData('0', 0x1D)]
    public void On_AZERTY_a_letter_or_a_digit_is_the_key_that_types_it(char key, int expected)
    {
        Assert.Equal(((ushort)expected, false), MacKeyCodes.Resolve(key, Layout(French)));
    }

    [Theory]
    [InlineData('Y', 0x06)]
    [InlineData('Z', 0x10)]
    [InlineData('A', 0x00)]
    [InlineData('1', 0x12)]
    public void On_QWERTZ_Y_and_Z_trade_keys(char key, int expected)
    {
        Assert.Equal(((ushort)expected, false), MacKeyCodes.Resolve(key, Layout(German)));
    }

    [Fact]
    public void On_US_every_letter_and_digit_is_at_its_place()
    {
        var layout = Layout(Us);
        foreach (var key in Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Select(k => (uint)k))
        {
            Assert.Equal((MacKeyCodes.ToKeyCode(key)!.Value, false), MacKeyCodes.Resolve(key, layout));
        }
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F12")]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Left")]
    [InlineData("Delete")]
    public void A_key_without_a_character_keeps_its_place_on_any_layout(string name)
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+" + name, out var gesture));
        foreach (var table in new[] { Us, French, German, Russian })
        {
            Assert.Equal((MacKeyCodes.ToKeyCode(gesture.VirtualKey)!.Value, false), MacKeyCodes.Resolve(gesture.VirtualKey, Layout(table)));
        }
    }

    [Fact]
    public void A_letter_no_key_types_falls_back_to_its_US_place_and_says_so()
    {
        var layout = Layout(Russian);
        Assert.Equal(((ushort)0x00, true), MacKeyCodes.Resolve('A', layout));
        Assert.Equal(((ushort)0x2E, true), MacKeyCodes.Resolve('M', layout));
        Assert.Equal(((ushort)0x12, false), MacKeyCodes.Resolve('1', layout));
        Assert.Equal(((ushort)0x0C, true), MacKeyCodes.Resolve('Q', null));
        Assert.Null(MacKeyCodes.Resolve(0x13, Layout(French))); // Pause: no key on a Mac
    }

    [Fact]
    public void A_digit_of_the_keypad_or_a_symbol_is_never_taken_for_the_key_of_a_digit()
    {
        var layout = Layout(French);
        foreach (var digit in Enumerable.Range('0', 10).Select(d => (uint)d))
        {
            Assert.Equal(MacKeyCodes.ToKeyCode(digit), layout.CodeOf(digit));
        }

        Assert.Equal(26 + 10, layout.Count);
        Assert.Equal(26 + 10, Layout(German).Count);
    }

    [Theory]
    [InlineData("French")]
    [InlineData("German")]
    [InlineData("US")]
    public void Every_key_the_window_names_by_a_letter_or_a_digit_is_the_key_registered_for_it(string name)
    {
        // The window (SdlKeys over SDL's default options) names a key by the letter or digit it types without Shift, and a
        // key of the top row by the digit it types with Shift: registering that name must land on the same key.
        var table = name switch { "French" => French, "German" => German, _ => Us };
        var layout = Layout(table);
        var checkedKeys = 0;
        foreach (var code in MacKeyLayout.CharacterKeys)
        {
            var (plain, shift) = table[code];
            var named = plain is [var c] && MacKeyLayout.IsCharacter(char.ToUpperInvariant(c)) ? char.ToUpperInvariant(c)
                : shift is [>= '0' and <= '9'] ? shift[0]
                : '\0';
            if (named != '\0')
            {
                Assert.Equal(((ushort)code, false), MacKeyCodes.Resolve(named, layout));
                checkedKeys++;
            }
        }

        Assert.Equal(36, checkedKeys);
    }

    private static MacKeyLayout Layout(Dictionary<ushort, (string Plain, string Shift)> table) =>
        MacKeyLayout.From("test", (code, shift) => table.TryGetValue(code, out var typed) ? (shift ? typed.Shift : typed.Plain) : null);

    private static Dictionary<ushort, (string Plain, string Shift)> With(Dictionary<ushort, (string Plain, string Shift)> table, Dictionary<ushort, (string Plain, string Shift)> changes)
    {
        var copy = new Dictionary<ushort, (string Plain, string Shift)>(table);
        foreach (var (code, typed) in changes)
        {
            copy[code] = typed;
        }

        return copy;
    }
}
