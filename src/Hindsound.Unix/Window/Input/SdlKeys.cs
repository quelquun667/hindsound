using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Unix.Window.Input;

/// <summary>
/// The keys of SDL 3.2 as the screen knows them: the Windows virtual keys and the <see cref="HotkeyGesture"/> modifier flags
/// that <c>SettingsScreen.Key</c>, the text field and the shortcut capture take. Plain values in, plain values out: the window
/// reads them from its SDL_KeyboardEvent (scancode, key, mod).
/// <para>
/// A letter or a digit is told by the key (the SDL keycode), which follows the keyboard layout as a Windows virtual key does:
/// on an AZERTY keyboard the key marked A is VK 'A', wherever its place. SDL's default keycode options give a Latin letter on
/// a non-Latin layout and the digits of the French top row, as Windows does. Every other key is told by its place (the
/// scancode), as a virtual key of Windows names a place for those.
/// </para>
/// </summary>
internal static class SdlKeys
{
    // SDL_Keymod (SDL_keycode.h, SDL 3.2).
    public const ushort ModLShift = 0x0001, ModRShift = 0x0002, ModLCtrl = 0x0040, ModRCtrl = 0x0080, ModLAlt = 0x0100,
        ModRAlt = 0x0200, ModLGui = 0x0400, ModRGui = 0x0800, ModNum = 0x1000, ModCaps = 0x2000, ModMode = 0x4000;

    /// <summary>SDLK_SCANCODE_MASK: a keycode with this bit has no character and names its scancode.</summary>
    public const uint ScancodeMask = 1u << 30;

    // SDL_Scancode values (SDL_scancode.h, SDL 3.2): the USB usage page 0x07.
    public const uint ScanA = 4, ScanZ = 29, Scan1 = 30, Scan0 = 39, ScanReturn = 40, ScanEscape = 41, ScanBackspace = 42,
        ScanTab = 43, ScanSpace = 44, ScanMinus = 45, ScanSlash = 56, ScanCapsLock = 57, ScanF1 = 58, ScanF12 = 69,
        ScanPrintScreen = 70, ScanScrollLock = 71, ScanPause = 72, ScanInsert = 73, ScanHome = 74, ScanPageUp = 75,
        ScanDelete = 76, ScanEnd = 77, ScanPageDown = 78, ScanRight = 79, ScanLeft = 80, ScanDown = 81, ScanUp = 82,
        ScanNumLock = 83, ScanKpDivide = 84, ScanKpMultiply = 85, ScanKpMinus = 86, ScanKpPlus = 87, ScanKpEnter = 88,
        ScanKp1 = 89, ScanKp9 = 97, ScanKp0 = 98, ScanKpPeriod = 99, ScanNonUsBackslash = 100, ScanApplication = 101,
        ScanF13 = 104, ScanF24 = 115, ScanLCtrl = 224, ScanLShift = 225, ScanLAlt = 226, ScanLGui = 227, ScanRCtrl = 228,
        ScanRShift = 229, ScanRAlt = 230, ScanRGui = 231;

    // Windows virtual keys not in Vk.
    private const uint VkClear = 0x0C, VkPause = 0x13, VkCapital = 0x14, VkSnapshot = 0x2C, VkInsert = 0x2D, VkDelete = 0x2E,
        VkApps = 0x5D, VkNumpad0 = 0x60, VkMultiply = 0x6A, VkAdd = 0x6B, VkSubtract = 0x6D, VkDecimal = 0x6E, VkDivide = 0x6F,
        VkF1 = 0x70, VkF13 = 0x7C, VkNumLock = 0x90, VkScroll = 0x91, VkOem1 = 0xBA, VkOemPlus = 0xBB, VkOemComma = 0xBC,
        VkOemMinus = 0xBD, VkOemPeriod = 0xBE, VkOem2 = 0xBF, VkOem3 = 0xC0, VkOem4 = 0xDB, VkOem5 = 0xDC, VkOem6 = 0xDD,
        VkOem7 = 0xDE, VkOem102 = 0xE2;

    // The punctuation of the US layout, by scancode from ScanMinus (45) to ScanSlash (56), and by the character it types.
    private static readonly uint[] PunctuationByPlace =
        [VkOemMinus, VkOemPlus, VkOem4, VkOem6, VkOem5, VkOem5, VkOem1, VkOem7, VkOem3, VkOemComma, VkOemPeriod, VkOem2];

    // '#' is left out (a NUL holds its place): it is Shift+3 on a US keyboard, which must stay the key 3.
    private const string PunctuationCharacters = "-=[]\\\0;'`,./";

