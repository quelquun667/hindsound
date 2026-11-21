using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Unix.Desktop;

namespace Hindsound.Mac.Desktop;

/// <summary>
/// Opening and revealing through <c>/usr/bin/open</c>, which every macOS has: <c>open target</c> opens a file or a folder
/// with the app the system gives for it, <c>open -R file</c> shows the files selected in the Finder. Never waited on by the
/// loop: what <c>open</c> answers later goes to the log (<see cref="ExternalTools"/>).
/// </summary>
internal sealed class MacOpen(Action<string> log, string program = MacOpen.Program) : IFileOpener, IFolderShell
{
    public const string Program = "/usr/bin/open";

    public const int Ok = 0;

    // E_FAIL: the caller logs an HRESULT.
    public const int Failed = unchecked((int)0x80004005);

    public string? Open(string target) => ExternalTools.Start(program, OpenArguments(target), null, log, "open");

    public int ShowSelected(string folder, IReadOnlyList<string> files)
    {
        if (ExternalTools.Start(program, RevealArguments(folder, files), null, log, "open -R") is not { } error)
        {
            return Ok;
        }

        log("show in folder: " + error);
        return Failed;
    }

    /// <summary>The arguments of <c>open</c> for a target: <c>open</c> reads no <c>--</c>, and a name that starts with a dash would pass for an option.</summary>
    public static IReadOnlyList<string> OpenArguments(string target) => [Safe(target)];

    /// <summary>The files selected in their folder (<c>-R</c>); the folder itself opened when no file is given.</summary>
    public static IReadOnlyList<string> RevealArguments(string folder, IReadOnlyList<string> files) =>
        files.Count == 0 ? [Safe(folder)] : ["-R", .. files.Select(Safe)];

    private static string Safe(string path) => path.StartsWith('-') ? "./" + path : path;
}
