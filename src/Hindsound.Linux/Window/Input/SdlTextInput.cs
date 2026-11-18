using System.Runtime.Versioning;

namespace Hindsound.Linux.Window.Input;

/// <summary>
/// The text input of one SDL window: on while a text field has the keyboard, so typed characters and the input method's
/// composition come as text events, and off otherwise, so a key held on a page does not open the input method. The area
/// tells the input method where to put its candidate list: under the field, at the caret.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class SdlTextInput
{
    private readonly nint _window;
    private bool _on;
    private (int X, int Y, int W, int H, int Cursor) _area = (-1, -1, -1, -1, -1);

    public SdlTextInput(nint window) => _window = window;

    public bool On => _on;

    /// <summary>Follows the field: call it after each key, text and paint. Calls SDL only when something changed.</summary>
    /// <param name="wanted">A field is shown and has the keyboard (<see cref="LinuxTextField.WantsTextInput"/>).</param>
    /// <param name="x">
    /// The field's box in window coordinates, the unit of SDL_SetTextInputArea: the physical pixels of
    /// <see cref="LinuxTextField.InputArea"/> divided by the pixel density of the window, as the window does.
    /// </param>
    /// <param name="cursor">The caret, in window coordinates from the left of the box.</param>
    public void Update(bool wanted, int x, int y, int w, int h, int cursor)
    {
        if (wanted && (x, y, w, h, cursor) != _area)
        {
            _area = (x, y, w, h, cursor);
            var rect = new SdlNative.InputRect { X = x, Y = y, W = w, H = h };
            SdlNative.SDL_SetTextInputArea(_window, &rect, cursor);
        }

        if (wanted != _on)
        {
            _on = wanted;
            if (wanted)
            {
                SdlNative.SDL_StartTextInput(_window);
            }
            else
            {
                SdlNative.SDL_StopTextInput(_window);
            }
        }
    }
}

/// <summary>Text on the desktop's clipboard, through SDL (the window must exist: SDL's video is on).</summary>
[SupportedOSPlatform("linux")]
internal static unsafe class SdlClipboardText
{
    /// <summary>The text on the clipboard, or null when it holds none.</summary>
    public static string? Get()
    {
        var text = SdlNative.SDL_GetClipboardText();
        if (text is null)
        {
            return null;
        }

        try
        {
            return text[0] == 0 ? null : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)text);
        }
        finally
        {
            SdlNative.FreeInputMemory(text);
        }
    }

    /// <summary>Puts text on the clipboard. False when SDL could not.</summary>
    public static bool Set(string text) => SdlNative.SDL_SetClipboardText(text);
}
