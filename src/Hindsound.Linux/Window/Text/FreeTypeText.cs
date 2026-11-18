using System.Runtime.InteropServices;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using static Hindsound.Linux.Window.Text.TextNative;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// The text of the window on Linux: the fonts the app ships, placed by HarfBuzz (kerning and ligatures of the font), drawn
/// by FreeType in grayscale, with the measures of the Windows text (see <see cref="ICanvasText"/>), so every layout of the
/// pages falls where it falls on Windows. A character the fonts lack comes from a font of the system that fontconfig finds.
/// <para>
/// Hinting is along y only, as DirectWrite's grayscale text does: baselines, x-heights and the heights of the horizontal
/// stems snap to whole pixels, so the letters stay crisp at 12 to 14 px, while x, the widths and the advances stay the design
/// ones the measures use. Glyphs are placed at a quarter of a pixel: the fraction of x that the Windows text keeps shows, and
/// a glyph is drawn at most four times per size.
/// </para>
/// <para>
/// One thread only, the window's loop: FreeType faces, the HarfBuzz buffer and the caches are not locked. Everything native
/// is freed by <see cref="Dispose"/>.
/// </para>
/// </summary>
internal sealed unsafe class FreeTypeText : ICanvasText
{
    /// <summary>The variable that points the text to another folder of fonts (the CI's).</summary>
    public const string FontDirectoryVariable = "HINDSOUND_FONT_DIR";

    // The caches together stay near 4 MB: the glyph bitmaps up to 3 MB, the measures and the fallback choices a few
    // thousand entries. A cache that is full is emptied whole: the pages draw the same few hundred strings again and again.
    private const int GlyphBytes = 3 << 20;
    private const int GlyphOverhead = 96;
    private const int Measures = 4096;
    private const int Choices = 4096;
    private const int Fallbacks = 4;

    // Quarter-pixel positions along x.
    private const int Subpixels = 4;

    private static readonly (Face Face, string File, string Name, int Weight)[] Fonts =
    [
        (Face.Title, "BarlowSemiCondensed-SemiBold.ttf", "the font of the headings", 180),
        (Face.Body, "Selawik-Regular.ttf", "the font of the text", 80),
        (Face.Medium, "Selawik-Semibold.ttf", "the font of the buttons", 180),
        (Face.Mono, "CascadiaMono-Regular.ttf", "the monospaced font", 80),
    ];

    private readonly nint _library;
    private readonly nint _buffer;
    private readonly FontFile[] _faces = new FontFile[Fonts.Length];
    private readonly List<FontFile> _fallbacks = [];
    private readonly FontFallback _fallback = new();
    private readonly Dictionary<(Face, int Em, string Text), float> _measures = [];
    private readonly Dictionary<(Face, int CodePoint), FontFile?> _choices = [];
    private readonly Dictionary<(int File, int Em, uint Glyph, int Subpixel), Glyph> _glyphs = [];
    private int _glyphBytes;
    private int _nextId = Fonts.Length;
    private bool _disposed;

    /// <summary>The fonts of <see cref="FontDirectory"/>.</summary>
    public FreeTypeText()
        : this(FontDirectory(Environment.GetEnvironmentVariable(FontDirectoryVariable), AppContext.BaseDirectory))
    {
    }

    /// <exception cref="TextUnavailableException">A font of the app is missing or unreadable, or a library cannot be loaded.</exception>
    public FreeTypeText(string fontDirectory)
    {
        foreach (var (_, file, name, _) in Fonts)
        {
            if (!File.Exists(Path.Combine(fontDirectory, file)))
            {
                throw new TextUnavailableException($"{Capital(name)} ({file}) is missing.");
            }
        }

        foreach (var library in new[] { FreeType, HarfBuzz, Fontconfig })
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
            {
                throw new TextUnavailableException($"{library} cannot be loaded.");
            }

            NativeLibrary.Free(handle);
        }

