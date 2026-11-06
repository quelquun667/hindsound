using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public class TrayIconMarkGridTests
{
    private const uint Accent = 0xD4F53C;
    private const uint HalfAccent = 0x74862D; // halfway between the plate 15181E and the accent

    private static string[] GridFile(string kind, int size) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Ui", "TrayGrids", $"{kind}-{size}.txt"));

    private static string[] Rows(float[] coverage, int size)
    {
        var rows = new string[size];
        for (var y = 0; y < size; y++)
        {
            var row = new char[size];
            for (var x = 0; x < size; x++)
            {
                row[x] = coverage[y * size + x] switch
                {
                    1f => '#',
                    0.5f => '+',
                    0f => '.',
                    _ => '?',
                };
            }

            rows[y] = new string(row);
        }

        return rows;
    }

    [Theory]
    [InlineData("active", TrayState.Active, 12)]
    [InlineData("active", TrayState.Active, 16)]
    [InlineData("active", TrayState.Active, 18)]
    [InlineData("active", TrayState.Active, 24)]
    [InlineData("pause", TrayState.Paused, 12)]
    [InlineData("pause", TrayState.Paused, 16)]
    [InlineData("pause", TrayState.Paused, 18)]
    [InlineData("pause", TrayState.Paused, 24)]
    public void Each_grid_is_its_file_exactly(string kind, TrayState state, int size)
    {
        var expected = GridFile(kind, size);

        Assert.Equal(size, expected.Length);
        Assert.All(expected, row => Assert.Equal(size, row.Length));
        Assert.Equal(expected, Rows(TrayIconShape.Coverage(state, size), size));
    }

    [Theory]
    [InlineData(TrayState.Active, 12)]
    [InlineData(TrayState.Active, 16)]
    [InlineData(TrayState.Active, 18)]
    [InlineData(TrayState.Active, 24)]
    [InlineData(TrayState.Paused, 12)]
    [InlineData(TrayState.Paused, 16)]
    [InlineData(TrayState.Paused, 18)]
    [InlineData(TrayState.Paused, 24)]
    public void Each_grid_mirrors_left_and_right(TrayState state, int size)
    {
        foreach (var row in Rows(TrayIconShape.Coverage(state, size), size))
        {
            Assert.Equal(row, new string(row.Reverse().ToArray()));
        }
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(24)]
    public void Each_pause_grid_is_the_stems_of_the_active_one(int size)
    {
        var active = Rows(TrayIconShape.Coverage(TrayState.Active, size), size);
        var paused = Rows(TrayIconShape.Coverage(TrayState.Paused, size), size);

        Assert.All(paused, row => Assert.Equal(active[0], row));
        Assert.NotEqual(active, paused);
    }

    [Theory]
    [InlineData(16, 2, 12)]
    [InlineData(20, 2, 16)]
    [InlineData(24, 3, 18)]
    [InlineData(32, 4, 24)]
    public void The_icon_at_100_125_150_and_200_percent_shows_its_grid_inside_the_inset(int icon, int inset, int grid)
    {
        var pixels = TrayIconShape.Compose(TrayState.Active, icon, Accent);
        var rows = GridFile("active", grid);

        for (var y = 0; y < grid; y++)
        {
            for (var x = 0; x < grid; x++)
            {
                var rgb = pixels[(y + inset) * icon + x + inset] & 0xFFFFFF;
                switch (rows[y][x])
                {
                    case '#':
                        Assert.Equal(Accent, rgb);
                        break;
                    case '.':
                        Assert.Equal(TrayIconShape.PlateColor, rgb);
                        break;
                    default:
                        Assert.NotEqual(Accent, rgb);
                        Assert.NotEqual(TrayIconShape.PlateColor, rgb);
                        break;
                }
            }
        }
    }

    [Fact]
    public void A_half_covered_pixel_is_halfway_between_the_plate_and_the_accent()
    {
        var pixels = TrayIconShape.Compose(TrayState.Active, 20, Accent);

        // Row 5, column 7 of the 16 px grid, drawn 2 px in.
        Assert.Equal(HalfAccent, pixels[(5 + 2) * 20 + 7 + 2] & 0xFFFFFF);
    }
}

public class TrayIconMarkDrawnTests
{
    private const uint Accent = 0xD4F53C;

    // Units of the logo's view to a pixel of the shape: the square from 75 to 525 fills it.
    private static int Pixel(float unit, int size) => (int)((unit - 75) * size / 450);

    private static float At(float[] shape, int size, float unitX, float unitY) =>
        shape[Pixel(unitY, size) * size + Pixel(unitX, size)];

