using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Unix.Window;

/// <summary>
/// The calls of SDL 3.2 for the keyboard text, the clipboard text and the folder dialog. Every one of them is in SDL 3.2.0.
/// The library is found by the same name as the rest of the window's calls, so the resolver that looks next to the program
/// first applies to these too. SDL's bool is one byte (stdbool).
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class SdlNative
{
    private const string InputLibrary = "libSDL3.so.0";

    /// <summary>SDL_Rect.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputRect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    [LibraryImport(InputLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_StartTextInput(nint window);

    [LibraryImport(InputLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_StopTextInput(nint window);

    /// <param name="cursor">Where the caret is, in pixels from the left of <paramref name="rect"/>.</param>
    [LibraryImport(InputLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_SetTextInputArea(nint window, InputRect* rect, int cursor);

    [LibraryImport(InputLibrary, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_SetClipboardText(string text);

    /// <summary>A copy SDL allocates, empty when the clipboard holds no text: free it with <see cref="FreeInputMemory"/>.</summary>
    [LibraryImport(InputLibrary)]
    internal static partial byte* SDL_GetClipboardText();

    /// <summary>SDL_free, under a name of its own here.</summary>
    [LibraryImport(InputLibrary, EntryPoint = "SDL_free")]
    internal static partial void FreeInputMemory(void* memory);

    /// <summary>
    /// Opens the folder dialog of the desktop (the XDG portal, else zenity) and returns at once. <paramref name="callback"/>
    /// gets the folders chosen, an empty list when the user backs out, or null on an error: it may come on another thread,
    /// and through the portal only while the events are pumped.
    /// </summary>
    [LibraryImport(InputLibrary, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void SDL_ShowOpenFolderDialog(
        delegate* unmanaged[Cdecl]<nint, byte**, int, void> callback, nint userdata, nint window, string? defaultLocation,
        [MarshalAs(UnmanagedType.U1)] bool allowMany);

    /// <summary>SDL_GetError: the message of the last error on this thread.</summary>
    [LibraryImport(InputLibrary, EntryPoint = "SDL_GetError")]
    private static partial byte* GetInputError();

    internal static string InputError() => Marshal.PtrToStringUTF8((nint)GetInputError()) ?? string.Empty;
}
