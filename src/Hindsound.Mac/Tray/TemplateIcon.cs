using Hindsound.Ui;

namespace Hindsound.Mac.Tray;

/// <summary>
/// The pixels of the icon of the menu bar: a template image, black with the shape in its alpha, which macOS tints itself
/// for a light or a dark bar. So the state shows by its shape only (<see cref="TrayIconShape"/>), never by a colour. Pure:
/// tested on every system.
/// </summary>
internal static class TemplateIcon
{
    /// <summary>The side of the icon in points, as the status items of macOS are drawn.</summary>
    public const int Points = 18;

    /// <summary>Pixels per point: drawn for a Retina screen, which macOS scales down for the others.</summary>
    public const int Scale = 2;

    /// <summary>The side of the icon in pixels.</summary>
    public const int Pixels = Points * Scale;

    /// <summary>RGBA, row by row from the top, 4 bytes a pixel: colour 0, alpha the coverage of the shape.</summary>
    public static byte[] Rgba(TrayState state, int size = Pixels)
    {
        var coverage = TrayIconShape.Coverage(state, size);
        var bytes = new byte[size * size * 4];
        for (var i = 0; i < coverage.Length; i++)
        {
            bytes[i * 4 + 3] = (byte)MathF.Round(Hindsound.Ui.Pixels.Clamp(coverage[i]) * 255f);
        }

        return bytes;
    }
}
