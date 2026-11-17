using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary>Which of the window sizes of the settings a screen can take.</summary>
internal static class WindowZoomFit
{
    /// <summary>The smallest zoom: the window is never drawn below its design size, even on a screen that cannot hold it.</summary>
    public const int Floor = 100;

    /// <summary>
    /// The largest of <see cref="GeneralSettings.WindowZooms"/> that is no larger than <paramref name="wanted"/> and whose whole
    /// window, frame included, fits the work area; <see cref="Floor"/> if none does.
    /// </summary>
    /// <param name="wanted">The zoom of the settings.</param>
    /// <param name="outerSizeAt">The outer size of the window, in pixels, at a zoom on this screen.</param>
    /// <param name="workWidth">What the taskbar leaves of the screen, in pixels.</param>
    /// <param name="workHeight">What the taskbar leaves of the screen, in pixels.</param>
    public static int Choose(int wanted, Func<int, (int W, int H)> outerSizeAt, int workWidth, int workHeight)
    {
        var best = Floor;
        foreach (var zoom in GeneralSettings.WindowZooms)
        {
            if (zoom <= Floor || zoom > wanted || zoom <= best)
            {
                continue;
            }

            var (width, height) = outerSizeAt(zoom);
            if (width <= workWidth && height <= workHeight)
            {
                best = zoom;
            }
        }

        return best;
    }
}
