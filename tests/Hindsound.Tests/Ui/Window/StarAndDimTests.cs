using Hindsound.Tests.Audio;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>ICanvas.FillPolygon, ICanvas.Dim and the star, on a real bitmap: the pixels, not the calls.</summary>
public class StarAndDimTests
{
    private const uint Ground = Theme.Panel, Ink = 0x2EF2C4;
    private const float CenterX = 40, CenterY = 40, Radius = 20;

    private static unsafe uint At(GdiCanvas canvas, int x, int y) => canvas.Bits[y * canvas.Width + x] & 0xFFFFFF;

    private static GdiCanvas Canvas(double scale)
    {
        var canvas = new GdiCanvas((int)(80 * scale), (int)(80 * scale), scale, null);
        canvas.FillRect(new Box(0, 0, 80, 80), Ground);
        return canvas;
    }

    private static uint Channel(uint rgb, int shift) => (rgb >> shift) & 0xFF;

    /// <summary>How far a colour is from the ground toward the ink: 0 is the ground, 1 is the ink.</summary>
    private static double Share(uint rgb) =>
        Enumerable.Range(0, 3).Select(i => i * 8).Average(shift =>
            ((double)Channel(rgb, shift) - Channel(Ground, shift)) / ((double)Channel(Ink, shift) - Channel(Ground, shift)));

    // The scales the window is checked at: 100, 125, 150 and 200 %.
    public static TheoryData<double> Scales => new() { 1.0, 1.25, 1.5, 2.0 };

    [Theory]
    [MemberData(nameof(Scales))]
    public void A_filled_star_is_solid_in_the_middle_and_clear_in_the_corner(double scale)
    {
        using var canvas = Canvas(scale);

        canvas.Star(CenterX, CenterY, Radius, filled: true, Ink);

        Assert.Equal(Ink, At(canvas, (int)(CenterX * scale), (int)(CenterY * scale)));
        // The corner of the box around the star lies beyond the dents and between two points.
        Assert.Equal(Ground, At(canvas, (int)((CenterX - Radius) * scale) + 1, (int)((CenterY - Radius) * scale) + 1));
        Assert.Equal(Ground, At(canvas, (int)((CenterX + Radius) * scale) - 1, (int)((CenterY - Radius) * scale) + 1));
        Assert.Equal(Ground, At(canvas, 0, 0));
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_edge_of_a_filled_star_is_softened_in_many_steps(double scale)
    {
        using var canvas = Canvas(scale);

        canvas.Star(CenterX, CenterY, Radius, filled: true, Ink);

        var steps = new HashSet<uint>();
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var pixel = At(canvas, x, y);
                if (pixel != Ground && pixel != Ink)
                {
                    steps.Add(pixel);
                    // A blend never leaves the box of the two colours.
                    var share = Share(pixel);
                    Assert.InRange(share, -0.02, 1.02);
                }
            }
        }

        // Ten edges of a few dozen pixels each: far more than a stair of two or three greys.
        Assert.True(steps.Count >= 20, $"only {steps.Count} different edge colours at {scale}");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_whole_star_stays_inside_its_box(double scale)
    {
        using var canvas = Canvas(scale);

        canvas.Star(CenterX, CenterY, Radius, filled: true, Ink);

        var left = (int)((CenterX - Radius) * scale) - 1;
        var right = (int)MathF.Ceiling((CenterX + Radius) * (float)scale) + 1;
        var top = (int)((CenterY - Radius) * scale) - 1;
        var bottom = (int)MathF.Ceiling((CenterY + Radius) * (float)scale) + 1;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                if (x < left || x > right || y < top || y > bottom)
                {
                    Assert.Equal(Ground, At(canvas, x, y));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void An_empty_star_has_a_clear_middle_and_a_line_on_each_edge(double scale)
    {
        using var canvas = Canvas(scale);

        canvas.Star(CenterX, CenterY, Radius, filled: false, Ink);

        Assert.Equal(Ground, At(canvas, (int)(CenterX * scale), (int)(CenterY * scale)));
        Assert.Equal(Ground, At(canvas, (int)((CenterX - Radius) * scale) + 1, (int)((CenterY - Radius) * scale) + 1));

        // The middle of each of the ten sides carries the line.
        var corners = new (float X, float Y)[CanvasShapes.StarCorners];
        CanvasShapes.StarCorner(CenterX, CenterY, Radius, corners);
        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            var x = (int)((a.X + b.X) / 2 * scale);
            var y = (int)((a.Y + b.Y) / 2 * scale);
            Assert.True(Share(At(canvas, x, y)) > 0.4, $"side {i} at {scale}: {At(canvas, x, y):X6}");
        }
    }

    [Fact]
    public void A_filled_and_an_empty_star_differ_in_the_middle_only_by_what_fills_it()
    {
        using var filled = Canvas(1);
        using var empty = Canvas(1);

        filled.Star(CenterX, CenterY, Radius, filled: true, Ink);
        empty.Star(CenterX, CenterY, Radius, filled: false, Ink);

        var inkFilled = 0;
        var inkEmpty = 0;
        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                inkFilled += At(filled, x, y) == Ink ? 1 : 0;
                inkEmpty += At(empty, x, y) == Ink ? 1 : 0;
            }
        }

