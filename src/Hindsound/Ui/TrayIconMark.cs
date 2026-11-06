using System.Numerics;

namespace Hindsound.Ui;

/// <summary>
/// The mark of the logo as coverage from 0 to 1: two stems and the wave between them, or the stems alone for the pause sign.
/// A curve this thin turns to mush when it is only antialiased at the sizes of the notification area, so those four sizes
/// are drawn by hand, pixel by pixel. Every other size comes from the drawing of the logo. Pure: tested without any window.
/// </summary>
internal static class TrayIconMark
{
    // A row per line: "." empty, "+" half covered, "#" full. Each one fills the shape of the icon drawn at 100 %, 125 %, 150 % and 200 %.
    private const string Active12 = """
        .##......##.
        .##......##.
        .##......##.
        .##......##.
        .##..++..##.
        .##.####.##.
        .####..####.
        .##......##.
        .##......##.
        .##......##.
        .##......##.
        .##......##.
        """;

    private const string Active16 = """
        .##..........##.
        .##..........##.
        .##..........##.
        .##..........##.
        .##..........##.
        .##....++....##.
        .##...+##+...##.
        .##...#..#...##.
        .###+.#..#.+###.
        .##.+#+..+#+.##.
        .##..........##.
        .##..........##.
        .##..........##.
        .##..........##.
        .##..........##.
        .##..........##.
        """;

    private const string Active18 = """
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        .##.....++.....##.
        .##....+##+....##.
        .##....#..#....##.
        .####+.#..#.+####.
        .##.+##+..+##+.##.
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        .##............##.
        """;

    private const string Active24 = """
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###......##......###..
        ..###.....+##+.....###..
        ..###.....#..#.....###..
        ..####+..+#..#+..+####..
        ..######.#+..+#.######..
        ..###..###....###..###..
        ..###...+......+...###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        ..###..............###..
        """;

    // The pause grids are the same stems, on every row: one row is all they need.
    private const string PauseRow12 = ".##......##.";
    private const string PauseRow16 = ".##..........##.";
    private const string PauseRow18 = ".##............##.";
    private const string PauseRow24 = "..###..............###..";

    // The drawing of mark.svg, in the units of its 600 x 600 view. The plate's inset leaves the square from 75 to 525 to the shape.
    private const float BoxStart = 75, BoxSize = 450;
    private const float Top = 75, Bottom = 525;
    private const float LeftStemStart = 112.5f, LeftStemEnd = 153.5f, RightStemStart = 446.5f, RightStemEnd = 487.5f;
    private const float StemRadius = 20.5f;
    private const float WaveWidth = 28;
    private const int SegmentsPerCurve = 16;

    // The ends of the wave stop inside the stems: the shape of its caps never shows.
    private static readonly Vector2[] Wave = Flatten(
        new(130, 313),
        [
            (new(145, 309), new(160, 298), new(177, 298)),
            (new(200, 298), new(203, 332), new(226, 332)),
            (new(264, 332), new(262, 238), new(300, 238)),
            (new(338, 238), new(336, 332), new(374, 332)),
            (new(397, 332), new(400, 298), new(423, 298)),
            (new(440, 298), new(455, 309), new(470, 313)),
        ]);

    public static float[] Coverage(bool withWave, int size)
    {
        var grid = withWave ? ActiveGrid(size) : PauseGrid(size);
        return grid is null ? Drawn(withWave, size) : Read(grid, withWave ? 1 : size, size);
    }

    private static string? ActiveGrid(int size) => size switch
    {
        12 => Active12,
        16 => Active16,
        18 => Active18,
        24 => Active24,
        _ => null,
    };

    private static string? PauseGrid(int size) => size switch
    {
        12 => PauseRow12,
        16 => PauseRow16,
        18 => PauseRow18,
        24 => PauseRow24,
        _ => null,
    };

    /// <param name="grid">The rows. Line breaks are skipped: a checkout can have either kind.</param>
    /// <param name="repeat">How many times the rows are repeated.</param>
    /// <param name="size">The side of the result.</param>
    private static float[] Read(string grid, int repeat, int size)
    {
        var coverage = new float[size * size];
        var at = 0;
        for (var i = 0; i < repeat; i++)
        {
            foreach (var cell in grid)
            {
                if (cell is '#' or '+' or '.')
                {
                    coverage[at++] = cell switch
                    {
                        '#' => 1,
                        '+' => 0.5f,
                        _ => 0,
                    };
                }
            }
        }

        return coverage;
    }

    private static float[] Drawn(bool withWave, int size)
    {
        var coverage = new float[size * size];
        var unitsPerPixel = BoxSize / size;
        for (var y = 0; y < size; y++)
        {
            var unitY = BoxStart + (y + 0.5f) * unitsPerPixel;
            for (var x = 0; x < size; x++)
            {
                var unitX = BoxStart + (x + 0.5f) * unitsPerPixel;
                var distance = MathF.Min(
                    Sdf.RoundRect(unitX, unitY, LeftStemStart, Top, LeftStemEnd, Bottom, StemRadius),
                    Sdf.RoundRect(unitX, unitY, RightStemStart, Top, RightStemEnd, Bottom, StemRadius));
                if (withWave)
                {
                    distance = MathF.Min(distance, WaveDistance(unitX, unitY));
                }

                coverage[y * size + x] = Pixels.Clamp(0.5f - distance / unitsPerPixel);
            }
        }

        return coverage;
    }

    private static float WaveDistance(float x, float y)
    {
        var nearest = float.MaxValue;
        for (var i = 1; i < Wave.Length; i++)
        {
            nearest = MathF.Min(nearest, Sdf.Segment(x, y, Wave[i - 1].X, Wave[i - 1].Y, Wave[i].X, Wave[i].Y, WaveWidth));
        }

        return nearest;
    }

    private static Vector2[] Flatten(Vector2 start, (Vector2 First, Vector2 Second, Vector2 End)[] curves)
    {
        var points = new Vector2[curves.Length * SegmentsPerCurve + 1];
        points[0] = start;
        var at = 1;
        foreach (var (first, second, end) in curves)
        {
            for (var step = 1; step <= SegmentsPerCurve; step++)
            {
                var t = (float)step / SegmentsPerCurve;
                var rest = 1 - t;
                points[at++] = (rest * rest * rest * start) + (3 * rest * rest * t * first) + (3 * rest * t * t * second) + (t * t * t * end);
            }

            start = end;
        }

        return points;
    }
}
