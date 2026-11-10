using Hindsound.Ui.Window;

namespace Hindsound.Ui;

/// <summary>The icons of the notification, as signed distances over a 24 x 24 design box (the paths of the mockup).</summary>
internal static unsafe class NotificationIcons
{
    public static void Draw(Pixels pixels, NotificationVisual visual, uint rgb, float left, float top, float s)
    {
        var size = 24 * s;
        for (var y = Math.Max(0, (int)top); y < Math.Min(pixels.Height, (int)MathF.Ceiling(top + size)); y++)
        {
            var row = pixels.Row(y);
            for (var x = Math.Max(0, (int)left); x < Math.Min(pixels.Width, (int)MathF.Ceiling(left + size)); x++)
            {
                var distance = Distance(visual, (x + 0.5f - left) / s, (y + 0.5f - top) / s);
                Pixels.Blend(row + x, rgb, Pixels.Clamp(0.5f - distance * s));
            }
        }
    }

    /// <summary>Distance in design pixels to the shape, negative inside.</summary>
    public static float Distance(NotificationVisual visual, float x, float y) => visual switch
    {
        NotificationVisual.Triangle => Triangle(x, y),
        NotificationVisual.Pause => MathF.Min(Sdf.RoundRect(x, y, 6, 4, 10, 20, 1), Sdf.RoundRect(x, y, 14, 4, 18, 20, 1)),
        NotificationVisual.Dot => Sdf.Circle(x, y, 12, 12, 5),
        NotificationVisual.Headset => Headset(x, y),
        NotificationVisual.Pending => MathF.Min(Sdf.Circle(x, y, 5, 12, 2), MathF.Min(Sdf.Circle(x, y, 12, 12, 2), Sdf.Circle(x, y, 19, 12, 2))),
        _ => float.MaxValue,
    };

    private static float Triangle(float x, float y)
    {
        var outline = MathF.Min(
            Sdf.Segment(x, y, 12, 3.5f, 22, 20.5f, 2),
            MathF.Min(Sdf.Segment(x, y, 22, 20.5f, 2, 20.5f, 2), Sdf.Segment(x, y, 2, 20.5f, 12, 3.5f, 2)));
        var mark = MathF.Min(Sdf.Segment(x, y, 12, 9.5f, 12, 14, 2), Sdf.Circle(x, y, 12, 17.2f, 1.15f));
        return MathF.Min(outline, mark);
    }

    private static float Headset(float x, float y)
    {
        const float stroke = 1.8f;
        var arc = y <= 12
            ? MathF.Abs(Sdf.Circle(x, y, 12, 12, 8)) - stroke / 2
            : MathF.Min(Sdf.Segment(x, y, 4, 12, 4, 15, stroke), Sdf.Segment(x, y, 20, 12, 20, 15, stroke));
        var leftCup = MathF.Abs(Sdf.RoundRect(x, y, 3, 14, 7.5f, 21, 1.2f)) - stroke / 2;
        var rightCup = MathF.Abs(Sdf.RoundRect(x, y, 16.5f, 14, 21, 21, 1.2f)) - stroke / 2;
        return MathF.Min(arc, MathF.Min(leftCup, rightCup));
    }
}
