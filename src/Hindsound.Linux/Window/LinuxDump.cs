using Hindsound.Linux.Window.Text;
using Hindsound.Ui.Window;
using Hindsound.Unix.Window;

namespace Hindsound.Linux.Window;

/// <summary><c>--dump-settings</c> on Linux: the shared <see cref="UnixDump"/>, with the text of Linux.</summary>
internal static class LinuxDump
{
    /// <summary>Writes the pages with the text of the window; fails when that text cannot start (a library or a font missing).</summary>
    public static int Write(string folder, double scale, Action<string> log) => UnixDump.Write(folder, scale, LinuxText.Create, log);

    /// <summary>Writes the pages with <paramref name="text"/>, which every canvas shares and none disposes.</summary>
    internal static int Write(string folder, double scale, ICanvasText text, Action<string> log) => UnixDump.Write(folder, scale, text, log);
}