        try
        {
            if (FT_Init_FreeType(out _library) != 0)
            {
                throw new TextUnavailableException($"{FreeType} does not start.");
            }

            for (var i = 0; i < Fonts.Length; i++)
            {
                var (face, file, name, _) = Fonts[i];
                _faces[(int)face] = FontFile.Open(_library, (int)face, Path.Combine(fontDirectory, file), 0)
                    ?? throw new TextUnavailableException($"{Capital(name)} ({file}) cannot be read.");
            }

            _buffer = hb_buffer_create();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Dispose();
            throw new TextUnavailableException("A function of the text libraries is missing (" + ex.GetType().Name + ").", ex);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The bytes the glyph cache holds now, for the tests of its bound.</summary>
    internal int GlyphCacheBytes => _glyphBytes;

    /// <summary>How many fonts of the system are open now.</summary>
    internal int FallbackCount => _fallbacks.Count;

    /// <summary>The folder of the fonts: the variable's if it is set, else <c>fonts</c> next to the app.</summary>
    public static string FontDirectory(string? variable, string appDirectory) =>
        string.IsNullOrEmpty(variable) ? Path.Combine(appDirectory, "fonts") : variable;

    public float Measure(FontSpec font, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (text.Length == 0)
        {
            return 0;
        }

        var em = Em(font);
        var key = (font.Face, em, text);
        if (_measures.TryGetValue(key, out var known))
        {
            return known;
        }

        double width = 0;
        foreach (var (file, start, length) in Runs(font.Face, text))
        {
            var positions = Shape(file, text, start, length, out _, out var count);
            long advance = 0;
            for (var i = 0; i < count; i++)
            {
                advance += positions[i].XAdvance;
            }

            width += (double)advance * em / file.Metrics.UnitsPerEm;
        }

        if (_measures.Count >= Measures)
        {
            _measures.Clear();
        }

        _measures[key] = (float)width;
        return (float)width;
    }

    public float LineHeight(FontSpec font)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (ascent, descent) = _faces[(int)font.Face].Metrics.AtEm(Em(font));
        return ascent + descent;
    }

    public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (text.Length == 0)
        {
            return;
        }

        var em = Em(font);
        var baseline = (int)MathF.Floor(top + _faces[(int)font.Face].Metrics.AtEm(em).Ascent + 0.5f);
        double pen = x;
        foreach (var (file, start, length) in Runs(font.Face, text))
        {
            var positions = Shape(file, text, start, length, out var infos, out var count);
            var scale = (double)em / file.Metrics.UnitsPerEm;
            file.SetEm(em);
            for (var i = 0; i < count; i++)
            {
                var gx = pen + (positions[i].XOffset * scale);
                var gy = baseline - (int)Math.Round(positions[i].YOffset * scale);
                var column = (int)Math.Floor(gx);
                var subpixel = (int)Math.Round((gx - column) * Subpixels);
                if (subpixel == Subpixels)
                {
                    column++;
                    subpixel = 0;
                }

                var glyph = GlyphOf(file, em, infos[i].Codepoint, subpixel);
                if (glyph.Width > 0)
                {
                    GlyphBlender.Blend(target, glyph.Coverage, glyph.Width, glyph.Height, column + glyph.Left, gy - glyph.Top, rgb);
                }

                pen += positions[i].XAdvance * scale;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_buffer != 0)
        {
            hb_buffer_destroy(_buffer);
        }

        foreach (var file in _faces)
        {
            file?.Dispose();
        }

        foreach (var file in _fallbacks)
        {
            file.Dispose();
        }

        _fallbacks.Clear();
        _fallback.Dispose();
        if (_library != 0)
        {
            FT_Done_FreeType(_library);
        }

        _measures.Clear();
        _choices.Clear();
        _glyphs.Clear();
        _glyphBytes = 0;
    }

    /// <summary>What FreeType's glyph slot says after drawing one character of this font: the check that the offsets this
    /// file reads match the library's structures.</summary>
    internal (bool SlotOfFace, byte PixelMode, ushort Grays, uint Width, uint Rows) ProbeSlot(FontSpec font, char character)
    {
        var file = _faces[(int)font.Face];
        file.SetEm(Em(font));
        TextNative.FtVector delta = default;
        FT_Set_Transform(file.FtFace, 0, &delta);
        FT_Load_Glyph(file.FtFace, FT_Get_Char_Index(file.FtFace, character), LoadNoBitmap | LoadTargetLight | LoadRender);
        var slot = *(nint*)(file.FtFace + FaceGlyphOffset);
        var bitmap = slot + SlotBitmapOffset;
        return (*(nint*)(slot + SlotFaceOffset) == file.FtFace, *(byte*)(bitmap + BitmapPixelModeOffset),
            *(ushort*)(bitmap + BitmapNumGraysOffset), *(uint*)(bitmap + BitmapWidthOffset), *(uint*)(bitmap + BitmapRowsOffset));
    }

