using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>
/// What the Windows text (DirectWrite, the faces of the Windows app: Segoe UI 400 and 600, Bahnschrift SemiBold
/// SemiCondensed, Cascadia Mono) measured for a fixed set of strings at the sizes the pages use and their scaled sizes,
/// recorded on Windows 11 on 18/11/2026: the yardstick of the Linux text, whose fonts are chosen to keep these widths.
/// </summary>
internal static class WindowsTextWidths
{
    public static readonly string[] Texts =
    [
        "Settings",
        "Library",
        "Save the last 30 seconds",
        "Start with the session",
        "The quick brown fox jumps over the lazy dog",
        "0123456789",
        "Hindsound",
        "AVATAR Wave",
        "Keep 5 minutes in memory",
        "a b ",
        "00:12:34",
    ];

    public static readonly Row[] Rows =
    [
        new(Face.Body, 12f, 16, [42.639f, 35.865f, 128.783f, 112.107f, 237.885f, 64.688f, 58.055f, 73.09f, 141.938f, 19.734f, 44.016f]),
        new(Face.Body, 13f, 17, [46.192f, 38.854f, 139.515f, 121.45f, 257.709f, 70.078f, 62.893f, 79.181f, 153.766f, 21.379f, 47.684f]),
        new(Face.Body, 14f, 19, [49.745f, 41.843f, 150.247f, 130.792f, 277.532f, 75.469f, 67.73f, 85.271f, 165.594f, 23.023f, 51.352f]),
        new(Face.Body, 16.25f, 21, [56.852f, 47.82f, 171.711f, 149.477f, 317.18f, 86.25f, 77.406f, 97.453f, 189.25f, 26.313f, 58.688f]),
        new(Face.Body, 19.5f, 28, [71.064f, 59.775f, 214.639f, 186.846f, 396.475f, 107.813f, 96.758f, 121.816f, 236.563f, 32.891f, 73.359f]),
        new(Face.Body, 26f, 36, [92.384f, 77.708f, 279.03f, 242.899f, 515.417f, 140.156f, 125.785f, 158.361f, 307.531f, 42.758f, 95.367f]),
        new(Face.Medium, 12f, 16, [44.121f, 37.957f, 132.053f, 116.232f, 245.156f, 64.881f, 59.766f, 75.967f, 146.566f, 20.092f, 44.18f]),
        new(Face.Medium, 13f, 17, [47.798f, 41.12f, 143.057f, 125.919f, 265.586f, 70.288f, 64.746f, 82.297f, 158.78f, 21.766f, 47.861f]),
        new(Face.Medium, 14f, 19, [51.475f, 44.283f, 154.062f, 135.605f, 286.016f, 75.694f, 69.727f, 88.628f, 170.994f, 23.44f, 51.543f]),
        new(Face.Medium, 16f, 21, [58.828f, 50.609f, 176.07f, 154.977f, 326.875f, 86.508f, 79.688f, 101.289f, 195.422f, 26.789f, 58.906f]),
        new(Face.Medium, 16.25f, 21, [58.828f, 50.609f, 176.07f, 154.977f, 326.875f, 86.508f, 79.688f, 101.289f, 195.422f, 26.789f, 58.906f]),
        new(Face.Medium, 26f, 36, [95.596f, 82.24f, 286.114f, 251.837f, 531.172f, 140.575f, 129.492f, 164.595f, 317.561f, 43.532f, 95.723f]),
        new(Face.Title, 18f, 22, [57.278f, 50.528f, 173.145f, 152.772f, 309.815f, 81.193f, 72.861f, 94.069f, 186.812f, 25.436f, 54.65f]),
        new(Face.Title, 20f, 24, [63.643f, 56.143f, 192.383f, 169.746f, 344.238f, 90.215f, 80.957f, 104.522f, 207.568f, 28.262f, 60.723f]),
        new(Face.Title, 22f, 27, [70.007f, 61.757f, 211.621f, 186.721f, 378.662f, 99.236f, 89.053f, 114.974f, 228.325f, 31.088f, 66.795f]),
        new(Face.Title, 27.5f, 34, [89.1f, 78.6f, 269.336f, 237.645f, 481.934f, 126.301f, 113.34f, 146.33f, 290.596f, 39.566f, 85.012f]),
        new(Face.Title, 44f, 53, [140.014f, 123.514f, 423.242f, 373.441f, 757.324f, 198.473f, 178.106f, 229.947f, 456.65f, 62.176f, 133.59f]),
        new(Face.Mono, 11f, 15, [51.563f, 45.117f, 154.688f, 141.797f, 277.148f, 64.453f, 58.008f, 70.898f, 154.688f, 25.781f, 51.563f]),
        new(Face.Mono, 12f, 16, [56.25f, 49.219f, 168.75f, 154.688f, 302.344f, 70.313f, 63.281f, 77.344f, 168.75f, 28.125f, 56.25f]),
        new(Face.Mono, 13f, 17, [60.938f, 53.32f, 182.813f, 167.578f, 327.539f, 76.172f, 68.555f, 83.789f, 182.813f, 30.469f, 60.938f]),
        new(Face.Mono, 14f, 18, [65.625f, 57.422f, 196.875f, 180.469f, 352.734f, 82.031f, 73.828f, 90.234f, 196.875f, 32.813f, 65.625f]),
        new(Face.Mono, 15f, 20, [70.313f, 61.523f, 210.938f, 193.359f, 377.93f, 87.891f, 79.102f, 96.68f, 210.938f, 35.156f, 70.313f]),
        new(Face.Mono, 16.25f, 21, [75f, 65.625f, 225f, 206.25f, 403.125f, 93.75f, 84.375f, 103.125f, 225f, 37.5f, 75f]),
        new(Face.Mono, 26f, 34, [121.875f, 106.641f, 365.625f, 335.156f, 655.078f, 152.344f, 137.109f, 167.578f, 365.625f, 60.938f, 121.875f]),
    ];

    /// <param name="Size">The size of the FontSpec, in physical pixels.</param>
    /// <param name="LineHeight">ICanvasText.LineHeight on Windows.</param>
    /// <param name="Widths">ICanvasText.Measure of each of <see cref="Texts"/>, in order.</param>
    public sealed record Row(Face Face, float Size, int LineHeight, float[] Widths);
}
