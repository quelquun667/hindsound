using System.Runtime.Versioning;
using Hindsound.Linux.Window;
using Hindsound.Linux.Window.Text;

namespace Hindsound.Linux.App;

/// <summary>
/// Whether the settings window can be drawn here: SDL 3 loads (the copy next to the program first), and the text of the
/// window starts, which needs FreeType, HarfBuzz, fontconfig and the four fonts shipped with the app. Checked once per
/// process; without it the app runs with its icon and its notification only, and the log says what was missing.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxWindowSupport
{
    private static readonly object Gate = new();
    private static bool? s_available;

    public static bool Available(Action<string> log)
    {
        lock (Gate)
        {
            s_available ??= Check(log);
            return s_available.Value;
        }
    }

    private static bool Check(Action<string> log)
    {
        if (!SdlLibrary.TryLoad())
        {
            log("window: none, SDL cannot be loaded: " + SdlLibrary.LastError);
            return false;
        }

        try
        {
            using var text = new FreeTypeText();
        }
        catch (TextUnavailableException ex)
        {
            log("window: none, " + ex.Message);
            return false;
        }

        log("window: SDL from " + SdlLibrary.LoadedFrom);
        return true;
    }
}