    /// <summary>What the text does with this string, for the messages of the tests that run on Linux only: HarfBuzz's
    /// version, the tables of each font that kern or hint, every glyph's advance, and the highest coverage of its
    /// glyphs at each quarter-pixel shift, with how many were drawn with their stems snapped and how many of their pixels fall
    /// in each eighth of coverage.</summary>
    internal string Describe(FontSpec font, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var em = Em(font);
        var report = new System.Text.StringBuilder();
        report.Append("HarfBuzz ").Append(Marshal.PtrToStringUTF8((nint)hb_version_string()));
        foreach (var (file, start, length) in Runs(font.Face, text))
        {
            report.Append(" | ").Append(file.Name).Append(" tables:");
            foreach (var tag in new[] { "GPOS", "GSUB", "kern", "fpgm", "prep", "gasp" })
            {
                report.Append(' ').Append(tag).Append(file.HasTable(tag) ? "+" : "-");
            }

            var positions = Shape(file, text, start, length, out var infos, out var count);
            var glyphs = new uint[count];
            report.Append("; advances in units of ").Append(file.Metrics.UnitsPerEm).Append(':');
            for (var i = 0; i < count; i++)
            {
                glyphs[i] = infos[i].Codepoint;
                report.Append(System.FormattableString.Invariant($" #{infos[i].Codepoint}@{infos[i].Cluster}={positions[i].XAdvance}"));
                if (positions[i].XOffset != 0)
                {
                    report.Append(System.FormattableString.Invariant($"(dx {positions[i].XOffset})"));
                }
            }

            file.SetEm(em);
            for (var subpixel = 0; subpixel < Subpixels; subpixel++)
            {
                int highest = 0, snapped = 0;
                var steps = new int[GlyphBlender.Levels + 1];
                foreach (var glyph in glyphs)
                {
                    var drawn = GlyphOf(file, em, glyph, subpixel);
                    highest = Math.Max(highest, drawn.Coverage.Length == 0 ? 0 : drawn.Coverage.Max());
                    snapped += drawn.Snapped ? 1 : 0;
                    foreach (var c in drawn.Coverage)
                    {
                        steps[GlyphBlender.Level(c)] += c == 0 ? 0 : 1;
                    }
                }

                report.Append(System.FormattableString.Invariant($"; at +{subpixel}/{Subpixels} px highest coverage {highest}, {snapped}/{glyphs.Length} snapped, pixels by eighth of coverage {string.Join(",", steps)}"));
            }
        }

        return report.ToString();
    }

    private static int Em(FontSpec font) => Math.Max(1, (int)MathF.Floor(font.Size + 0.5f));

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private IEnumerable<(FontFile File, int Start, int Length)> Runs(Face face, string text)
    {
        var primary = _faces[(int)face];
        var files = new List<FontFile> { primary };
        var runs = TextRuns.Split(text, codePoint =>
        {
            var file = Choose(face, primary, codePoint, files);
            var index = files.IndexOf(file);
            if (index < 0)
            {
                files.Add(file);
                index = files.Count - 1;
            }

            return index;
        });
        foreach (var run in runs)
        {
            yield return (files[run.Font], run.Start, run.Length);
        }
    }

    // The font of one character: the page's when it has it (or for a space or a control, which it draws as nothing), else
    // the system's that fontconfig names, else the page's again, whose missing-glyph box is drawn.
    private FontFile Choose(Face face, FontFile primary, int codePoint, List<FontFile> inUse)
    {
        if (codePoint < 0x21 || primary.Covers(codePoint))
        {
            return primary;
        }

        if (_choices.TryGetValue((face, codePoint), out var chosen))
        {
            return chosen ?? primary;
        }

        chosen = Fallback(codePoint, Fonts[Array.FindIndex(Fonts, f => f.Face == face)].Weight, inUse);
        if (_choices.Count >= Choices)
        {
            _choices.Clear();
        }

        _choices[(face, codePoint)] = chosen;
        return chosen ?? primary;
    }

    // inUse: the fonts the string being cut already uses, which must stay open until it is drawn.
    private FontFile? Fallback(int codePoint, int weight, List<FontFile> inUse)
    {
        foreach (var open in _fallbacks)
        {
            if (open.Covers(codePoint))
            {
                return open;
            }
        }

        var oldest = _fallbacks.FindIndex(open => !inUse.Contains(open));
        if ((_fallbacks.Count >= Fallbacks && oldest < 0) || _fallback.Find(codePoint, weight) is not var (path, index))
        {
            return null;
        }

        var file = FontFile.Open(_library, _nextId++, path, index);
        if (file is null || !file.Covers(codePoint))
        {
            file?.Dispose();
            return null;
        }

        if (_fallbacks.Count >= Fallbacks)
        {
            // The oldest font of the system the string does not use goes, with everything that may point to it.
            _fallbacks[oldest].Dispose();
            _fallbacks.RemoveAt(oldest);
            _choices.Clear();
            _glyphs.Clear();
            _glyphBytes = 0;
        }

        _fallbacks.Add(file);
        return file;
    }

    private GlyphPosition* Shape(FontFile file, string text, int start, int length, out GlyphInfo* infos, out int count)
    {
        hb_buffer_clear_contents(_buffer);
        fixed (char* chars = text)
        {
            hb_buffer_add_utf16(_buffer, (ushort*)chars, text.Length, (uint)start, length);
        }

        hb_buffer_guess_segment_properties(_buffer);
        hb_shape(file.HbFont, _buffer, 0, 0);
        infos = hb_buffer_get_glyph_infos(_buffer, out var infoCount);
        var positions = hb_buffer_get_glyph_positions(_buffer, out var positionCount);
        count = (int)Math.Min(infoCount, positionCount);
        return positions;
    }

