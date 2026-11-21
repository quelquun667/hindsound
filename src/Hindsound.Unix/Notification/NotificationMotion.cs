namespace Hindsound.Unix.Notification;

/// <summary>
/// How the drawn notification moves, with the times of the Windows one: it comes out of the screen edge in 140 ms (ease
/// out), its bars rise in 200 ms, it holds until 2.74 s, then goes back into the edge in 180 ms. Replaced while it shows, it
/// does not come out again: the hold starts over.
/// </summary>
internal static class NotificationMotion
{
    public const int EnterMs = 140, BarsMs = 200, HoldEndMs = EnterMs + 2600, TotalMs = HoldEndMs + 180;

    /// <summary>The time between two frames while it moves, and between two looks at the clicks while it holds.</summary>
    public const int FrameMs = 16, HoldTickMs = 50;

    /// <summary>The part of the width that shows, from 0 to 1, <paramref name="elapsedMs"/> after it started.</summary>
    public static double Visible(double elapsedMs)
    {
        if (elapsedMs < EnterMs)
        {
            var t = 1 - elapsedMs / EnterMs;
            return 1 - t * t * t;
        }

        if (elapsedMs < HoldEndMs)
        {
            return 1;
        }

        var u = Math.Min(1, (elapsedMs - HoldEndMs) / (TotalMs - HoldEndMs));
        return 1 - u * u * u;
    }

    /// <summary>The height of the bars, from 0.05 to 1.</summary>
    public static float BarsScale(double elapsedMs) => elapsedMs < BarsMs ? 0.05f + 0.95f * (float)(elapsedMs / BarsMs) : 1;

    /// <summary>The time to wait before the next frame, or before the end of the hold.</summary>
    public static int NextTick(double elapsedMs) =>
        elapsedMs >= BarsMs && elapsedMs < HoldEndMs ? (int)Math.Clamp(HoldEndMs - elapsedMs, 1, HoldTickMs) : FrameMs;

    /// <summary>
    /// The columns of the picture that show, and where they go in a window as wide as the picture, placed where it ends up:
    /// on the right edge, its first columns at the right of the window (the stripe comes out last); on the left edge,
    /// mirrored, its last columns at the left. At least one column shows, as on Windows.
    /// </summary>
    public static (int SourceX, int DestinationX, int Width) Slice(int width, double visible, bool leftEdge)
    {
        var shown = Math.Clamp((int)Math.Round(width * visible), 1, width);
        return leftEdge ? (width - shown, 0, shown) : (0, width - shown, shown);
    }
}
