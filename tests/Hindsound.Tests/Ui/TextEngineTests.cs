using Hindsound.Export;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui;

public unsafe class TextEngineTests
{
    private const uint Accent = 0xD4F53C;
    private static readonly float[] Loud = Enumerable.Repeat(0.8f, 24).ToArray();
    private static readonly Face[] Faces = [Face.Title, Face.Body, Face.Medium, Face.Mono];

    internal static NotificationContent Saved() =>
        NotificationStates.ForClip(new ClipResult(["c"], TimeSpan.FromSeconds(30), 1, false, Loud, false), TimeSpan.FromSeconds(30), true, true, Accent);

    private static uint At(GdiCanvas canvas, int x, int y) => canvas.Bits[y * canvas.Width + x] & 0xFFFFFF;

    // The rows that hold ink, from the first to the last: what the eye calls the height of the text.
    private static (int First, int Last) InkRows(GdiCanvas canvas, uint background)
    {
        int first = -1, last = -1;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var p = At(canvas, x, y);
                var g = (p >> 8) & 0xFF;
                if (g > ((background >> 8) & 0xFF) + 100)
                {
                    first = first < 0 ? y : first;
                    last = y;
                    break;
                }
            }
        }

        return (first, last);
    }

    [Theory]
    [InlineData((int)Face.Title, 18)]
    [InlineData((int)Face.Body, 13)]
    [InlineData((int)Face.Medium, 13)]
    [InlineData((int)Face.Mono, 12)]
    public void Drawn_text_leaves_ink_inside_its_box_and_nowhere_else(int faceIndex, float size)
    {
        var face = (Face)faceIndex;
        using var canvas = new GdiCanvas(260, 60, 1.5);
        Assert.True(canvas.UsesDirectWrite);
        canvas.FillRect(new Box(0, 0, 260, 60), Theme.Base);
        const float x = 20, top = 8;
        canvas.Text("Clip length 5:00", face, size, x, top, Theme.Text);

        float left = x * 1.5f - 1, right = (x + canvas.Measure("Clip length 5:00", face, size)) * 1.5f + 1;
        float upper = top * 1.5f - 1, lower = (top + canvas.LineHeight(face, size)) * 1.5f + 1;
        int inside = 0, outside = 0;
        for (var py = 0; py < canvas.Height; py++)
        {
            for (var px = 0; px < canvas.Width; px++)
            {
                if (At(canvas, px, py) == Theme.Base)
                {
                    continue;
                }

                if (px >= left && px < right && py >= upper && py < lower)
                {
                    inside++;
                }
                else
                {
                    outside++;
                }
            }
        }

        Assert.True(inside > 100, $"{inside} pixels of ink in the box");
        Assert.Equal(0, outside);
    }

    [Fact]
    public void The_notification_keeps_a_premultiplied_alpha_and_its_clear_corner()
    {
        using var bitmap = new NotificationBitmap(Saved(), 1.5, Accent, false);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.PixelAt(x, y);
                var a = p >> 24;
                Assert.True(((p >> 16) & 0xFF) <= a && ((p >> 8) & 0xFF) <= a && (p & 0xFF) <= a, $"({x}, {y}) = {p:X8} is not premultiplied");
            }
        }

        Assert.Equal(0u, bitmap.PixelAt(0, 0) >> 24);

        // The title: letters drawn, and every pixel of its row still opaque.
        var titleInk = 0;
        for (var y = (int)(8 * 1.5); y < (int)(32 * 1.5); y++)
        {
            for (var x = (int)(122 * 1.5); x < (int)(330 * 1.5); x++)
            {
                var p = bitmap.PixelAt(x, y);
                Assert.Equal(255u, p >> 24);
                titleInk += (p & 0xFFFFFF) != NotificationPalette.Fill ? 1 : 0;
            }
        }

        Assert.True(titleInk > 200, $"{titleInk} pixels of title");
    }

    [Fact]
    public void The_letters_of_the_notification_are_grey_not_ClearType()
    {
        using var bitmap = new NotificationBitmap(Saved(), 1.5, Accent, false);
        const uint fill = NotificationPalette.Fill, title = NotificationPalette.Title;
        var edges = 0;
        for (var y = (int)(8 * 1.5); y < (int)(32 * 1.5); y++)
        {
            for (var x = (int)(122 * 1.5); x < (int)(330 * 1.5); x++)
            {
                var p = bitmap.PixelAt(x, y) & 0xFFFFFF;
                if (p == fill || p == title)
                {
                    continue;
                }

                // Grey coverage moves the three channels together from the fill to the title colour; ClearType does not.
                var cover = new[] { 16, 8, 0 }.Select(shift => (((p >> shift) & 0xFF) - (float)((fill >> shift) & 0xFF)) / (((title >> shift) & 0xFF) - (float)((fill >> shift) & 0xFF))).ToArray();
                Assert.True(cover.Max() - cover.Min() < 0.12f, $"({x}, {y}) = {p:X6}: {string.Join(", ", cover)}");
                edges++;
            }
        }

        Assert.True(edges > 50, $"{edges} edge pixels");
    }

    // Over a clear bitmap, the letters bring their own alpha and leave the rest of the bound box clear.
    [Fact]
    public void Text_over_clear_pixels_stays_premultiplied_and_leaves_the_rest_clear()
    {
        Native.BITMAPINFOHEADER info = default;
        info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        info.Width = 160;
        info.Height = -40;
        info.Planes = 1;
        info.BitCount = 32;
        var dc = Native.CreateCompatibleDC(0);
        void* bits;
        var dib = Native.CreateDIBSection(dc, &info, 0, &bits, 0, 0);
        var old = Native.SelectObject(dc, dib);
        try
        {
            var pixels = new Pixels((uint*)bits, 160, 40);
            using (var painter = new TextPainter(dc, pixels, TextEngine.Shared, clearType: false))
            {
                painter.Text(new FontSpec(Face.Title, 20), "Saved", 10, 6, 0xF2F4F7);
            }

            int clear = 0, partial = 0, solid = 0;
            for (var i = 0; i < 160 * 40; i++)
            {
                var p = ((uint*)bits)[i];
                var a = p >> 24;
                Assert.True(((p >> 16) & 0xFF) <= a && ((p >> 8) & 0xFF) <= a && (p & 0xFF) <= a, $"pixel {i} = {p:X8}");
                _ = a == 0 ? clear++ : a == 255 ? solid++ : partial++;
            }

            Assert.True(solid > 50 && partial > 50 && clear > 160 * 40 / 2, $"{solid} solid, {partial} partial, {clear} clear");
        }
        finally
        {
            Native.SelectObject(dc, old);
            Native.DeleteObject(dib);
            Native.DeleteDC(dc);
        }
    }

    // White on black: an edge pixel whose three channels part by more than 40 is a ClearType fringe; grey moves them together.
    private static int Fringes(uint* bits, int count)
    {
        var fringes = 0;
        for (var i = 0; i < count; i++)
        {
            var p = bits[i];
            int r = (int)((p >> 16) & 0xFF), g = (int)((p >> 8) & 0xFF), b = (int)(p & 0xFF);
            fringes += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 40 ? 1 : 0;
        }

        return fringes;
    }

    // White text on an opaque black bitmap, through a painter of the shared engine: the number of ClearType fringes.
    private static int FringesOfPainter(bool clearType)
    {
        Native.BITMAPINFOHEADER info = default;
        info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        info.Width = 240;
        info.Height = -40;
        info.Planes = 1;
        info.BitCount = 32;
        var dc = Native.CreateCompatibleDC(0);
        void* bits;
        var dib = Native.CreateDIBSection(dc, &info, 0, &bits, 0, 0);
        var old = Native.SelectObject(dc, dib);
        try
        {
            new Span<uint>(bits, 240 * 40).Fill(0xFF000000);
            using (var painter = new TextPainter(dc, new Pixels((uint*)bits, 240, 40), TextEngine.Shared, clearType, opaque: true))
            {
                Assert.True(painter.UsesDirectWrite);
                painter.Text(new FontSpec(Face.Body, 20), "Clip length 5:00", 6, 6, 0xFFFFFF);
            }

            return Fringes((uint*)bits, 240 * 40);
        }
        finally
        {
            Native.SelectObject(dc, old);
            Native.DeleteObject(dib);
            Native.DeleteDC(dc);
        }
    }

    // The window keeps the ClearType chosen from v1.png: coloured fringes on the edges of its letters. The same opaque bitmap
    // asked in grey has none, so a wrong index of SetTextAntialiasMode (which leaves Direct2D on its default) is seen too.
    [Fact]
    public void The_window_draws_its_letters_in_ClearType_and_grey_when_asked()
    {
        var clearType = FringesOfPainter(clearType: true);
        Assert.True(clearType > 20, $"{clearType} fringes in ClearType");
        Assert.Equal(0, FringesOfPainter(clearType: false));

        using var canvas = new GdiCanvas(240, 40, 1);
        Assert.True(canvas.UsesDirectWrite);
        canvas.FillRect(new Box(0, 0, 240, 40), 0x000000);
        canvas.Text("Clip length 5:00", Face.Body, 20, 6, 6, 0xFFFFFF);
        var window = Fringes(canvas.Bits, 240 * 40);
        Assert.True(window > 20, $"{window} fringes in the window");
    }

    [Fact]
    public void Measures_grow_with_the_text_and_keep_their_fraction()
    {
        using var canvas = new GdiCanvas(10, 10, 1.25);
        foreach (var face in Faces)
        {
            var widths = new[] { "C", "Cl", "Clip", "Clip length" }.Select(t => canvas.Measure(t, face, 13)).ToArray();
            Assert.True(widths.Zip(widths.Skip(1)).All(w => w.First < w.Second), $"{face}: {string.Join(", ", widths)}");
            Assert.InRange(canvas.LineHeight(face, 13), 13f, 26f);
        }

        // A float width, not GDI's whole pixels.
        Assert.Contains(Faces, face => canvas.Measure("Clip length", face, 13) * 1.25f % 1 != 0);
    }

    [Fact]
    public void The_fonts_are_the_ones_chosen_for_the_app()
    {
        var engine = TextEngine.Shared!;
        Assert.Equal((600, 4), engine.MatchedStyleOf(Face.Title));
        Assert.Equal((600, 5), engine.MatchedStyleOf(Face.Medium));
        Assert.Equal((400, 5), engine.MatchedStyleOf(Face.Body));
        Assert.Equal("Bahnschrift", engine.FamilyOf(Face.Title));
        var mono = new FontSpec(Face.Mono, 12);
        Assert.Equal(engine.Measure(mono, "iiii"), engine.Measure(mono, "WWWW"), 0.01f);
    }

    [Fact]
    public void A_missing_Cascadia_Mono_falls_back_to_Consolas_and_says_so_once()
    {
        var lines = new List<string>();
        using var engine = TextEngine.TryCreate(lines.Add, missingForTests: ["Cascadia Mono"])!;

        Assert.Equal("Consolas", engine.FamilyOf(Face.Mono));
        var small = new FontSpec(Face.Mono, 12);
        Assert.Equal(engine.Measure(small, "iiii"), engine.Measure(small, "WWWW"), 0.01f);
        engine.Measure(new FontSpec(Face.Mono, 20), "5:00");

        Assert.Single(lines);
        Assert.Contains("Cascadia Mono not installed, using Consolas", lines[0]);
    }

    // Never a proportional font for the keys and durations. Courier New comes with every Windows.
    [Fact]
    public void Without_Cascadia_Mono_nor_Consolas_the_mono_text_takes_Courier_New()
    {
        var lines = new List<string>();
        using var engine = TextEngine.TryCreate(lines.Add, missingForTests: ["Cascadia Mono", "Consolas"])!;

        Assert.Equal("Courier New", engine.FamilyOf(Face.Mono));
        var small = new FontSpec(Face.Mono, 12);
        Assert.Equal(engine.Measure(small, "iiii"), engine.Measure(small, "WWWW"), 0.01f);
        Assert.Single(lines);
        Assert.Contains("Cascadia Mono not installed, using Courier New", lines[0]);
    }

    // GDI (the text box, or all the text without DirectWrite) gives a font for any name: the face it chose is checked.
    [Fact]
    public void GDI_takes_the_first_mono_family_installed_the_same_one_as_DirectWrite()
    {
        var font = GdiFonts.CreateFirstInstalled(["Cascadia MonoX", "ConsolasX", "Courier New"], 16, Native.ANTIALIASED_QUALITY, out var used);
        var dc = Native.CreateCompatibleDC(0);
        var old = Native.SelectObject(dc, font);
        try
        {
            Assert.Equal("Courier New", used);
            Native.SIZE thin, wide;
            Native.GetTextExtentPoint32W(dc, "iiii", 4, &thin);
            Native.GetTextExtentPoint32W(dc, "WWWW", 4, &wide);
            Assert.Equal(thin.Cx, wide.Cx);
        }
        finally
        {
            Native.SelectObject(dc, old);
            Native.DeleteObject(font);
            Native.DeleteDC(dc);
        }

        Native.DeleteObject(GdiFonts.Create(Face.Mono, 16, Native.ANTIALIASED_QUALITY, out var mono));
        Assert.Equal(TextEngine.Shared!.FamilyOf(Face.Mono), mono);
    }

    [Fact]
    public void When_Direct2D_does_not_start_the_text_is_drawn_by_GDI()
    {
        var lines = new List<string>();
        Assert.Null(TextEngine.TryCreate(lines.Add, refuseForTests: true));
        Assert.Contains(lines, line => line.Contains("drawing with GDI"));

        using var canvas = new GdiCanvas(200, 40, 1, null);
        Assert.False(canvas.UsesDirectWrite);
        canvas.FillRect(new Box(0, 0, 200, 40), Theme.Base);
        canvas.Text("Clip length", Face.Title, 18, 4, 4, Theme.Text);
        Assert.True(InkRows(canvas, Theme.Base).First >= 0);
        Assert.True(canvas.Measure("Clip length", Face.Title, 18) > 40);

        using var bitmap = new NotificationBitmap(Saved(), 1, Accent, false, null);
        Assert.Equal(255u, bitmap.PixelAt(130, 20) >> 24);
    }

    // DirectWrite puts each line where GDI put it: the ink of a capital starts and ends on the same rows, one pixel apart at most.
    [Theory]
    [InlineData((int)Face.Title, 18, 1.0)]
    [InlineData((int)Face.Body, 13, 1.0)]
    [InlineData((int)Face.Medium, 13, 1.0)]
    [InlineData((int)Face.Mono, 12, 1.0)]
    [InlineData((int)Face.Title, 18, 1.5)]
    [InlineData((int)Face.Body, 13, 1.5)]
    [InlineData((int)Face.Medium, 13, 1.5)]
    [InlineData((int)Face.Mono, 12, 1.5)]
    public void The_text_stays_at_the_height_GDI_gave_it(int faceIndex, float size, double scale)
    {
        var face = (Face)faceIndex;
        using var modern = new GdiCanvas(120, 60, scale);
        using var gdi = new GdiCanvas(120, 60, scale, null);
        foreach (var canvas in new[] { modern, gdi })
        {
            canvas.FillRect(new Box(0, 0, 120, 60), Theme.Base);
            canvas.Text("HE", face, size, 4, 6, Theme.Text);
        }

        var (modernTop, modernBottom) = InkRows(modern, Theme.Base);
        var (gdiTop, gdiBottom) = InkRows(gdi, Theme.Base);
        var rows = $"{face} {size} x{scale}: ink {modernTop}-{modernBottom} against GDI {gdiTop}-{gdiBottom}, lines {modern.LineHeight(face, size)} and {gdi.LineHeight(face, size)}";
        Assert.True(Math.Abs(modernTop - gdiTop) <= 1 && Math.Abs(modernBottom - gdiBottom) <= 1, rows);
        Assert.InRange(modern.LineHeight(face, size) - gdi.LineHeight(face, size), -1.5f, 1.5f);
    }
}