    /// <summary>
    /// The Windows virtual key of an SDL key, or 0 for a key Windows has no code for here (the screen then ignores it).
    /// The modifier keys come as Windows sends them in WM_KEYDOWN: VK_SHIFT, VK_CONTROL and VK_MENU for either side, and
    /// VK_LWIN or VK_RWIN.
    /// </summary>
    /// <param name="scancode">SDL_KeyboardEvent.scancode.</param>
    /// <param name="keycode">SDL_KeyboardEvent.key.</param>
    /// <param name="mod">SDL_KeyboardEvent.mod: Num Lock decides what a key of the keypad is, as on Windows.</param>
    public static uint VirtualKey(uint scancode, uint keycode, ushort mod)
    {
        if (IsCharacterKey(scancode))
        {
            return CharacterKey(scancode, keycode);
        }

        if (scancode is >= ScanF1 and <= ScanF12)
        {
            return VkF1 + (scancode - ScanF1);
        }

        if (scancode is >= ScanF13 and <= ScanF24)
        {
            return VkF13 + (scancode - ScanF13);
        }

        if (scancode is >= ScanKp1 and <= ScanKp0 || scancode == ScanKpPeriod)
        {
            return Keypad(scancode, (mod & ModNum) != 0);
        }

        return scancode switch
        {
            ScanReturn or ScanKpEnter => Vk.Enter,
            ScanEscape => Vk.Escape,
            ScanBackspace => Vk.Back,
            ScanTab => Vk.Tab,
            ScanSpace => Vk.Space,
            ScanCapsLock => VkCapital,
            ScanPrintScreen => VkSnapshot,
            ScanScrollLock => VkScroll,
            ScanPause => VkPause,
            ScanInsert => VkInsert,
            ScanHome => Vk.Home,
            ScanPageUp => Vk.PageUp,
            ScanDelete => VkDelete,
            ScanEnd => Vk.End,
            ScanPageDown => Vk.PageDown,
            ScanRight => Vk.Right,
            ScanLeft => Vk.Left,
            ScanDown => Vk.Down,
            ScanUp => Vk.Up,
            ScanNumLock => VkNumLock,
            ScanKpDivide => VkDivide,
            ScanKpMultiply => VkMultiply,
            ScanKpMinus => VkSubtract,
            ScanKpPlus => VkAdd,
            ScanApplication => VkApps,
            ScanLCtrl or ScanRCtrl => Vk.Ctrl,
            ScanLShift or ScanRShift => Vk.Shift,
            ScanLAlt or ScanRAlt => Vk.Alt,
            ScanLGui => Vk.LWin,
            ScanRGui => Vk.RWin,
            _ => 0,
        };
    }

    /// <summary>
    /// The modifiers held, as <see cref="HotkeyGesture"/> flags. AltGr (SDL_KMOD_MODE) is no modifier here: it picks a
    /// character, which comes as text input, and Ctrl+Alt stands for it on Windows only.
    /// </summary>
    public static uint Modifiers(ushort mod)
    {
        uint modifiers = 0;
        if ((mod & (ModLCtrl | ModRCtrl)) != 0) modifiers |= HotkeyGesture.ModControl;
        if ((mod & (ModLAlt | ModRAlt)) != 0) modifiers |= HotkeyGesture.ModAlt;
        if ((mod & (ModLShift | ModRShift)) != 0) modifiers |= HotkeyGesture.ModShift;
        if ((mod & (ModLGui | ModRGui)) != 0) modifiers |= HotkeyGesture.ModWin;
        return modifiers;
    }

    // The letters, the digits of the top row and the punctuation: what a layout moves around.
    private static bool IsCharacterKey(uint scancode) =>
        scancode is >= ScanA and <= Scan0 or >= ScanMinus and <= ScanSlash || scancode == ScanNonUsBackslash;

    private static uint CharacterKey(uint scancode, uint keycode)
    {
        if ((keycode & ScancodeMask) == 0)
        {
            switch (keycode)
            {
                case >= 'a' and <= 'z':
                    return keycode - 'a' + 'A';
                case >= 'A' and <= 'Z' or >= '0' and <= '9':
                    return keycode;
                case > 0 and < 0x80 when PunctuationCharacters.IndexOf((char)keycode) is var at and >= 0:
                    return PunctuationByPlace[at];
            }
        }

        // A character of its own (an accented letter, a symbol typed with Shift): the key is named by its place on a US keyboard.
        return scancode switch
        {
            >= ScanA and <= ScanZ => 'A' + (scancode - ScanA),
            Scan0 => '0',
            >= Scan1 and < Scan0 => '1' + (scancode - Scan1),
            ScanNonUsBackslash => VkOem102,
            _ => PunctuationByPlace[scancode - ScanMinus],
        };
    }

    // Num Lock on: the digits and the decimal point. Off: the keys printed under them, as Windows reads them then.
    private static uint Keypad(uint scancode, bool numLock)
    {
        if (numLock)
        {
            return scancode switch
            {
                ScanKp0 => VkNumpad0,
                ScanKpPeriod => VkDecimal,
                _ => VkNumpad0 + 1 + (scancode - ScanKp1),
            };
        }

        return scancode switch
        {
            ScanKp0 => VkInsert,
            ScanKpPeriod => VkDelete,
            ScanKp1 => Vk.End,
            ScanKp1 + 1 => Vk.Down,
            ScanKp1 + 2 => Vk.PageDown,
            ScanKp1 + 3 => Vk.Left,
            ScanKp1 + 4 => VkClear,
            ScanKp1 + 5 => Vk.Right,
            ScanKp1 + 6 => Vk.Home,
            ScanKp1 + 7 => Vk.Up,
            _ => Vk.PageUp,
        };
    }
}
