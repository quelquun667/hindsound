using Hindsound.Unix.Window.Text;
using static Hindsound.Linux.Window.Text.TextNative;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// One font file, opened twice over the same file: by FreeType, which draws its glyphs, and by HarfBuzz, which places them.
/// HarfBuzz works at the font's own units (scale = units per em), so its advances are the design ones, unhinted, as
/// DirectWrite measures; the caller scales them to the em.
/// </summary>
internal sealed unsafe class FontFile : IDisposable
{
    private int _em = -1;
    private bool _disposed;

    private FontFile(int id, string name, nint ftFace, nint hbBlob, nint hbFace, nint hbFont, VerticalMetrics metrics)
    {
        Id = id;
        Name = name;
        FtFace = ftFace;
        HbBlob = hbBlob;
        HbFace = hbFace;
        HbFont = hbFont;
        Metrics = metrics;
    }

    /// <summary>A number of its own for the whole life of the text, so the glyphs of a closed fallback never match a new one.</summary>
    public int Id { get; }

    /// <summary>The name of the file, for the messages of the tests.</summary>
    public string Name { get; }

    public nint FtFace { get; }

    public nint HbBlob { get; }

    public nint HbFace { get; }

    public nint HbFont { get; }

    public VerticalMetrics Metrics { get; }

    /// <summary>The font at <paramref name="path"/> (face <paramref name="index"/> of a collection), or null if FreeType or
    /// HarfBuzz cannot read it or it has no outlines.</summary>
    public static FontFile? Open(nint library, int id, string path, int index) => Open(library, id, PathBytes(path), index);

    /// <summary>The font at <paramref name="path"/>, the file's name as the system's bytes ended by a zero, as fontconfig
    /// gives it: a name that is not valid UTF-8 is never turned into a string and back.</summary>
    public static FontFile? Open(nint library, int id, byte[] path, int index)
    {
        fixed (byte* file = path)
        {
            return Open(library, id, file, index);
        }
    }

    /// <summary>A path as the bytes <see cref="Open(nint, int, byte[], int)"/> takes: UTF-8, ended by a zero.</summary>
    public static byte[] PathBytes(string path)
    {
        var bytes = new byte[System.Text.Encoding.UTF8.GetByteCount(path) + 1];
        System.Text.Encoding.UTF8.GetBytes(path, bytes);
        return bytes;
    }

    private static FontFile? Open(nint library, int id, byte* path, int index)
    {
        if (FT_New_Face(library, path, index, out var ftFace) != 0 || ftFace == 0)
        {
            return null;
        }

        if ((*(long*)(ftFace + FaceFlagsOffset) & FaceFlagScalable) == 0)
        {
            FT_Done_Face(ftFace);
            return null;
        }

        var blob = hb_blob_create_from_file(path);
        if (hb_blob_get_length(blob) == 0)
        {
            hb_blob_destroy(blob);
            FT_Done_Face(ftFace);
            return null;
        }

        var hbFace = hb_face_create(blob, (uint)index);
        var metrics = VerticalMetrics.Read(Table(hbFace, "head"), Table(hbFace, "hhea"), Table(hbFace, "OS/2"), Table(hbFace, "VDMX"));
        var hbFont = hb_font_create(hbFace);
        hb_ot_font_set_funcs(hbFont);
        hb_font_set_scale(hbFont, metrics.UnitsPerEm, metrics.UnitsPerEm);
        var name = System.IO.Path.GetFileName(System.Text.Encoding.UTF8.GetString(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated(path)));
        return new FontFile(id, name, ftFace, blob, hbFace, hbFont, metrics);
    }

    /// <summary>Whether the font has a glyph of its own for this code point.</summary>
    public bool Covers(int codePoint) => FT_Get_Char_Index(FtFace, (nuint)codePoint) != 0;

    /// <summary>Whether the font has this table, for the messages of the tests.</summary>
    public bool HasTable(string tag) => Table(HbFace, tag).Length > 0;

    /// <summary>Sets FreeType's size to this em, once per change.</summary>
    public void SetEm(int em)
    {
        if (_em != em)
        {
            FT_Set_Pixel_Sizes(FtFace, 0, (uint)em);
            _em = em;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        hb_font_destroy(HbFont);
        hb_face_destroy(HbFace);
        hb_blob_destroy(HbBlob);
        FT_Done_Face(FtFace);
    }

    // A copy of the table: HarfBuzz's blob is let go at once.
    private static byte[] Table(nint face, string tag)
    {
        var blob = hb_face_reference_table(face, Tag(tag));
        try
        {
            var data = hb_blob_get_data(blob, out var length);
            return data == null || length == 0 ? [] : new ReadOnlySpan<byte>(data, (int)length).ToArray();
        }
        finally
        {
            hb_blob_destroy(blob);
        }
    }
}