[CollectionDefinition("Counts handles", DisableParallelization = true)]
public class CountsHandles;

[Collection("Counts handles")]
public class TextLeakTests
{
    [Fact]
    public void Two_hundred_drawings_leave_no_GDI_USER_or_COM_object_behind()
    {
        using var engine = TextEngine.TryCreate(_ => { }, missingForTests: [])!;
        var process = Native.GetCurrentProcess();
        Draw(engine);
        var references = engine.FactoryReferences();
        var gdi = Native.GetGuiResources(process, Native.GR_GDIOBJECTS);
        var user = Native.GetGuiResources(process, Native.GR_USEROBJECTS);

        for (var i = 0; i < 200; i++)
        {
            Draw(engine);
        }

        Assert.Equal(references, engine.FactoryReferences());
        Assert.Equal(gdi, Native.GetGuiResources(process, Native.GR_GDIOBJECTS));
        Assert.Equal(user, Native.GetGuiResources(process, Native.GR_USEROBJECTS));
        // Every target, brush and layout given back, to its last reference: a kept one leaks nothing the counts above see.
        Assert.Equal(0, engine.HeldForTests);
    }

    // Each new size makes a format: a family, a font, a face and its OS/2 table are taken to read the line, and all given back.
    [Fact]
    public void Formats_of_every_size_give_back_their_families_fonts_faces_and_tables()
    {
        using var engine = TextEngine.TryCreate(_ => { }, missingForTests: [])!;
        var references = engine.FactoryReferences();
        foreach (var face in new[] { Face.Title, Face.Body, Face.Medium, Face.Mono })
        {
            for (var size = 8f; size < 40; size += 0.75f)
            {
                Assert.True(engine.LineHeight(new FontSpec(face, size)) > 0);
                engine.MatchedStyleOf(face);
            }
        }

        Assert.Equal(0, engine.HeldForTests);
        Assert.Equal(references, engine.FactoryReferences());
    }

    private static void Draw(TextEngine engine)
    {
        using (var canvas = new GdiCanvas(200, 50, 1.25, engine))
        {
            canvas.Text("Clip length", Face.Title, 18, 4, 4, Theme.Text);
            canvas.Text("Ctrl+Shift+F10", Face.Mono, 12, 4, 30, Theme.Text);
            canvas.Measure("Keep in memory", Face.Medium, 13);
        }

        using (var gdi = new GdiCanvas(200, 50, 1.25, null))
        {
            gdi.Text("Clip length", Face.Body, 13, 4, 4, Theme.Text);
        }

        using var bitmap = new NotificationBitmap(TextEngineTests.Saved(), 1.25, 0xD4F53C, false, engine);
    }
}
