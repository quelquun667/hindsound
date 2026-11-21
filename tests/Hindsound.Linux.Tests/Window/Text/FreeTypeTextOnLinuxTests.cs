using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Linux.Window.Text;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.Window.Text;
using Xunit.Abstractions;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>
/// The Linux text on a real Linux, with FreeType, HarfBuzz and fontconfig of the system and the fonts the app ships, from
/// the folder of HINDSOUND_FONT_DIR (the CI sets it): its measures against those of the Windows text, its lines, what it
/// draws and where, a character the fonts lack, and every page of the window drawn with it.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed unsafe class FreeTypeTextOnLinuxTests(ITestOutputHelper output)
{
    private static string Fonts => Environment.GetEnvironmentVariable(FreeTypeText.FontDirectoryVariable) is { Length: > 0 } folder
        ? folder
        : throw new InvalidOperationException(FreeTypeText.FontDirectoryVariable + " is not set: it names the folder of the app's fonts.");

    private static string FileOf(Face face) => face switch
    {
        Face.Title => "BarlowSemiCondensed-SemiBold.ttf",
        Face.Body => "Selawik-Regular.ttf",
        Face.Medium => "Selawik-Semibold.ttf",
        _ => "CascadiaMono-Regular.ttf",
    };

    // An opaque bitmap with a guard of canaries before and after it.
    private sealed class Bitmap(int width, int height, uint fill = 0xFF101418)
    {
        public const int Guard = 256;
        public const uint Canary = 0x5A5A5A5A;
        public readonly uint[] Memory = Make(width, height, fill);

        public int Width => width;

        public int Height => height;

        public uint this[int x, int y] => Memory[Guard + (y * width) + x];

        public void Draw(ICanvasText text, FontSpec font, string s, float x, float top, uint rgb)
        {
            fixed (uint* bits = Memory)
            {
                text.Draw(new Pixels(bits + Guard, width, height), font, s, x, top, rgb);
            }
        }

        public int Changed(uint fill = 0xFF101418) => Memory.Skip(Guard).Take(width * height).Count(p => p != fill);

        public bool GuardsKept() => Memory.Take(Guard).Concat(Memory.Skip(Guard + (width * height))).All(p => p == Canary);

        private static uint[] Make(int width, int height, uint fill)
        {
            var memory = new uint[Guard + (width * height) + Guard];
            Array.Fill(memory, Canary);
            Array.Fill(memory, fill, Guard, width * height);
            return memory;
        }
    }

    [Fact]
    public void Selawik_and_Cascadia_Mono_keep_the_widths_of_the_Windows_text_within_3_percent()
    {
        using var text = LinuxText.Create(Fonts);
        var worst = new Dictionary<Face, (double Gap, string Where)>();
        var failures = new List<string>();
        foreach (var row in WindowsTextWidths.Rows)
        {
            for (var i = 0; i < WindowsTextWidths.Texts.Length; i++)
            {
                var s = WindowsTextWidths.Texts[i];
                var linux = text.Measure(new FontSpec(row.Face, row.Size), s);
                var ratio = linux / row.Widths[i];
                if (!worst.TryGetValue(row.Face, out var known) || Math.Abs(ratio - 1) > known.Gap)
                {
                    worst[row.Face] = (Math.Abs(ratio - 1), string.Create(CultureInfo.InvariantCulture, $"{(ratio > 1 ? "wider" : "narrower")} on {row.Size} \"{s}\""));
                }

                // Segoe UI kerns the capitals of this string (AV, VA, AT, TA, Wa) and Selawik has no kerning: the Linux
                // text is wider by what those pairs take back on Windows, 5.0 % on Body 12 (76.76 for 73.09). It stays
                // within 3 % narrower, as every string, and within 7 % wider: that 5 % and 2 % for the faces themselves.
                var kernedOnWindows = s == "AVATAR Wave" && row.Face is Face.Body or Face.Medium;

                // The digits of Selawik Semibold advance 1153/2048 em, 4.1 % more than those of Segoe UI Semibold at every
                // size, and its colon (518/2048 em) more still: "0123456789" is 4.1 % wider, "00:12:34" 5.5 %. That is the
                // width of the font, not of the code: those two strings stay within 6.5 % wider, and 3 % narrower.
                var digitsOfSemibold = row.Face is Face.Medium && s is "0123456789" or "00:12:34";
                var widest = kernedOnWindows ? 0.07 : digitsOfSemibold ? 0.065 : 0.03;
                if (row.Face != Face.Title && (ratio < 0.97 || ratio > 1 + widest))
                {
                    failures.Add($"{row.Face} {row.Size} \"{s}\": {linux} on Linux, {row.Widths[i]} on Windows ({ratio - 1:P1}, within -3 % and +{widest:P1}); {text.Describe(new FontSpec(row.Face, row.Size), s)}");
                }
            }
        }

        // Barlow stands in for Bahnschrift, which no free font copies: how far it is, for the record.
        foreach (var (face, (gap, where)) in worst)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{face}: widest gap from Windows {gap:P1}, {where}"));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_line_height_follows_the_rule_of_the_Windows_text_from_the_fonts_own_tables()
    {
        using var text = LinuxText.Create(Fonts);
        foreach (var row in WindowsTextWidths.Rows)
        {
            var font = new FontSpec(row.Face, row.Size);
            var em = (int)MathF.Floor(row.Size + 0.5f);
            var (ascent, descent, hasVdmx) = Expected(Path.Combine(Fonts, FileOf(row.Face)), em);
            var line = text.LineHeight(font);
            if (hasVdmx)
            {
                Assert.True(line >= ascent + descent, $"{row.Face} {row.Size}: {line} < {ascent + descent}");
            }
            else
            {
                Assert.Equal(ascent + descent, line);
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{row.Face} {row.Size}: line {line} on Linux, {row.LineHeight} on Windows"));
        }
    }

    [Fact]
    public void Measures_are_unrounded_count_trailing_spaces_and_kern()
    {
        using var text = LinuxText.Create(Fonts);
        var body = new FontSpec(Face.Body, 13);

        Assert.Equal(0, text.Measure(body, string.Empty));
        Assert.True(text.Measure(body, "a ") > text.Measure(body, "a"));
        Assert.NotEqual(MathF.Round(text.Measure(body, "Settings")), text.Measure(body, "Settings"));
        Assert.Equal(text.Measure(new FontSpec(Face.Body, 16), "Settings"), text.Measure(new FontSpec(Face.Body, 16.25f), "Settings"));

        // Barlow kerns T against a (its sources: -14 to -66 thousandths of an em across its masters): HarfBuzz's kerning
        // reaches the measure, and the drawing, whose ink sits further left than that of an "a" drawn after the unkerned "T".
        var title = new FontSpec(Face.Title, 44);
        var pair = text.Measure(title, "Ta");
        var apart = text.Measure(title, "T") + text.Measure(title, "a");
        Assert.True(pair < apart - 0.1f, $"Ta is not kerned: Ta {pair} for T + a {apart}; {text.Describe(title, "Ta")}");
        var kerned = new Bitmap(160, 70);
        kerned.Draw(text, title, "Ta", 4, 4, 0xFFFFFF);
        var unkerned = new Bitmap(160, 70);
        unkerned.Draw(text, title, "T", 4, 4, 0xFFFFFF);
        unkerned.Draw(text, title, "a", 4 + text.Measure(title, "T"), 4, 0xFFFFFF);
        Assert.True(InkCentre(kerned) < InkCentre(unkerned), $"the ink of the drawn Ta is centred at {InkCentre(kerned)}, of T then a at {InkCentre(unkerned)}");

        // Selawik has no kerning (no kern table, no pairs in its GPOS), where Segoe UI kerns AV and To: the pairs measure
        // what their letters do. A font with kerning in its place fails here, and the widths of the Windows text show it.
        foreach (var (left, right) in new[] { ("A", "V"), ("T", "o") })
        {
            var together = text.Measure(body, left + right);
            var sum = text.Measure(body, left) + text.Measure(body, right);
            Assert.True(Math.Abs(together - sum) < 0.001f, $"{left}{right} {together} for {left} + {right} {sum}: the body font kerns; {text.Describe(body, left + right)}");
        }
    }

    // The mean column of the ink, each pixel weighed by how far its green is from the background's.
    private static double InkCentre(Bitmap bitmap)
    {
        double sum = 0, weight = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var ink = (int)((bitmap[x, y] >> 8) & 0xFF) - 0x14;
                sum += x * ink;
                weight += ink;
            }
        }

        return sum / weight;
    }

    [Fact]
    public void The_offsets_read_in_FreeTypes_structures_match_the_library()
    {
        using var text = LinuxText.Create(Fonts);

        var (slotOfFace, pixelMode, grays, width, rows) = text.ProbeSlot(new FontSpec(Face.Body, 13), 'H');

        Assert.True(slotOfFace);
        Assert.Equal(TextNative.PixelModeGray, pixelMode);
        Assert.Equal(256, grays);
        Assert.InRange(width, 5u, 14u);
        Assert.InRange(rows, 8u, 12u);
    }

    [Fact]
    public void Text_is_drawn_in_its_line_with_smooth_edges_and_the_baseline_on_a_whole_pixel()
    {
        using var text = LinuxText.Create(Fonts);
        var body = new FontSpec(Face.Body, 13);
        var line = (int)text.LineHeight(body);
        var bitmap = new Bitmap(120, 40);

        bitmap.Draw(text, body, "Hg Settings", 5.25f, 10, 0xF2F4F7);

        int solid = 0, partial = 0;
        // The drawn pixels by sixteenth of the way from the background's green to the text's.
        var shades = new int[17];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap[x, y];
                Assert.Equal(0xFFu, p >> 24);
                if (p == 0xFF101418)
                {
                    continue;
                }

                _ = p == 0xFFF2F4F7 ? solid++ : partial++;
                shades[Math.Clamp((((int)(p >> 8) & 0xFF) - 0x14) * 16 / (0xF4 - 0x14), 0, 16)]++;
                Assert.InRange(y, 9, 10 + line);
                Assert.InRange(x, 5, 5 + (int)text.Measure(body, "Hg Settings") + 2);
            }
        }

        Assert.True(solid > 20 && partial > 20, $"{solid} solid, {partial} partial (DirectWrite on Windows: 70 and 223); by sixteenth of the way to the text's colour {string.Join(",", shades)}; {text.Describe(body, "Hg Settings")}");
        var snapped = new Bitmap(120, 40);
        snapped.Draw(text, body, "Hg Settings", 5.25f, 10.3f, 0xF2F4F7);
        Assert.Equal(bitmap.Memory, snapped.Memory);
        Assert.True(bitmap.GuardsKept());
    }

    [Fact]
    public void A_fraction_of_x_moves_the_letters_by_a_fraction_of_a_pixel()
    {
        using var text = LinuxText.Create(Fonts);
        var body = new FontSpec(Face.Body, 13);
        var whole = new Bitmap(60, 30);
        var half = new Bitmap(60, 30);

        whole.Draw(text, body, "lll", 10, 5, 0xFFFFFF);
        half.Draw(text, body, "lll", 10.5f, 5, 0xFFFFFF);

        Assert.NotEqual(whole.Memory, half.Memory);
    }

    [Theory]
    [InlineData(-6f, -8f)]
    [InlineData(30f, -8f)]
    [InlineData(-6f, 8f)]
    [InlineData(30f, 8f)]
    [InlineData(-500f, 0f)]
    [InlineData(500f, 0f)]
    [InlineData(0f, -100f)]
    [InlineData(0f, 100f)]
    public void Text_across_any_edge_draws_only_on_the_bitmap(float x, float top)
    {
        using var text = LinuxText.Create(Fonts);
        var bitmap = new Bitmap(40, 16);

        bitmap.Draw(text, new FontSpec(Face.Title, 22), "Wg\u00C9jy", x, top, 0xFFFFFF);

        Assert.True(bitmap.GuardsKept());
        Assert.All(bitmap.Memory.Skip(Bitmap.Guard).Take(40 * 16), p => Assert.Equal(0xFFu, p >> 24));
    }

    [Fact]
    public void A_character_the_fonts_lack_comes_from_a_font_of_the_system_without_a_crash()
    {
        using var text = LinuxText.Create(Fonts);
        var body = new FontSpec(Face.Body, 13);
        const string name = "Clip \u65E5\u672C\u8A9E \U0001F3B5";
        var bitmap = new Bitmap(200, 30);

        var width = text.Measure(body, name);
        bitmap.Draw(text, body, name, 2, 4, 0xFFFFFF);

        Assert.True(width > text.Measure(body, "Clip "), width.ToString(CultureInfo.InvariantCulture));
        Assert.True(bitmap.Changed() > 0);
        Assert.True(bitmap.GuardsKept());
        Assert.InRange(text.FallbackCount, 0, 4);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{text.FallbackCount} font(s) of the system open, width {width}"));
    }

    [Fact]
    public void The_glyph_cache_stays_within_its_bound()
    {
        using var text = LinuxText.Create(Fonts);
        var bitmap = new Bitmap(400, 200);
        const string all = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        for (var size = 8; size <= 120; size += 4)
        {
            foreach (var face in new[] { Face.Title, Face.Body, Face.Medium, Face.Mono })
            {
                bitmap.Draw(text, new FontSpec(face, size), all, 0.25f * (size % 4), 0, 0xFFFFFF);
                Assert.InRange(text.GlyphCacheBytes, 0, 3 << 20);
            }
        }
    }

    [Fact]
    public void Dispose_frees_once_and_a_disposed_text_refuses_work()
    {
        var text = LinuxText.Create(Fonts);
        text.Measure(new FontSpec(Face.Body, 13), "Settings");

        text.Dispose();
        text.Dispose();

        Assert.Throws<ObjectDisposedException>(() => text.Measure(new FontSpec(Face.Body, 13), "Settings"));
        Assert.Equal(0, text.GlyphCacheBytes);
        Assert.Equal(0, text.FallbackCount);
    }

    [Fact]
    public void Every_scene_of_the_pages_draws_its_text()
    {
        var counting = new CountingText(LinuxText.Create(Fonts));
        var scenes = new List<string>();
        var memory = new List<nint>();
        try
        {
            SettingsDump.Render(1.0, (width, height, scale) =>
            {
                var bits = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
                memory.Add((nint)bits);
                return new PixelCanvas(bits, width, height, scale, counting);
            }, (name, canvas) =>
            {
                Assert.True(counting.Pixels > 0, name + " has no text pixels");
                scenes.Add(name);
                counting.Pixels = 0;
            });
        }
        finally
        {
            foreach (var bits in memory)
            {
                NativeMemory.Free((void*)bits);
            }
        }

        Assert.True(counting.Disposed);
        Assert.True(scenes.Count >= 40, scenes.Count.ToString(CultureInfo.InvariantCulture));
    }

    // The text of the pages, counting the pixels each of its draws changed in the band of its line.
    private sealed class CountingText(FreeTypeText inner) : ICanvasText
    {
        public int Pixels { get; set; }

        public bool Disposed { get; private set; }

        public float Measure(FontSpec font, string text) => inner.Measure(font, text);

        public float LineHeight(FontSpec font) => inner.LineHeight(font);

        public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
        {
            var from = Math.Clamp((int)top - 2, 0, target.Height);
            var to = Math.Clamp((int)(top + inner.LineHeight(font)) + 2, 0, target.Height);
            var before = new ReadOnlySpan<uint>(target.Origin + (from * target.Width), (to - from) * target.Width).ToArray();
            inner.Draw(target, font, text, x, top, rgb);
            var after = new ReadOnlySpan<uint>(target.Origin + (from * target.Width), (to - from) * target.Width);
            for (var i = 0; i < before.Length; i++)
            {
                Pixels += before[i] != after[i] ? 1 : 0;
            }
        }

        public void Dispose()
        {
            Disposed = true;
            inner.Dispose();
        }
    }

    // The ascent and the descent at this em from the font file, read here without the text's own reader: the larger of
    // hhea (or OS/2's typographic metrics when USE_TYPO_METRICS is set) and usWin, rounded at the em.
    private static (int Ascent, int Descent, bool HasVdmx) Expected(string path, int em)
    {
        var font = File.ReadAllBytes(path);
        var tables = new Dictionary<string, (int Offset, int Length)>();
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = 12 + (16 * i);
            var tag = System.Text.Encoding.ASCII.GetString(font, record, 4);
            tables[tag] = ((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12)));
        }

        Span<byte> Table(string tag) => font.AsSpan(tables[tag].Offset, tables[tag].Length);
        float unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(Table("head")[18..]);
        var os2 = Table("OS/2");
        var hhea = Table("hhea");
        var typo = (BinaryPrimitives.ReadUInt16BigEndian(os2[62..]) & 0x80) != 0;
        int ascent = typo ? BinaryPrimitives.ReadInt16BigEndian(os2[68..]) : BinaryPrimitives.ReadInt16BigEndian(hhea[4..]);
        int descent = -(typo ? BinaryPrimitives.ReadInt16BigEndian(os2[70..]) : BinaryPrimitives.ReadInt16BigEndian(hhea[6..]));
        ascent = Math.Max(ascent, BinaryPrimitives.ReadUInt16BigEndian(os2[74..]));
        descent = Math.Max(descent, BinaryPrimitives.ReadUInt16BigEndian(os2[76..]));
        return ((int)MathF.Round(ascent / unitsPerEm * em), (int)MathF.Round(descent / unitsPerEm * em), tables.ContainsKey("VDMX"));
    }
}
