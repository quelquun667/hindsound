using Hindsound.App;

namespace Hindsound.Linux.Desktop;

/// <summary>Opens a file or a folder with what the desktop gives for it, through xdg-open. Never waits for it.</summary>
internal sealed class XdgFileOpener(Action<string> log) : IFileOpener
{
    public string? Open(string target)
    {
        if (ExternalTools.Find("xdg-open") is not { } program)
        {
            return "no xdg-open on this system";
        }

        // xdg-open reads no "--": a relative name that starts with a dash would pass for an option.
        var safe = target.StartsWith('-') ? "./" + target : target;
        return ExternalTools.Start(program, [safe], null, log, "xdg-open");
    }
}
