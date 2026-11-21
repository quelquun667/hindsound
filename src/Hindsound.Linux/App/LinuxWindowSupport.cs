using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Hindsound.Linux.Window;
using Hindsound.Linux.Window.Text;
using Hindsound.Unix.Window;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.App;

/// <summary>
/// Whether the settings window can be drawn here: SDL 3 loads (the copy next to the program first), and the text of the
/// window starts, which needs FreeType, HarfBuzz, fontconfig and the four fonts shipped with the app. Checked once per
/// process; without it the app runs with its icon and its notification only, and the log says what was missing. The log
/// never names a path: it says whether SDL and the fonts came with the app or from elsewhere.
/// </summary>
internal static class LinuxWindowSupport
{
    private static readonly object Gate = new();
    private static bool? s_available;

    [SupportedOSPlatform("linux")]
    public static bool Available(Action<string> log)
    {
        lock (Gate)
        {
            s_available ??= Check(log);
            return s_available.Value;
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool Check(Action<string> log)
    {
        if (!SdlLibrary.TryLoad())
        {
            foreach (var line in NoSdlLines(File.Exists(SdlLibrary.ProgramCandidates[0]), SdlLibrary.LastError))
            {
                log(line);
            }

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

        log(FoundLine(SdlLibrary.LoadedFrom, Environment.GetEnvironmentVariable(FreeTypeText.FontDirectoryVariable)));
        return true;
    }

    /// <summary>
    /// Why SDL did not load, as the loader said it, for the log: every folder of a path is dropped (it would carry the name of
    /// the user) and the lines of the message are joined.
    /// </summary>
    internal static string Reason(string message) =>
        Regex.Replace(Regex.Replace(message, @"(?<![\w.])/(?:[^/\s:'`()]+/)+", ""), @"\s+", " ").Trim();

    /// <summary>The lines of a system where SDL does not load: the line below, then why, when the loader said it.</summary>
    internal static IEnumerable<string> NoSdlLines(bool copyNextToProgram, string? loaderError)
    {
        yield return NoSdlLine(copyNextToProgram);
        if (!string.IsNullOrWhiteSpace(loaderError))
        {
            yield return "window: " + Reason(loaderError);
        }
    }

    /// <summary>The line of a system where SDL does not load: whether the copy shipped next to the program is there or not.</summary>
    internal static string NoSdlLine(bool copyNextToProgram) => copyNextToProgram
        ? "window: none, the " + SdlLibrary.SystemName + " next to the program cannot be loaded, nor one of the system"
        : "window: none, " + SdlLibrary.SystemName + " is not next to the program and cannot be loaded from the system";

    /// <summary>
    /// The line of a window that can be drawn: SDL from the copy next to the program (a path) or from the system (a bare
    /// name), and the fonts from the folder the variable names or from the one next to the program. A package is checked by it.
    /// </summary>
    internal static string FoundLine(string? sdlLoadedFrom, string? fontVariable) =>
        "window: SDL " + (sdlLoadedFrom is not null && Path.IsPathRooted(sdlLoadedFrom) ? "next to the program" : "from the system")
        + ", fonts " + (string.IsNullOrEmpty(fontVariable) ? "next to the program" : "from " + FreeTypeText.FontDirectoryVariable);
}
