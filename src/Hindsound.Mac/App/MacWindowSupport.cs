using System.Runtime.Versioning;
using Hindsound.Mac.Window;
using Hindsound.Unix.Window;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Mac.App;

/// <summary>
/// Whether the settings window can be drawn here: the libSDL3.0.dylib of the bundle loads, and the text starts (FreeType
/// and HarfBuzz of the bundle, CoreText, the four fonts in Contents/Resources/fonts). Checked once per process; the log
/// says what was missing, without a path.
/// </summary>
internal static class MacWindowSupport
{
    private static readonly object Gate = new();
    private static bool? s_available;

    [SupportedOSPlatform("macos")]
    public static bool Available(Action<string> log)
    {
        lock (Gate)
        {
            s_available ??= Check(log);
            return s_available.Value;
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool Check(Action<string> log)
    {
        if (!SdlLibrary.TryLoad())
        {
            log("window: none, " + UnixLibraries.MacFiles[SdlLibrary.SystemName] + " is not in the bundle or cannot be loaded");
            return false;
        }

        try
        {
            using var text = MacText.Create();
        }
        catch (TextUnavailableException ex)
        {
            log("window: none, " + ex.Message);
            return false;
        }

        log(FoundLine(SdlLibrary.LoadedFrom, AppContext.BaseDirectory, Environment.GetEnvironmentVariable(FreeTypeText.FontDirectoryVariable)));
        return true;
    }

    /// <summary>The line of a window that can be drawn: SDL from the Frameworks of the bundle or from next to the program, and the fonts.</summary>
    internal static string FoundLine(string? sdlLoadedFrom, string programDirectory, string? fontVariable) =>
        "window: SDL " + (sdlLoadedFrom is not null && sdlLoadedFrom.Contains("/Frameworks/", StringComparison.Ordinal)
            ? "from the bundle" : "next to the program")
        + ", fonts " + (!string.IsNullOrEmpty(fontVariable) ? "from " + FreeTypeText.FontDirectoryVariable
            : MacText.InBundle(programDirectory) ? "from the bundle" : "next to the program");
}
