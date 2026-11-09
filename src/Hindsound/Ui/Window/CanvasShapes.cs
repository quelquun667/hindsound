namespace Hindsound.Ui.Window;

/// <summary>Shapes made of the canvas's own calls: nothing here knows what the canvas draws on.</summary>
internal static class CanvasShapes
{
    /// <summary>A star has five points and five dents, so ten corners.</summary>
    public const int StarCorners = 10;

    /// <summary>How far in the dents are, as a share of the radius of the points: the proportions the mockup was drawn with.</summary>
    public const float StarDent = 0.42f;

    /// <summary>The line of an outlined star, in design pixels: a little over one physical pixel at 100 %.</summary>
    public const float StarLine = 1.1f;

    /// <summary>The ten corners of a star standing on its centre, first the top point, then clockwise, alternating point and dent.</summary>
    public static void StarCorner(float cx, float cy, float radius, Span<(float X, float Y)> into)
    {
        for (var i = 0; i < StarCorners; i++)
        {
            var r = i % 2 == 0 ? radius : radius * StarDent;
            var angle = -MathF.PI / 2 + i * MathF.PI / 5;
            into[i] = (cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle));
        }
    }

    /// <summary>
    /// A five-pointed star of the given colour: solid, or ten thin strokes for the outline. <paramref name="radius"/> is the
    /// distance from the centre to a point, in design pixels.
    /// </summary>
    public static void Star(this ICanvas c, float cx, float cy, float radius, bool filled, uint rgb)
    {
        Span<(float X, float Y)> corners = stackalloc (float X, float Y)[StarCorners];
        StarCorner(cx, cy, radius, corners);
        if (filled)
        {
            c.FillPolygon(corners, rgb);
            return;
        }

        for (var i = 0; i < StarCorners; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % StarCorners];
            c.Stroke(a.X, a.Y, b.X, b.Y, StarLine, rgb);
        }
    }
}