    [Theory]
    [InlineData(30)] // 250 %
    [InlineData(36)] // 300 %
    [InlineData(48)] // the big icon at 200 %
    [InlineData(96)]
    public void The_stems_are_full_and_run_from_top_to_bottom(int size)
    {
        var shape = TrayIconShape.Coverage(TrayState.Active, size);

        foreach (var stem in new[] { 133f, 467f })
        {
            Assert.True(shape[Pixel(stem, size)] > 0.5f, "the round top");
            Assert.True(shape[((size - 1) * size) + Pixel(stem, size)] > 0.5f, "the round bottom");
            for (var y = 1; y < size - 1; y++)
            {
                Assert.Equal(1f, shape[y * size + Pixel(stem, size)]);
            }
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(36)]
    [InlineData(48)]
    [InlineData(96)]
    public void The_central_peak_is_there_and_the_wave_stays_between_the_stems(int size)
    {
        var shape = TrayIconShape.Coverage(TrayState.Active, size);

        Assert.True(At(shape, size, 300, 238) > 0.9f, "the top of the peak");
        Assert.True(At(shape, size, 226, 332) > 0.9f, "the left valley");
        Assert.True(At(shape, size, 374, 332) > 0.9f, "the right valley");
        Assert.Equal(0f, At(shape, size, 300, 300)); // under the peak
        Assert.Equal(0f, At(shape, size, 300, 150)); // above the wave
        Assert.Equal(0f, At(shape, size, 300, 450)); // below the wave
    }

    [Fact]
    public void The_wave_goes_through_the_points_of_the_logo_with_its_width()
    {
        const int Size = 450; // one pixel per unit
        var shape = TrayIconShape.Coverage(TrayState.Active, Size);

        foreach (var (x, y) in new (float, float)[] { (177, 298), (226, 332), (300, 238), (374, 332), (423, 298) })
        {
            Assert.Equal(1f, At(shape, Size, x, y));
        }

        var column = Enumerable.Range(0, Size).Sum(y => shape[y * Size + 225]); // x = 300, through the peak
        Assert.InRange(column, 27.5f, 28.5f);
    }

    [Fact]
    public void Each_stem_has_the_width_of_the_logo_and_round_ends()
    {
        const int Size = 450;
        var shape = TrayIconShape.Coverage(TrayState.Paused, Size);

        var row = Enumerable.Range(0, Size).Sum(x => shape[375 * Size + x]); // y = 450, away from the wave
        Assert.InRange(row, 81.5f, 82.5f); // two stems of 41

        Assert.Equal(0f, At(shape, Size, 113, 76)); // the corner of the stem is cut
        Assert.Equal(1f, At(shape, Size, 133, 76));
    }

    [Theory]
    [InlineData(TrayState.Active, 14)]
    [InlineData(TrayState.Active, 30)]
    [InlineData(TrayState.Active, 36)]
    [InlineData(TrayState.Active, 48)]
    [InlineData(TrayState.Paused, 30)]
    public void The_drawing_mirrors_left_and_right(TrayState state, int size)
    {
        var shape = TrayIconShape.Coverage(state, size);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size / 2; x++)
            {
                Assert.InRange(shape[y * size + x] - shape[y * size + size - 1 - x], -0.001f, 0.001f);
            }
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(36)]
    [InlineData(48)]
    public void The_edge_is_antialiased(int size)
    {
        Assert.Contains(TrayIconShape.Coverage(TrayState.Active, size), c => c > 0.05f && c < 0.95f);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(36)]
    [InlineData(48)]
    public void Without_the_wave_nothing_is_left_between_the_stems(int size)
    {
        var shape = TrayIconShape.Coverage(TrayState.Paused, size);
        var active = TrayIconShape.Coverage(TrayState.Active, size);

        for (var y = 0; y < size; y++)
        {
            for (var x = Pixel(160, size) + 1; x < Pixel(440, size); x++)
            {
                Assert.Equal(0f, shape[y * size + x]);
            }
        }

        Assert.NotEqual(active, shape);
    }

    [Theory]
    [InlineData(40, 5)]
    [InlineData(48, 6)]
    [InlineData(64, 8)]
    [InlineData(96, 12)]
    public void No_pixel_of_the_mark_leaves_the_inset(int icon, int inset)
    {
        var pixels = TrayIconShape.Compose(TrayState.Active, icon, Accent);

        for (var y = 0; y < icon; y++)
        {
            for (var x = 0; x < icon; x++)
            {
                if (x < inset || x >= icon - inset || y < inset || y >= icon - inset)
                {
                    Assert.True(pixels[y * icon + x] >> 24 == 0 || (pixels[y * icon + x] & 0xFFFFFF) == TrayIconShape.PlateColor, $"pixel {x},{y}");
                }
            }
        }

        Assert.Contains(0xFF000000u | Accent, pixels);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(30)]
    public void The_error_keeps_its_triangle(int size)
    {
        var shape = TrayIconShape.Coverage(TrayState.Error, size);
        var scale = size / 24f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = NotificationIcons.Distance(NotificationVisual.Triangle, (x + 0.5f) / scale, (y + 0.5f) / scale) * scale;
                Assert.Equal(Pixels.Clamp(0.5f - distance), shape[y * size + x]);
            }
        }
    }
}
