using Hindsound.App;

namespace Hindsound.Linux.Desktop;

/// <summary>A program that puts text on the clipboard, and its arguments.</summary>
internal sealed record ClipboardTool(string Program, IReadOnlyList<string> Arguments)
{
    /// <summary>
    /// The program for this session. Under Wayland, wl-copy; the X11 tools after it reach the clipboard through XWayland,
    /// which the desktop keeps in step. Under X11, xclip, which names the type of what it holds; xsel last, which cannot,
    /// so a file manager pastes its paths as text. Null when no session or no tool is there: the app says "Not copied".
    /// </summary>
    public static ClipboardTool? Choose(string? waylandDisplay, string? x11Display, Func<string, string?> find)
    {
        var candidates = new List<(string Name, string[] Arguments)>();
        if (!string.IsNullOrEmpty(waylandDisplay))
        {
            candidates.Add(("wl-copy", ["--type", UriList.MimeType]));
        }

        if (!string.IsNullOrEmpty(x11Display))
        {
            candidates.Add(("xclip", ["-selection", "clipboard", "-t", UriList.MimeType]));
            candidates.Add(("xsel", ["--clipboard", "--input"]));
        }

        foreach (var (name, arguments) in candidates)
        {
            if (find(name) is { } program)
            {
                return new ClipboardTool(program, arguments);
            }
        }

        return null;
    }
}

/// <summary>
/// The clip on the clipboard as text/uri-list, through the clipboard program of the session. The program forks to hold the
/// clipboard, so the loop never waits for it: a program that starts and takes the list counts as done, and a later failure
/// goes to the log.
/// </summary>
internal sealed class LinuxClipboard(Action<string> log) : IClipboardFiles
{
    private bool _noToolSaid;

    public bool TrySet(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        var tool = ClipboardTool.Choose(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            ExternalTools.Find);
        if (tool is null)
        {
            if (!_noToolSaid)
            {
                _noToolSaid = true;
                log("clipboard: no wl-copy, xclip or xsel for this session");
            }

            return false;
        }

        var error = ExternalTools.Start(tool.Program, tool.Arguments, UriList.Of(paths), log, "clipboard");
        if (error is not null)
        {
            log(error);
            return false;
        }

        return true;
    }
}