    private Glyph GlyphOf(FontFile file, int em, uint glyphIndex, int subpixel)
    {
        var key = (file.Id, em, glyphIndex, subpixel);
        if (_glyphs.TryGetValue(key, out var known))
        {
            return known;
        }

        var glyph = Render(file, glyphIndex, subpixel);
        var bytes = glyph.Coverage.Length + GlyphOverhead;
        if (_glyphBytes + bytes > GlyphBytes)
        {
            _glyphs.Clear();
            _glyphBytes = 0;
        }

        _glyphs[key] = glyph;
        _glyphBytes += bytes;
        return glyph;
    }

    // The outline hinted along y only, with its horizontal stems snapped to whole pixels, then drawn in grayscale. FreeType
    // has no load mode for that: its light mode leaves the stem heights fractional (a 0.9 px bar of Selawik at 13 px never
    // fills a row, and the text looks blurred next to the Windows one), and the auto-hinter snaps them only in the modes
    // that also hint x. So the outline is loaded twice, at the same quarter-pixel shift: once unhinted, for the design x of
    // every point, and once by the auto-hinter for vertical LCD, whose y snaps the stems; the y of the second is kept with
    // the x of the first. Should the two outlines differ in their points, the light mode draws the glyph.
    private static Glyph Render(FontFile file, uint glyphIndex, int subpixel)
    {
        TextNative.FtVector delta = new() { X = subpixel * 64 / Subpixels };
        FT_Set_Transform(file.FtFace, 0, &delta);
        var slot = *(nint*)(file.FtFace + FaceGlyphOffset);
        var outline = slot + SlotOutlineOffset;
        if (FT_Load_Glyph(file.FtFace, glyphIndex, LoadNoBitmap | LoadNoHinting) != 0)
        {
            return Glyph.Empty;
        }

        var loaded = false;
        if (*(uint*)(slot + SlotFormatOffset) == GlyphFormatOutline)
        {
            int count = *(short*)(outline + OutlinePointCountOffset);
            var designX = new long[Math.Max(0, count)];
            var points = *(TextNative.FtVector**)(outline + OutlinePointsOffset);
            for (var i = 0; i < designX.Length; i++)
            {
                designX[i] = points[i].X;
            }

            if (FT_Load_Glyph(file.FtFace, glyphIndex, LoadNoBitmap | LoadForceAutohint | LoadTargetLcdV) == 0
                && *(uint*)(slot + SlotFormatOffset) == GlyphFormatOutline
                && *(short*)(outline + OutlinePointCountOffset) == count)
            {
                points = *(TextNative.FtVector**)(outline + OutlinePointsOffset);
                for (var i = 0; i < designX.Length; i++)
                {
                    points[i].X = designX[i];
                }

                loaded = FT_Render_Glyph(slot, RenderModeNormal) == 0;
            }
        }

        if (!loaded && FT_Load_Glyph(file.FtFace, glyphIndex, LoadNoBitmap | LoadTargetLight | LoadRender) != 0)
        {
            return Glyph.Empty;
        }

        var bitmap = slot + SlotBitmapOffset;
        var rows = (int)*(uint*)(bitmap + BitmapRowsOffset);
        var width = (int)*(uint*)(bitmap + BitmapWidthOffset);
        var pitch = *(int*)(bitmap + BitmapPitchOffset);
        var buffer = *(byte**)(bitmap + BitmapBufferOffset);
        var mode = *(byte*)(bitmap + BitmapPixelModeOffset);
        if (rows <= 0 || width <= 0 || buffer == null || (mode != PixelModeGray && mode != PixelModeMono))
        {
            return Glyph.Empty;
        }

        var coverage = new byte[rows * width];
        for (var r = 0; r < rows; r++)
        {
            // A negative pitch stores the rows bottom up from the start of the buffer.
            var source = buffer + ((pitch >= 0 ? r : rows - 1 - r) * (long)Math.Abs(pitch));
            for (var c = 0; c < width; c++)
            {
                coverage[(r * width) + c] = mode == PixelModeGray ? source[c] : (byte)(((source[c >> 3] >> (7 - (c & 7))) & 1) * 255);
            }
        }

        return new Glyph(*(int*)(slot + SlotBitmapLeftOffset), *(int*)(slot + SlotBitmapTopOffset), width, rows, coverage, loaded);
    }

    // Snapped: drawn with the stems snapped along y, not by the light mode.
    private sealed record Glyph(int Left, int Top, int Width, int Height, byte[] Coverage, bool Snapped = false)
    {
        public static readonly Glyph Empty = new(0, 0, 0, 0, []);
    }
}
