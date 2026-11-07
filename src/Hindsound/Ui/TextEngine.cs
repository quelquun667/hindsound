using System.Runtime.InteropServices;
using Hindsound.Ui.Window;

namespace Hindsound.Ui;

/// <summary>One font of the interface at one size, in physical pixels (the em size, as GDI's negative height was).</summary>
internal readonly record struct FontSpec(Face Face, float Size);

/// <summary>
/// What each <see cref="Face"/> asks for: the family, weight and stretch DirectWrite matches, the font that replaces a missing
/// family, and the face name GDI is given when DirectWrite could not start.
/// </summary>
internal static class FontFaces
{
    // DWRITE_FONT_STRETCH: 4 is SemiCondensed, 5 is Normal.
    public static (string Family, int Weight, int Stretch, string? Fallback) Of(Face face) => face switch
    {
        Face.Title => ("Bahnschrift", 600, 4, null),
        Face.Medium => ("Segoe UI", 600, 5, null),
        // Cascadia Mono is not on every Windows 10: Consolas keeps the digits and keys at a fixed pitch.
        Face.Mono => ("Cascadia Mono", 400, 5, "Consolas"),
        _ => ("Segoe UI", 400, 5, null),
    };

    public static string GdiName(Face face) => face switch
    {
        Face.Title => "Bahnschrift SemiBold SemiConden",
        Face.Medium => "Segoe UI Semibold",
        Face.Mono => "Cascadia Mono",
        _ => "Segoe UI",
    };
}

/// <summary>
/// DirectWrite for the shapes of the letters, Direct2D to lay them on the bitmaps already used. No COM interop of the runtime
/// (the exe is trimmed): each call goes through the interface's table of functions, at the index of d2d1.h or dwrite.h.
/// The factories are made once, on first use, and kept; this class is their only owner.
/// </summary>
internal sealed unsafe partial class TextEngine : IDisposable
{
    private const int FactoryMultiThreaded = 1, DWriteFactoryShared = 0, DWriteFactoryIsolated = 1;
    private const int WrapNone = 1, LineSpacingUniform = 1;
    private const int TargetSoftware = 1, FormatB8G8R8A8 = 87, AlphaPremultiplied = 1, AlphaIgnore = 3;
    private const int TextAntialiasClearType = 1, TextAntialiasGrayscale = 2;

    private static readonly Guid D2DFactoryId = new("06152247-6f50-465a-9245-118bfd3b6007");
    private static readonly Guid DWriteFactoryId = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    private static readonly object SharedGate = new();
    private static TextEngine? _shared;
    private static bool _sharedTried;

    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly HashSet<string> _missing;
    private readonly Dictionary<FontSpec, Format> _formats = [];
    private readonly Dictionary<Face, string> _families = [];
    private readonly HashSet<string> _told = [];
    private nint _d2d, _dwrite, _collection;

    /// <summary>Where the engine writes its few lines: the app's journal, set at start.</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>The engine of the app, or null when DirectWrite or Direct2D did not start: the text is then drawn by GDI.</summary>
    public static TextEngine? Shared
    {
        get
        {
            lock (SharedGate)
            {
                if (!_sharedTried)
                {
                    _sharedTried = true;
                    _shared = TryCreate(line => Log?.Invoke(line));
                    if (_shared is not null)
                    {
                        Log?.Invoke("text: drawn with DirectWrite");
                    }
                }

                return _shared;
            }
        }
    }

