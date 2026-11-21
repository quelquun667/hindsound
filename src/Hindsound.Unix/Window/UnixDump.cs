using System.Runtime.InteropServices;
using Hindsound.Ui.Window;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Unix.Window;

/// <summary>
/// <c>--dump-settings</c> on Linux and macOS: every page of the settings as a PNG file, drawn by the portable pages on a
/// bitmap of this class with the text of the window, the features of this system included. Shows nothing.
/// </summary>
internal static unsafe class UnixDump
{
    private const int ExitFailed = 3;

    /// <summary>Writes the pages with the text of the window; fails when that text cannot start (a library or a font missing).</summary>
    /// <param name="newText">The text of the window of this system (its font fallback included).</param>
    public static int Write(string folder, double scale, Func<FreeTypeText> newText, Action<string> log)
    {
        FreeTypeText text;
        try
        {
            text = newText();
        }
        catch (TextUnavailableException ex)
        {
            log("dump failed: " + ex.Message);
            return ExitFailed;
        }

        using (text)
        {
            return Write(folder, scale, text, log);
        }
    }

    /// <summary>Writes the pages with <paramref name="text"/>, which every canvas shares and none disposes.</summary>
    internal static int Write(string folder, double scale, ICanvasText text, Action<string> log)
    {
        try
        {
            SettingsDump.Write(folder, scale, (width, height, canvasScale) => new BitmapCanvas(width, height, canvasScale, text));
            return 0;
        }
        catch (Exception ex)
        {
            log("dump failed: " + ex);
            return ExitFailed;
        }
    }

    /// <summary>A canvas on a bitmap of its own, freed with it; the text is the dump's, not the canvas's.</summary>
    internal sealed class BitmapCanvas(int width, int height, double scale, ICanvasText text)
        : PixelCanvas(Allocate(width, height), width, height, scale, text)
    {
        private bool _freed;

        private static uint* Allocate(int width, int height) =>
            (uint*)NativeMemory.AllocZeroed((nuint)Math.Max(1, width) * (nuint)Math.Max(1, height), sizeof(uint));

        // Not the base: it would dispose the text every canvas of the dump shares.
        protected override void Dispose(bool disposing)
        {
            if (!_freed)
            {
                _freed = true;
                NativeMemory.Free(Bits);
            }
        }
    }
}
