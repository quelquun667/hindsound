using Hindsound.Ui;

namespace Hindsound.Linux.Tray;

/// <summary>One image of the icon, as the IconPixmap property of StatusNotifierItem carries it (D-Bus type (iiay)).</summary>
/// <param name="Argb">Width times height pixels, each as the bytes A, R, G, B: ARGB32 in network byte order.</param>
internal sealed record IconPixmap(int Width, int Height, byte[] Argb);

/// <summary>The images of the icon, drawn from the same shapes as on Windows. Pure: tested without a bus.</summary>
internal static class IconPixmaps
{
    /// <summary>The sizes offered: the host picks the one closest to its panel, and scales none of them up.</summary>
    public static readonly int[] Sizes = [22, 32, 48];

    public static IReadOnlyList<IconPixmap> Of(TrayState state, uint accent) => [.. Sizes.Select(size => Of(state, accent, size))];

    public static IconPixmap Of(TrayState state, uint accent, int size)
    {
        var pixels = TrayIconShape.Compose(state, size, accent);
        var bytes = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            bytes[i * 4] = (byte)(pixel >> 24);
            bytes[i * 4 + 1] = (byte)(pixel >> 16);
            bytes[i * 4 + 2] = (byte)(pixel >> 8);
            bytes[i * 4 + 3] = (byte)pixel;
        }

        return new(size, size, bytes);
    }
}