    private TextEngine(Action<string> log, IEnumerable<string>? missing)
    {
        _log = log;
        _missing = new HashSet<string>(missing ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <param name="log">Where to tell a failure or a replaced font.</param>
    /// <param name="missingForTests">Families treated as not installed: how the tests reach the fallback without removing a font.</param>
    /// <param name="refuseForTests">Asks Direct2D for an interface that does not exist, so the start really fails.</param>
    public static TextEngine? TryCreate(Action<string> log, IEnumerable<string>? missingForTests = null, bool refuseForTests = false)
    {
        var engine = new TextEngine(log, missingForTests);
        try
        {
            var d2dId = refuseForTests ? Guid.Empty : D2DFactoryId;
            nint d2d;
            var hr = D2D1CreateFactory(FactoryMultiThreaded, &d2dId, null, &d2d);
            if (hr < 0)
            {
                throw new COMException("D2D1CreateFactory", hr);
            }

            engine._d2d = d2d;
            var dwriteId = DWriteFactoryId;
            nint dwrite;
            // A test engine gets a factory of its own, so counting its references is not disturbed by the app's.
            hr = DWriteCreateFactory(missingForTests is null ? DWriteFactoryShared : DWriteFactoryIsolated, &dwriteId, &dwrite);
            if (hr < 0)
            {
                throw new COMException("DWriteCreateFactory", hr);
            }

            engine._dwrite = dwrite;
            nint collection;
            // IDWriteFactory::GetSystemFontCollection, index 3.
            Check(((delegate* unmanaged<nint, nint*, int, int>)Vtbl(dwrite)[3])(dwrite, &collection, 0), "GetSystemFontCollection");
            engine._collection = collection;
            return engine;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            log($"text: DirectWrite unavailable ({ex.Message}, 0x{ex.HResult:X8}), drawing with GDI");
            engine.Dispose();
            return null;
        }
    }

    /// <summary>The family a face is drawn with, once a missing one is replaced.</summary>
    public string FamilyOf(Face face)
    {
        lock (_gate)
        {
            if (!_families.TryGetValue(face, out var name))
            {
                var (family, _, _, fallback) = FontFaces.Of(face);
                name = Resolve(family, fallback);
                _families[face] = name;
            }

            return name;
        }
    }

    /// <summary>The weight and stretch of the font DirectWrite matched for a face, to check the right one is used.</summary>
    public (int Weight, int Stretch) MatchedStyleOf(Face face)
    {
        var (_, weight, stretch, _) = FontFaces.Of(face);
        nint font = MatchFont(FamilyOf(face), weight, stretch);
        if (font == 0)
        {
            return (0, 0);
        }

        var vtbl = Vtbl(font);
        // IDWriteFont::GetWeight, index 4; GetStretch, index 5.
        var result = (((delegate* unmanaged<nint, int>)vtbl[4])(font), ((delegate* unmanaged<nint, int>)vtbl[5])(font));
        Release(font);
        return result;
    }

    public float LineHeight(FontSpec font) => FormatOf(font).LineHeight;

    public float Measure(FontSpec font, string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var layout = CreateLayout(font, text);
        try
        {
            return WidthOf(layout);
        }
        finally
        {
            Release(layout);
        }
    }

    /// <summary>A Direct2D target for a 32-bit DIB selected in a memory DC, and the brush the text is painted with.</summary>
    /// <param name="opaque">An opaque bitmap (the window); a premultiplied alpha otherwise (the notification).</param>
    /// <param name="clearType">ClearType, on an opaque bitmap only: it needs to know what is below each letter. Grey otherwise.</param>
    public bool CreateTarget(bool opaque, bool clearType, out nint target, out nint brush)
    {
        target = 0;
        brush = 0;
        clearType &= opaque;
        RenderTargetProperties properties = new()
        {
            Type = TargetSoftware,
            Format = FormatB8G8R8A8,
            AlphaMode = opaque ? AlphaIgnore : AlphaPremultiplied,
            DpiX = 96,
            DpiY = 96,
        };
        nint created;
        // ID2D1Factory::CreateDCRenderTarget, index 16.
        var hr = ((delegate* unmanaged<nint, RenderTargetProperties*, nint*, int>)Vtbl(_d2d)[16])(_d2d, &properties, &created);
        if (hr < 0)
        {
            TellOnce("target", $"text: no Direct2D target (0x{hr:X8}), drawing with GDI");
            return false;
        }

        // ID2D1RenderTarget::SetTextAntialiasMode, index 34.
        ((delegate* unmanaged<nint, int, void>)Vtbl(created)[34])(created, clearType ? TextAntialiasClearType : TextAntialiasGrayscale);
        ColorF white = new() { R = 1, G = 1, B = 1, A = 1 };
        nint solid;
        // ID2D1RenderTarget::CreateSolidColorBrush, index 8.
        hr = ((delegate* unmanaged<nint, ColorF*, void*, nint*, int>)Vtbl(created)[8])(created, &white, null, &solid);
        if (hr < 0)
        {
            Release(created);
            TellOnce("brush", $"text: no Direct2D brush (0x{hr:X8}), drawing with GDI");
            return false;
        }

        target = created;
        brush = solid;
        return true;
    }

    /// <summary>Draws one line with its top at (x, top), in pixels of the bitmap. Only the box around the text is bound.</summary>
    public void Draw(nint target, nint brush, nint dc, int width, int height, FontSpec font, string text, float x, float top, uint rgb)
    {
        var layout = CreateLayout(font, text);
        try
        {
            var pad = MathF.Ceiling(font.Size / 2) + 2;
            var lineHeight = FormatOf(font).LineHeight;
            Native.RECT box = new()
            {
                Left = Math.Max(0, (int)MathF.Floor(x - pad)),
                Top = Math.Max(0, (int)MathF.Floor(top - pad)),
                Right = Math.Min(width, (int)MathF.Ceiling(x + WidthOf(layout) + pad)),
                Bottom = Math.Min(height, (int)MathF.Ceiling(top + lineHeight + pad)),
            };
            if (box.Right <= box.Left || box.Bottom <= box.Top)
            {
                return;
            }

            var vtbl = Vtbl(target);
            // ID2D1DCRenderTarget::BindDC, index 57.
            var hr = ((delegate* unmanaged<nint, nint, Native.RECT*, int>)vtbl[57])(target, dc, &box);
            if (hr < 0)
            {
                TellOnce("bind", $"text: BindDC failed (0x{hr:X8})");
                return;
            }

            ColorF color = new() { R = ((rgb >> 16) & 0xFF) / 255f, G = ((rgb >> 8) & 0xFF) / 255f, B = (rgb & 0xFF) / 255f, A = 1 };
            // ID2D1SolidColorBrush::SetColor, index 8.
            ((delegate* unmanaged<nint, ColorF*, void>)Vtbl(brush)[8])(brush, &color);
            // ID2D1RenderTarget::BeginDraw, index 48.
            ((delegate* unmanaged<nint, void>)vtbl[48])(target);
            // ID2D1RenderTarget::DrawTextLayout, index 28. Options 0: the baseline snaps to a pixel, x keeps its fraction.
            ((delegate* unmanaged<nint, Point2F, nint, nint, int, void>)vtbl[28])(target, new Point2F { X = x - box.Left, Y = top - box.Top }, layout, brush, 0);
            // ID2D1RenderTarget::EndDraw, index 49.
            hr = ((delegate* unmanaged<nint, ulong*, ulong*, int>)vtbl[49])(target, null, null);
            if (hr < 0)
            {
                TellOnce("draw", $"text: EndDraw failed (0x{hr:X8})");
            }
        }
        finally
        {
            Release(layout);
        }
    }

    /// <summary>References held on the two factories: what the tests count before and after.</summary>
    internal uint FactoryReferences() => References(_d2d) + References(_dwrite);

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var format in _formats.Values)
            {
                Release(format.Pointer);
            }

            _formats.Clear();
            Release(_collection);
            Release(_dwrite);
            Release(_d2d);
            _collection = _dwrite = _d2d = 0;
        }
    }

    public static void Release(nint unknown)
    {
        if (unknown != 0)
        {
            // IUnknown::Release, index 2.
            ((delegate* unmanaged<nint, uint>)Vtbl(unknown)[2])(unknown);
        }
    }

    private static uint References(nint unknown)
    {
        if (unknown == 0)
        {
            return 0;
        }

        // IUnknown::AddRef, index 1, then Release, index 2, which returns the count left.
        ((delegate* unmanaged<nint, uint>)Vtbl(unknown)[1])(unknown);
        return ((delegate* unmanaged<nint, uint>)Vtbl(unknown)[2])(unknown);
    }

    private static void** Vtbl(nint unknown) => *(void***)unknown;

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException(what, hr);
        }
    }

    private void TellOnce(string key, string line)
    {
        lock (_told)
        {
            if (_told.Add(key))
            {
                _log(line);
            }
        }
    }

    private static float WidthOf(nint layout)
    {
        TextMetrics metrics;
        // IDWriteTextLayout::GetMetrics, index 60.
        Check(((delegate* unmanaged<nint, TextMetrics*, int>)Vtbl(layout)[60])(layout, &metrics), "GetMetrics");
        return metrics.WidthIncludingTrailingWhitespace;
    }

    private nint CreateLayout(FontSpec font, string text)
    {
        var format = FormatOf(font).Pointer;
        nint layout;
        fixed (char* chars = text)
        {
            // IDWriteFactory::CreateTextLayout, index 18 (CreateTypography and GetGdiInterop come before it). No wrapping, so the width given only bounds nothing.
            Check(((delegate* unmanaged<nint, char*, uint, nint, float, float, nint*, int>)Vtbl(_dwrite)[18])(
                _dwrite, chars, (uint)text.Length, format, 100000, 100000, &layout), "CreateTextLayout");
        }

        return layout;
    }

    private Format FormatOf(FontSpec font)
    {
        lock (_gate)
        {
            if (_formats.TryGetValue(font, out var known))
            {
                return known;
            }

            var (_, weight, stretch, _) = FontFaces.Of(font.Face);
            var family = FamilyOf(font.Face);
            // The em in whole pixels, as GDI's negative height had it: the sizes of the window stay those of today.
            var em = MathF.Floor(font.Size + 0.5f);
            var (ascent, descent) = VerticalMetrics(family, weight, stretch, em);
            nint format;
            fixed (char* name = family)
            fixed (char* locale = "en-us")
            {
                // IDWriteFactory::CreateTextFormat, index 15. Style 0 is upright.
                Check(((delegate* unmanaged<nint, char*, nint, int, int, int, float, char*, nint*, int>)Vtbl(_dwrite)[15])(
                    _dwrite, name, _collection, weight, 0, stretch, em, locale, &format), "CreateTextFormat");
            }

            var vtbl = Vtbl(format);
            // IDWriteTextFormat::SetWordWrapping, index 5.
            ((delegate* unmanaged<nint, int, int>)vtbl[5])(format, WrapNone);
            // IDWriteTextFormat::SetLineSpacing, index 10. The line is ascent plus descent, the baseline at the ascent: where
            // GDI put them (TEXTMETRIC Height and Ascent), so the text stays at the same height as before.
            var lineHeight = ascent + descent;
            ((delegate* unmanaged<nint, int, float, float, int>)vtbl[10])(format, LineSpacingUniform, lineHeight, ascent);
            var created = new Format(format, lineHeight);
            _formats[font] = created;
            return created;
        }
    }

    // Ascent and descent in whole pixels at this em, from the font matched; Segoe UI's if the family is not there at all.
    private (float Ascent, float Descent) VerticalMetrics(string family, int weight, int stretch, float emPixels)
    {
        var font = MatchFont(family, weight, stretch);
        if (font == 0)
        {
            font = MatchFont("Segoe UI", weight, stretch);
        }

        if (font == 0)
        {
            return (MathF.Round(1.08f * emPixels), MathF.Round(0.25f * emPixels));
        }

        FontMetrics metrics;
        // IDWriteFont::GetMetrics, index 11.
        ((delegate* unmanaged<nint, FontMetrics*, void>)Vtbl(font)[11])(font, &metrics);
        float em = metrics.DesignUnitsPerEm;
        var (ascent, descent) = (metrics.Ascent / em, metrics.Descent / em);

        // DirectWrite gives the typographic metrics of fonts that ask for them (Bahnschrift, Cascadia Mono): shorter than the
        // Windows ones GDI always used, and GDI also grows them at some sizes (the VDMX table of Segoe UI). Every layout of the
        // window was centred with GDI's line, so the line keeps the larger of each, in whole pixels as TEXTMETRIC had them.
        if (GdiMetrics(font, emPixels) is var (gdiAscent, gdiDescent))
        {
            (ascent, descent) = (MathF.Max(ascent, gdiAscent / em), MathF.Max(descent, gdiDescent / em));
        }

        Release(font);
        return (MathF.Round(ascent * emPixels), MathF.Round(descent * emPixels));
    }

    // In design units: the larger of DirectWrite's GDI-compatible metrics at this em and of usWinAscent and usWinDescent in
    // the OS/2 table, where GDI's TEXTMETRIC Ascent and Descent come from.
    private static (float Ascent, float Descent)? GdiMetrics(nint font, float emPixels)
    {
        nint face;
        // IDWriteFont::CreateFontFace, index 13.
        if (((delegate* unmanaged<nint, nint*, int>)Vtbl(font)[13])(font, &face) < 0)
        {
            return null;
        }

        try
        {
            FontMetrics compatible;
            // IDWriteFontFace::GetGdiCompatibleMetrics, index 16. One pixel per DIP, no transform.
            var hasCompatible = ((delegate* unmanaged<nint, float, float, void*, FontMetrics*, int>)Vtbl(face)[16])(face, emPixels, 1, null, &compatible) >= 0;
            const uint os2 = 'O' | ('S' << 8) | ('/' << 16) | ('2' << 24);
            byte* table;
            uint size;
            void* context;
            int exists;
            // IDWriteFontFace::TryGetFontTable, index 12; ReleaseFontTable, index 13.
            if (((delegate* unmanaged<nint, uint, byte**, uint*, void**, int*, int>)Vtbl(face)[12])(face, os2, &table, &size, &context, &exists) < 0 || exists == 0)
            {
                return hasCompatible ? (compatible.Ascent, compatible.Descent) : null;
            }

            float winAscent = 0, winDescent = 0;
            if (size >= 78)
            {
                winAscent = (table[74] << 8) | table[75];
                winDescent = (table[76] << 8) | table[77];
            }

            ((delegate* unmanaged<nint, void*, void>)Vtbl(face)[13])(face, context);
            return hasCompatible ? (MathF.Max(winAscent, compatible.Ascent), MathF.Max(winDescent, compatible.Descent)) : (winAscent, winDescent);
        }
        finally
        {
            Release(face);
        }
    }

    private nint MatchFont(string family, int weight, int stretch)
    {
        if (!TryFindFamily(family, out var index))
        {
            return 0;
        }

        nint fontFamily, font = 0;
        // IDWriteFontCollection::GetFontFamily, index 4.
        if (((delegate* unmanaged<nint, uint, nint*, int>)Vtbl(_collection)[4])(_collection, index, &fontFamily) < 0)
        {
            return 0;
        }

        // IDWriteFontFamily::GetFirstMatchingFont, index 7. Style 0 is upright.
        if (((delegate* unmanaged<nint, int, int, int, nint*, int>)Vtbl(fontFamily)[7])(fontFamily, weight, stretch, 0, &font) < 0)
        {
            font = 0;
        }

        Release(fontFamily);
        return font;
    }

    private bool TryFindFamily(string family, out uint index)
    {
        index = 0;
        if (_missing.Contains(family))
        {
            return false;
        }

        uint found;
        int exists;
        fixed (char* name = family)
        {
            // IDWriteFontCollection::FindFamilyName, index 5.
            if (((delegate* unmanaged<nint, char*, uint*, int*, int>)Vtbl(_collection)[5])(_collection, name, &found, &exists) < 0)
            {
                return false;
            }
        }

        index = found;
        return exists != 0;
    }

    private string Resolve(string family, string? fallback)
    {
        if (TryFindFamily(family, out _))
        {
            return family;
        }

        if (fallback is not null && TryFindFamily(fallback, out _))
        {
            TellOnce("font " + family, $"text: font {family} not installed, using {fallback}");
            return fallback;
        }

        TellOnce("font " + family, $"text: font {family} not installed, DirectWrite picks the nearest one");
        return family;
    }

    private sealed record Format(nint Pointer, float LineHeight);

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderTargetProperties
    {
        public int Type, Format, AlphaMode;
        public float DpiX, DpiY;
        public int Usage, MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorF
    {
        public float R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point2F
    {
        public float X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FontMetrics
    {
        public ushort DesignUnitsPerEm, Ascent, Descent;
        public short LineGap;
        public ushort CapHeight, XHeight;
        public short UnderlinePosition;
        public ushort UnderlineThickness;
        public short StrikethroughPosition;
        public ushort StrikethroughThickness;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetrics
    {
        public float Left, Top, Width, WidthIncludingTrailingWhitespace, Height, LayoutWidth, LayoutHeight;
        public uint MaxBidiReorderingDepth, LineCount;
    }

    [LibraryImport("d2d1.dll")]
    private static partial int D2D1CreateFactory(int type, Guid* id, void* options, nint* factory);

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int type, Guid* id, nint* factory);
}

/// <summary>Text on a bitmap: DirectWrite through Direct2D when it started, GDI otherwise. The measures come from the engine that draws.</summary>
internal sealed unsafe class TextPainter : IDisposable
{
    private const int StockSystemFont = 13;
    private readonly nint _dc;
    private readonly Pixels _pixels;
    private readonly TextEngine? _engine;
    private readonly nint _target, _brush;
    private readonly Dictionary<FontSpec, nint> _gdiFonts = [];

    /// <param name="engine">Null draws with GDI.</param>
    /// <param name="clearType">For an opaque bitmap only: ClearType needs to know what is below each letter.</param>
    /// <param name="opaque">Whether the bitmap is opaque; by default, as ClearType is asked or not.</param>
    public TextPainter(nint dc, Pixels pixels, TextEngine? engine, bool clearType, bool? opaque = null)
    {
        _dc = dc;
        _pixels = pixels;
        if (engine is not null && engine.CreateTarget(opaque ?? clearType, clearType, out _target, out _brush))
        {
            _engine = engine;
        }
    }

    public bool UsesDirectWrite => _engine is not null;

    public float Measure(FontSpec font, string text)
    {
        if (_engine is not null)
        {
            return _engine.Measure(font, text);
        }

        Native.SelectObject(_dc, GdiFont(font));
        Native.SIZE size;
        Native.GetTextExtentPoint32W(_dc, text, text.Length, &size);
        return size.Cx;
    }

    public float LineHeight(FontSpec font)
    {
        if (_engine is not null)
        {
            return _engine.LineHeight(font);
        }

        Native.SelectObject(_dc, GdiFont(font));
        Native.TEXTMETRICW metrics;
        Native.GetTextMetricsW(_dc, &metrics);
        return metrics.Height;
    }

    /// <summary>The text, cut with an ellipsis if it would run past the width.</summary>
    public string Fit(FontSpec font, string text, float width)
    {
        if (Measure(font, text) <= width)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 0; length--)
        {
            var cut = text[..length].TrimEnd() + "...";
            if (Measure(font, cut) <= width)
            {
                return cut;
            }
        }

        return string.Empty;
    }

    public void Text(FontSpec font, string text, float x, float top, uint rgb)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (_engine is not null)
        {
            _engine.Draw(_target, _brush, _dc, _pixels.Width, _pixels.Height, font, text, x, top, rgb);
            return;
        }

        Native.SelectObject(_dc, GdiFont(font));
        Native.SetBkMode(_dc, Native.BK_TRANSPARENT);
        Native.SetTextColor(_dc, ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));
        Native.SIZE size;
        Native.GetTextExtentPoint32W(_dc, text, text.Length, &size);
        var left = (int)MathF.Round(x);
        var y = (int)MathF.Round(top);
        Native.TextOutW(_dc, left, y, text, text.Length);

        // GDI clears the alpha byte of what it touches: the text sits on an opaque fill, so it goes back to opaque.
        for (var row = Math.Max(0, y - 2); row < Math.Min(_pixels.Height, y + size.Cy + 2); row++)
        {
            var line = _pixels.Row(row);
            for (var col = Math.Max(0, left - 2); col < Math.Min(_pixels.Width, left + size.Cx + 2); col++)
            {
                line[col] |= 0xFF000000;
            }
        }
    }

    private nint GdiFont(FontSpec font)
    {
        if (!_gdiFonts.TryGetValue(font, out var handle))
        {
            var mono = font.Face == Face.Mono;
            handle = Native.CreateFontW(-(int)MathF.Floor(font.Size + 0.5f), 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, Native.ANTIALIASED_QUALITY,
                mono ? (uint)Native.FIXED_PITCH_MODERN : 0, FontFaces.GdiName(font.Face));
            _gdiFonts[font] = handle;
        }

        return handle;
    }

    public void Dispose()
    {
        TextEngine.Release(_brush);
        TextEngine.Release(_target);
        if (_gdiFonts.Count > 0)
        {
            Native.SelectObject(_dc, Native.GetStockObject(StockSystemFont));
            foreach (var handle in _gdiFonts.Values)
            {
                Native.DeleteObject(handle);
            }
        }
    }
}