        Assert.True(inkFilled > 4 * inkEmpty, $"{inkFilled} solid pixels filled, {inkEmpty} empty");
    }

    [Fact]
    public void A_star_has_five_points_and_five_dents_on_two_circles()
    {
        var corners = new (float X, float Y)[CanvasShapes.StarCorners];

        CanvasShapes.StarCorner(10, 20, 8, corners);

        Assert.Equal((10f, 12f), (MathF.Round(corners[0].X, 3), MathF.Round(corners[0].Y, 3)));
        for (var i = 0; i < corners.Length; i++)
        {
            var distance = MathF.Sqrt(MathF.Pow(corners[i].X - 10, 2) + MathF.Pow(corners[i].Y - 20, 2));
            // The ratio of the dents and the width of the line are those of the validated design, not whatever the code says.
            Assert.Equal(i % 2 == 0 ? 8f : 8f * 0.42f, distance, 3);
        }
    }

    [Fact]
    public void A_polygon_that_is_not_one_draws_nothing()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([], Ink);
        canvas.FillPolygon([(10, 10)], Ink);
        canvas.FillPolygon([(10, 10), (60, 60)], Ink);
        // Three points on one line enclose nothing.
        canvas.FillPolygon([(10, 10), (20, 20), (30, 30)], Ink);

        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                Assert.Equal(Ground, At(canvas, x, y));
            }
        }
    }

    [Fact]
    public void A_polygon_off_the_bitmap_or_past_its_edge_is_cut_and_nothing_breaks()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(2e9f, 2e9f), (3e9f, 2e9f), (2e9f, 3e9f)], Ink);
        canvas.FillPolygon([(-3e9f, -3e9f), (-2e9f, -3e9f), (-3e9f, -2e9f)], Ink);
        canvas.FillPolygon([(float.NaN, 0), (10, 10), (20, 0)], Ink);
        canvas.FillPolygon([(200, 200), (300, 200), (250, 300)], Ink);
        canvas.FillPolygon([(70, 70), (100, 70), (70, 100)], Ink);

        Assert.Equal(Ink, At(canvas, 71, 71));
        Assert.Equal(Ink, At(canvas, 79, 79));
    }

    [Fact]
    public void A_polygon_across_the_top_left_corner_is_cut_there_and_leaves_the_rest_of_the_bitmap_alone()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(-10, -10), (20, -10), (20, 20), (-10, 20)], Ink);

        Assert.Equal(Ink, At(canvas, 0, 0));
        Assert.Equal(Ink, At(canvas, 19, 19));
        Assert.Equal(Ground, At(canvas, 20, 5));
        Assert.Equal(Ground, At(canvas, 5, 20));
        Assert.Equal(Ground, At(canvas, 79, 0));
        Assert.Equal(Ground, At(canvas, 0, 79));
    }

    [Fact]
    public void A_polygon_across_the_right_edge_does_not_wrap_into_the_next_row()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(70, 10), (100, 10), (100, 20), (70, 20)], Ink);

        Assert.Equal(Ink, At(canvas, 79, 15));
        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                var inside = x >= 70 && y >= 10 && y < 20;
                Assert.Equal(inside ? Ink : Ground, At(canvas, x, y));
            }
        }
    }

    [Fact]
    public void A_polygon_across_the_bottom_edge_is_cut_there()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(10, 70), (20, 70), (20, 100), (10, 100)], Ink);

        Assert.Equal(Ink, At(canvas, 15, 79));
        Assert.Equal(Ground, At(canvas, 15, 69));
        Assert.Equal(Ground, At(canvas, 9, 79));
        Assert.Equal(Ground, At(canvas, 20, 79));
    }

    [Fact]
    public void A_square_covers_exactly_its_pixels_and_a_half_pixel_gives_half_the_colour()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(10, 10), (20, 10), (20, 20), (10, 20)], Ink);
        canvas.FillPolygon([(30, 10), (40.5f, 10), (40.5f, 20), (30, 20)], Ink);

        Assert.Equal(Ink, At(canvas, 10, 10));
        Assert.Equal(Ink, At(canvas, 19, 19));
        Assert.Equal(Ground, At(canvas, 20, 15));
        Assert.Equal(Ground, At(canvas, 15, 20));
        Assert.Equal(Ground, At(canvas, 9, 15));
        Assert.Equal(Ink, At(canvas, 39, 15));
        Assert.InRange(Share(At(canvas, 40, 15)), 0.47, 0.53);
    }

    [Fact]
    public void A_horizontal_edge_on_half_a_pixel_gives_half_the_colour_so_the_rows_are_smoothed_as_well()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(10, 10.5f), (20, 10.5f), (20, 20), (10, 20)], Ink);

        Assert.InRange(Share(At(canvas, 15, 10)), 0.4, 0.6);
        Assert.Equal(Ink, At(canvas, 15, 11));
        Assert.Equal(Ground, At(canvas, 15, 9));
    }

    [Fact]
    public void A_pixel_cut_by_the_left_side_gets_the_share_it_is_covered_by_and_so_does_one_cut_on_both_sides()
    {
        using var canvas = Canvas(1);

        canvas.FillPolygon([(10.75f, 10), (20, 10), (20, 20), (10.75f, 20)], Ink);
        canvas.FillPolygon([(30.25f, 10), (30.5f, 10), (30.5f, 20), (30.25f, 20)], Ink);

        Assert.InRange(Share(At(canvas, 10, 15)), 0.22, 0.28);
        Assert.Equal(Ink, At(canvas, 11, 15));
        Assert.InRange(Share(At(canvas, 30, 15)), 0.22, 0.28);
    }

    [Fact]
    public void Where_an_outline_crosses_itself_the_even_odd_rule_leaves_a_hole()
    {
        using var canvas = Canvas(1);

        // Two sides cross in the middle: what lies inside one turn of the outline is filled, and the open top and bottom are not.
        canvas.FillPolygon([(10, 10), (50, 50), (50, 10), (10, 50)], Ink);

        Assert.Equal(Ink, At(canvas, 12, 30));
        Assert.Equal(Ink, At(canvas, 48, 30));
        Assert.Equal(Ground, At(canvas, 30, 15));
        Assert.Equal(Ground, At(canvas, 30, 45));
    }

    [Fact]
    public void A_polygon_follows_the_origin_of_the_page()
    {
        using var canvas = Canvas(1);

        canvas.SetOrigin(-30);
        canvas.FillPolygon([(10, 40), (20, 40), (20, 50), (10, 50)], Ink);

        Assert.Equal(Ink, At(canvas, 15, 15));
        Assert.Equal(Ground, At(canvas, 15, 45));
    }

    [Fact]
    public void A_polygon_with_many_corners_and_a_wide_one_draw_the_same_as_a_small_one()
    {
        // Past the working space kept on the stack: an outline of 100 corners, and a shape wider than a row of coverage kept there.
        using var canvas = new GdiCanvas(1400, 40, 1, null);
        canvas.FillRect(new Box(0, 0, 1400, 40), Ground);
        var circle = new (float X, float Y)[100];
        for (var i = 0; i < circle.Length; i++)
        {
            var angle = 2 * MathF.PI * i / circle.Length;
            circle[i] = (20 + 15 * MathF.Cos(angle), 20 + 15 * MathF.Sin(angle));
        }

        canvas.FillPolygon(circle, Ink);
        canvas.FillPolygon([(100, 5), (1300, 5), (1300, 30), (100, 30)], Ink);

        Assert.Equal(Ink, At(canvas, 20, 20));
        Assert.Equal(Ground, At(canvas, 3, 3));
        Assert.Equal(Ink, At(canvas, 700, 15));
        Assert.Equal(Ink, At(canvas, 1299, 29));
        Assert.Equal(Ground, At(canvas, 1300, 29));
    }

    [Theory]
    [InlineData(0xFFFFFF, 0.45f)]
    [InlineData(0x2EF2C4, 0.45f)]
    [InlineData(0xFFB547, 0.45f)]
    [InlineData(0x808080, 0.45f)]
    [InlineData(0x15181E, 0.45f)]
    [InlineData(0x2EF2C4, 0.1f)]
    [InlineData(0x2EF2C4, 0.9f)]
    public void Dim_darkens_each_colour_toward_black_by_the_share_asked(uint color, float opacity)
    {
        using var canvas = Canvas(1);
        canvas.FillRect(new Box(10, 10, 20, 20), color);

        canvas.Dim(opacity);

        var dimmed = At(canvas, 15, 15);
        foreach (var shift in new[] { 0, 8, 16 })
        {
            Assert.InRange(Channel(dimmed, shift), Channel(color, shift) * (1 - opacity) - 1.01, Channel(color, shift) * (1 - opacity) + 0.6);
        }

        // What was already the ground is darkened as well, and in the same way.
        var ground = At(canvas, 60, 60);
        foreach (var shift in new[] { 0, 8, 16 })
        {
            Assert.InRange(Channel(ground, shift), Channel(Ground, shift) * (1 - opacity) - 1.01, Channel(Ground, shift) * (1 - opacity) + 0.6);
        }
    }

    [Fact]
    public void Dim_with_nothing_or_less_changes_nothing_and_with_all_or_more_makes_everything_black()
    {
        using var canvas = Canvas(1);
        canvas.FillRect(new Box(10, 10, 20, 20), Ink);

        canvas.Dim(0);
        canvas.Dim(-1);
        canvas.Dim(float.NaN);
        Assert.Equal(Ink, At(canvas, 15, 15));
        Assert.Equal(Ground, At(canvas, 60, 60));

        canvas.Dim(1);
        Assert.Equal(0u, At(canvas, 15, 15));
        Assert.Equal(0u, At(canvas, 60, 60));
    }

    [Fact]
    public void Dim_covers_the_whole_window_whatever_the_origin_of_the_page_is()
    {
        using var canvas = Canvas(1);
        canvas.SetOrigin(-30);

        canvas.Dim(0.5f);

        // The box is drawn after a scrolled page: the top and the bottom of the window are darkened as much as the middle.
        Assert.NotEqual(Ground, At(canvas, 0, 0));
        Assert.NotEqual(Ground, At(canvas, 79, 79));
        Assert.Equal(At(canvas, 0, 0), At(canvas, 40, 40));
        Assert.Equal(At(canvas, 0, 0), At(canvas, 79, 79));
    }

    [Fact]
    public void Dim_over_more_than_one_is_as_black_as_one()
    {
        using var canvas = Canvas(1);

        canvas.Dim(3f);

        Assert.Equal(0u, At(canvas, 15, 15));
    }

    [Fact]
    public unsafe void Dim_keeps_the_alpha_and_touches_every_pixel_and_none_beyond()
    {
        using var canvas = Canvas(1.25);
        var last = canvas.Width * canvas.Height - 1;

        canvas.Dim(0.5f);

        Assert.Equal(0xFFu, canvas.Bits[0] >> 24);
        Assert.Equal(0xFFu, canvas.Bits[last] >> 24);
        Assert.NotEqual(Ground, At(canvas, 0, 0));
        Assert.NotEqual(Ground, At(canvas, canvas.Width - 1, canvas.Height - 1));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 1)]
    [InlineData(4, 3)]
    public unsafe void Dim_treats_the_last_pixel_of_an_odd_bitmap_like_the_others(int width, int height)
    {
        using var canvas = new GdiCanvas(width, height, 1, null);
        canvas.FillRect(new Box(0, 0, width, height), Ink);

        canvas.Dim(0.5f);

        for (var i = 0; i < width * height; i++)
        {
            Assert.Equal(0xFF000000 | ((0x2Eu * 128 >> 8) << 16) | ((0xF2u * 128 >> 8) << 8) | (0xC4u * 128 >> 8), canvas.Bits[i]);
        }
    }

    [Fact]
    public void Dim_twice_is_darker_than_once_and_two_halves_make_a_quarter_of_the_light()
    {
        using var once = Canvas(1);
        using var twice = Canvas(1);
        once.FillRect(new Box(0, 0, 80, 80), 0xFFFFFF);
        twice.FillRect(new Box(0, 0, 80, 80), 0xFFFFFF);

        once.Dim(0.5f);
        twice.Dim(0.5f);
        twice.Dim(0.5f);

        Assert.InRange(Channel(At(once, 5, 5), 8), 126u, 128u);
        Assert.InRange(Channel(At(twice, 5, 5), 8), 62u, 64u);
    }

    [Fact]
    public void A_canvas_that_knows_neither_still_compiles_and_the_star_is_ten_strokes_or_one_polygon()
    {
        var fake = new BareCanvas();
        ICanvas bare = fake;

        // The defaults on the interface: a fake that does not care for them draws nothing, and nothing throws.
        bare.FillPolygon([(0, 0), (1, 0), (1, 1)], Ink);
        bare.Dim(0.45f);
        bare.Star(10, 10, 8, filled: true, Ink);
        bare.Star(10, 10, 8, filled: false, Ink);
        Assert.Equal(10, fake.Strokes);

        var recording = new RecordingCanvas();
        ICanvas recorded = recording;
        recorded.Star(10, 10, 8, filled: true, Ink);
        Assert.Equal(0, recording.Strokes);
        Assert.Single(recording.Polygons);
        Assert.Equal(CanvasShapes.StarCorners, recording.Polygons[0].Length);

        recorded.Star(10, 10, 8, filled: false, Ink);
        Assert.Equal(10, recording.Strokes);
        Assert.Single(recording.Polygons);
        Assert.All(recording.Widths, w => Assert.Equal(1.1f, w));
        Assert.All(recording.Colors, c => Assert.Equal(Ink, c));
    }

    private class BareCanvas : ICanvas
    {
        public int Strokes { get; private set; }

        public void SetOrigin(float dy) { }

        public void FillRect(Box box, uint rgb) { }

        public void FillRound(Box box, float radius, uint rgb) { }

        public void Frame(Box box, float radius, uint fill, uint border) { }

        public void Ring(Box box, float radius, float thickness, uint rgb) { }

        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Strokes++;

        public void Glow(Box box, float sigma, float opacity, uint rgb) { }

        public void Text(string text, Face face, float size, float x, float top, uint rgb) { }

        public float Measure(string text, Face face, float size) => text.Length * size / 2;

        public float LineHeight(Face face, float size) => size;

        public void WarningIcon(float x, float y, float size, uint rgb) { }

        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) { }
    }

    private sealed class RecordingCanvas : BareCanvas, ICanvas
    {
        public List<(float X, float Y)[]> Polygons { get; } = [];

        public List<float> Widths { get; } = [];

        public List<uint> Colors { get; } = [];

        public new int Strokes => Widths.Count;

        void ICanvas.Stroke(float ax, float ay, float bx, float by, float width, uint rgb)
        {
            Widths.Add(width);
            Colors.Add(rgb);
        }

        void ICanvas.FillPolygon(ReadOnlySpan<(float X, float Y)> points, uint rgb) => Polygons.Add(points.ToArray());
    }
}

