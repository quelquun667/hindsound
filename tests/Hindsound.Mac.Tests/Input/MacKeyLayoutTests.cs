using Hindsound.Input;
using Hindsound.Mac.Input;
using Hindsound.Unix.Window.Input;

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

    // The Turkish Q layout of a Mac: the key at the US place of I types the dotless i, and the Latin i is where US has the quote.
    private static readonly Dictionary<ushort, (string Plain, string Shift)> Turkish = With(Us, new()
    {
        [0x22] = ("\u0131", "I"), [0x27] = ("i", "\u0130"), [0x21] = ("\u011F", "\u011E"), [0x1E] = ("\u00FC", "\u00DC"),
        [0x29] = ("\u015F", "\u015E"), [0x2B] = ("\u00F6", "\u00D6"), [0x2F] = ("\u00E7", "\u00C7"), [0x2A] = (",", ";"),
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
    [InlineData("French", new ushort[0])]
    [InlineData("German", new ushort[0])]
    [InlineData("US", new ushort[0])]
    [InlineData("Turkish", new ushort[] { 0x22 })]
    public void Every_key_the_window_names_by_a_letter_or_a_digit_is_the_key_registered_for_it(string name, ushort[] namedByPlaceOnly)
    {
        // The window names a key with SdlKeys, from the scancode of the key and the keycode SDL gives it: the letter or digit it
        // types without Shift, or the digit it types with Shift (the top row of AZERTY). Registering that name must land on the
        // same key. A key whose character is no Latin letter or digit (the dotless i of Turkish) is named by its place: it is
        // listed, and the name it gets is the one of another key.
        var table = name switch { "French" => French, "German" => German, "Turkish" => Turkish, _ => Us };
        var layout = Layout(table);
        var checkedKeys = 0;
        var byPlace = new List<ushort>();
        foreach (var code in MacKeyLayout.CharacterKeys)
        {
            var (plain, shift) = table[code];
            var keycode = plain is [>= 'a' and <= 'z' or >= '0' and <= '9'] ? plain[0]
                : shift is [>= '0' and <= '9'] ? shift[0]
                : plain[0];
            var named = SdlKeys.VirtualKey(Scancode(code), keycode, 0);
            if (!MacKeyLayout.IsCharacter(named))
            {
                continue;
            }

            if (keycode is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                Assert.Equal(((ushort)code, false), MacKeyCodes.Resolve(named, layout));
                checkedKeys++;
            }
            else
            {
                byPlace.Add(code);
            }
        }

        Assert.Equal(36, checkedKeys);
        Assert.Equal(namedByPlaceOnly, byPlace);
    }

    [Fact]
    public void On_a_Turkish_keyboard_the_letter_I_is_the_key_that_types_the_Latin_i_not_the_dotless_one_at_its_US_place()
    {
        var layout = Layout(Turkish);

        Assert.Equal(((ushort)0x27, false), MacKeyCodes.Resolve('I', layout));
        Assert.NotEqual((ushort)0x22, layout.CodeOf('I'));
        Assert.Equal(26 + 10, layout.Count);
        foreach (var letter in Enumerable.Range('A', 26).Select(l => (uint)l))
        {
            Assert.NotEqual((ushort)0x22, layout.CodeOf(letter)); // the dotless key is no letter's key
        }

        Assert.Equal(((ushort)0x1F, false), MacKeyCodes.Resolve('O', layout)); // the letters Turkish keeps are where US has them
    }

    // The SDL scancode (USB usage page 0x07) of a character key of a Mac keyboard: A to Z, 1 to 9 and 0, then the punctuation.
    private static uint Scancode(ushort code)
    {
        for (var i = 0; i < 26; i++)
        {
            if (MacKeyCodes.ToKeyCode((uint)('A' + i)) == code)
            {
                return SdlKeys.ScanA + (uint)i;
            }
        }

        for (var i = 0; i < 10; i++)
        {
            if (MacKeyCodes.ToKeyCode((uint)('0' + i)) == code)
            {
                return i == 0 ? SdlKeys.Scan0 : SdlKeys.Scan1 + (uint)(i - 1);
            }
        }

        return code switch
        {
            0x0A => SdlKeys.ScanNonUsBackslash,
            0x1B => 45, 0x18 => 46, 0x21 => 47, 0x1E => 48, 0x2A => 49, 0x29 => 51, 0x27 => 52, 0x32 => 53, 0x2B => 54, 0x2F => 55, 0x2C => 56,
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
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
