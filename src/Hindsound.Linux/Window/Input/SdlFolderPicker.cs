using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Linux.Window.Input;

/// <summary>
/// "Choose the clips folder" through SDL's folder dialog: the desktop's own (the XDG portal, else zenity). The call waits for
/// the answer as the modal dialog of Windows does, pumping the window's events meanwhile, so the window keeps painting and
/// the portal's answer, which comes in with the events, gets through.
/// </summary>
internal static unsafe class SdlFolderPicker
{
    /// <summary>The folder chosen, or null when the user backs out or no dialog could be shown (see <paramref name="error"/>).</summary>
    /// <param name="window">The SDL window the dialog belongs to, or 0.</param>
    /// <param name="startFolder">Where the dialog opens, when that folder exists.</param>
    /// <param name="pumpOnce">
    /// Pumps the window's events once, waiting a little for one (SDL_WaitEventTimeout): called until the dialog answers.
    /// If it throws, the wait ends with that exception, and the dialog's answer, if one ever comes, is dropped.
    /// </param>
    /// <param name="error">SDL's message when the dialog failed, else null.</param>
    [SupportedOSPlatform("linux")]
    public static string? Pick(nint window, string startFolder, Action pumpOnce, out string? error)
    {
        var answer = new Answer();
        var handle = GCHandle.Alloc(answer);
        var answered = false;
        try
        {
            var start = !string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder) ? startFolder : null;
            SdlNative.SDL_ShowOpenFolderDialog(&OnAnswer, GCHandle.ToIntPtr(handle), window, start, allowMany: false);
            while (!answer.Done)
            {
                pumpOnce();
            }

            answered = true;
        }
        finally
        {
            // A dialog still open would call back into a freed handle: it keeps its handle, and the wait leaks it.
            if (answered)
            {
                handle.Free();
            }
        }

        error = answer.Error;
        return answer.Folder;
    }

    [SupportedOSPlatform("linux")]
    public static string? Pick(nint window, string startFolder, Action pumpOnce) => Pick(window, startFolder, pumpOnce, out _);

    /// <summary>What the dialog said. Written by the callback, perhaps on another thread, then <see cref="Done"/> last.</summary>
    internal sealed class Answer
    {
        private volatile bool _done;

        public string? Folder { get; set; }

        public string? Error { get; set; }

        public bool Done
        {
            get => _done;
            set => _done = value;
        }

        /// <summary>Reads SDL's list: null is an error, an empty list is the user backing out.</summary>
        public void Take(byte** list, Func<string> error)
        {
            try
            {
                if (list is null)
                {
                    Error = error();
                }
                else if (list[0] is not null)
                {
                    Folder = Marshal.PtrToStringUTF8((nint)list[0]);
                }
            }
            finally
            {
                Done = true;
            }
        }
    }

    [SupportedOSPlatform("linux")]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAnswer(nint userdata, byte** list, int filter)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is Answer answer)
            {
                answer.Take(list, SdlNative.InputError);
            }
        }
        catch
        {
            // Nothing may cross back into SDL. Take has marked the answer done whatever failed in it.
        }
    }
}
