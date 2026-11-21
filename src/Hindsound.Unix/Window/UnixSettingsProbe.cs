using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Unix.Window.Input;

namespace Hindsound.Unix.Window;

/// <summary>
/// What <c>--settings-probe</c> reads and sends on Linux: the windows and bitmaps the process holds, and events pushed to the
/// window's own queue. Never the real keyboard, and never another program's window: the PC may be in use.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class UnixSettingsProbe : ISettingsProbe
{
    private const uint ScancodeMask = 1u << 30;

    /// <summary>The bitmaps of the windows: what a window that leaks would leave behind.</summary>
    public uint DrawingObjects() => (uint)Math.Max(0, UnixSettingsWindow.LiveBitmaps);

    public uint WindowObjects() => (uint)Math.Max(0, UnixSettingsWindow.OpenWindows);

    public void Key(ISettingsWindow window, uint vk)
    {
        var (scancode, keycode) = KeyFor(vk) ?? throw new ArgumentException($"no SDL key gives the virtual key 0x{vk:X2}", nameof(vk));
        if (!Of(window).PushKey(scancode, keycode))
        {
            throw new InvalidOperationException("SDL's queue refused the key");
        }
    }

    public void KeyInTextField(ISettingsWindow window, uint vk) => Of(window).KeyInField(vk);

    public void TypeInTextField(ISettingsWindow window, string text) => Of(window).TypeInField(text);

    public void Activate(ISettingsWindow window) => Of(window).PushWindowEvent(SdlNative.EventWindowFocusGained);

    public void ScaleChange(ISettingsWindow window, uint dpi) => Of(window).ProbeScaleChange(dpi);

    public string ClientSize(ISettingsWindow window) => Of(window).ClientSizeText();

    public string IconWidths(ISettingsWindow window)
    {
        var side = Of(window).IconSideNow;
        return $"({side}, {side})";
    }

    public bool FolderDialogUsable() => SdlLibrary.TryLoad();

    /// <summary>
    /// A scancode and a keycode SDL could send for <paramref name="vk"/> with no modifier, as <see cref="SdlKeys"/> reads them:
    /// the keys that type nothing first, then those that type a character. Null if no key gives it.
    /// </summary>
    public static (uint Scancode, uint Keycode)? KeyFor(uint vk)
    {
        for (uint scancode = 1; scancode < 512; scancode++)
        {
            if (SdlKeys.VirtualKey(scancode, scancode | ScancodeMask, 0) == vk)
            {
                return (scancode, scancode | ScancodeMask);
            }
        }

        for (uint keycode = 0x08; keycode < 0x7F; keycode++)
        {
            for (uint scancode = 1; scancode < 512; scancode++)
            {
                if (SdlKeys.VirtualKey(scancode, keycode, 0) == vk)
                {
                    return (scancode, keycode);
                }
            }
        }

        return null;
    }

    private static UnixSettingsWindow Of(ISettingsWindow window) =>
        window as UnixSettingsWindow ?? throw new ArgumentException("not a window of the Linux shell", nameof(window));
}