/// <summary>Neither FillPolygon nor Dim may allocate: they run on every paint of a list of stars, and over a whole window.</summary>
[Collection(nameof(TimingSensitive))]
public class StarAndDimAllocationTests
{
    [Fact]
    public void FillPolygon_and_Dim_allocate_nothing_once_warm()
    {
        using var canvas = new GdiCanvas(400, 200, 2, null);
        canvas.FillRect(new Box(0, 0, 200, 100), Theme.Panel);
        (float X, float Y)[] star = new (float X, float Y)[CanvasShapes.StarCorners];
        CanvasShapes.StarCorner(30, 30, 8, star);
        var mixed = new (float X, float Y)[] { (5, 5), (60, 10), (40, 50), (20, 30) };

        canvas.FillPolygon(star, 0x2EF2C4);
        canvas.FillPolygon(mixed, 0x2EF2C4);
        canvas.Dim(0.45f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
        {
            canvas.FillPolygon(star, 0x2EF2C4);
            canvas.FillPolygon(mixed, 0x2EF2C4);
            canvas.Dim(0.45f);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void A_filled_star_allocates_nothing_once_warm()
    {
        using var canvas = new GdiCanvas(400, 200, 1.5, null);
        canvas.Star(30, 30, 8, filled: true, 0x2EF2C4);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 50; i++)
        {
            canvas.Star(30 + i, 30, 8, filled: true, 0x2EF2C4);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
