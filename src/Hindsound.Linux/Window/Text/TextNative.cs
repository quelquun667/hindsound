using System.Runtime.InteropServices;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// The calls of FreeType (2.11), HarfBuzz (2.7.4) and fontconfig the Linux text makes, none newer than those versions (the
/// ones of Ubuntu 22.04). Types as on 64-bit Linux: FT_Long, FT_ULong and FT_Pos are 8 bytes, FT_Int and FT_UInt 4.
/// </summary>
internal static unsafe partial class TextNative
{
    public const string FreeType = "libfreetype.so.6";
    public const string HarfBuzz = "libharfbuzz.so.0";
    public const string Fontconfig = "libfontconfig.so.1";

    // FT_LOAD_RENDER, FT_LOAD_NO_BITMAP and FT_LOAD_TARGET_LIGHT (FT_RENDER_MODE_LIGHT = 1, shifted by 16).
    public const int LoadRender = 1 << 2;
    public const int LoadNoBitmap = 1 << 3;
    public const int LoadTargetLight = 1 << 16;

    // FT_LOAD_NO_HINTING, FT_LOAD_FORCE_AUTOHINT, FT_LOAD_TARGET_LCD_V (FT_RENDER_MODE_LCD_V = 4, shifted by 16) and
    // FT_RENDER_MODE_NORMAL.
    public const int LoadNoHinting = 1 << 1;
    public const int LoadForceAutohint = 1 << 5;
    public const int LoadTargetLcdV = 4 << 16;
    public const int RenderModeNormal = 0;

    // FT_GLYPH_FORMAT_OUTLINE, the tag 'outl'.
    public const uint GlyphFormatOutline = 0x6F75746C;

    public const long FaceFlagScalable = 1;
    public const byte PixelModeMono = 1;
    public const byte PixelModeGray = 2;

    // Offsets in FT_FaceRec on LP64: num_faces, face_index, face_flags (16), style_flags, num_glyphs, family_name,
    // style_name, num_fixed_sizes (int, padded), available_sizes, num_charmaps (int, padded), charmaps, generic (16 bytes),
    // bbox (32 bytes), units_per_EM (136), seven FT_Short, then glyph (152).
    public const int FaceFlagsOffset = 16;
    public const int FaceGlyphOffset = 152;

    // Offsets in FT_GlyphSlotRec on LP64: library (0), face (8), next, glyph_index (padded), generic (16 bytes), metrics
    // (64 bytes), linearHoriAdvance, linearVertAdvance, advance (16 bytes), format (padded), bitmap (152), bitmap_left (192),
    // bitmap_top (196), outline (200). format sits at 144, after advance. In FT_Bitmap: rows, width, pitch, buffer (16),
    // num_grays (24), pixel_mode (26). In FT_Outline: n_contours, n_points (2), points (8), an array of FT_Vector.
    public const int SlotFaceOffset = 8;
    public const int SlotFormatOffset = 144;
    public const int SlotOutlineOffset = 200;
    public const int OutlinePointCountOffset = 2;
    public const int OutlinePointsOffset = 8;
    public const int SlotBitmapOffset = 152;
    public const int SlotBitmapLeftOffset = 192;
    public const int SlotBitmapTopOffset = 196;
    public const int BitmapRowsOffset = 0;
    public const int BitmapWidthOffset = 4;
    public const int BitmapPitchOffset = 8;
    public const int BitmapBufferOffset = 16;
    public const int BitmapNumGraysOffset = 24;
    public const int BitmapPixelModeOffset = 26;

    // FcResultMatch, FcMatchPattern.
    public const int FcResultMatch = 0;
    public const int FcMatchPattern = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct FtVector
    {
        public long X;
        public long Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphInfo
    {
        public uint Codepoint;
        public uint Mask;
        public uint Cluster;
        private readonly uint _var1;
        private readonly uint _var2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphPosition
    {
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
        private readonly uint _var;
    }

    // FreeType

    [LibraryImport(FreeType)]
    public static partial int FT_Init_FreeType(out nint library);

    [LibraryImport(FreeType)]
    public static partial int FT_Done_FreeType(nint library);

    [LibraryImport(FreeType, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FT_New_Face(nint library, string path, nint faceIndex, out nint face);

    [LibraryImport(FreeType)]
    public static partial int FT_Done_Face(nint face);

    [LibraryImport(FreeType)]
    public static partial int FT_Set_Pixel_Sizes(nint face, uint width, uint height);

    [LibraryImport(FreeType)]
    public static partial void FT_Set_Transform(nint face, nint matrix, FtVector* delta);

    [LibraryImport(FreeType)]
    public static partial int FT_Load_Glyph(nint face, uint glyphIndex, int loadFlags);

    [LibraryImport(FreeType)]
    public static partial int FT_Render_Glyph(nint slot, int renderMode);

    [LibraryImport(FreeType)]
    public static partial uint FT_Get_Char_Index(nint face, nuint charcode);

    // HarfBuzz

    [LibraryImport(HarfBuzz)]
    public static partial byte* hb_version_string();

    [LibraryImport(HarfBuzz, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint hb_blob_create_from_file(string path);

    [LibraryImport(HarfBuzz)]
    public static partial uint hb_blob_get_length(nint blob);

    [LibraryImport(HarfBuzz)]
    public static partial byte* hb_blob_get_data(nint blob, out uint length);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_blob_destroy(nint blob);

    [LibraryImport(HarfBuzz)]
    public static partial nint hb_face_create(nint blob, uint index);

    [LibraryImport(HarfBuzz)]
    public static partial uint hb_face_get_upem(nint face);

    [LibraryImport(HarfBuzz)]
    public static partial nint hb_face_reference_table(nint face, uint tag);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_face_destroy(nint face);

    [LibraryImport(HarfBuzz)]
    public static partial nint hb_font_create(nint face);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_ot_font_set_funcs(nint font);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_font_set_scale(nint font, int xScale, int yScale);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_font_destroy(nint font);

    [LibraryImport(HarfBuzz)]
    public static partial nint hb_buffer_create();

    [LibraryImport(HarfBuzz)]
    public static partial void hb_buffer_clear_contents(nint buffer);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_buffer_add_utf16(nint buffer, ushort* text, int textLength, uint itemOffset, int itemLength);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_buffer_guess_segment_properties(nint buffer);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_shape(nint font, nint buffer, nint features, uint numFeatures);

    [LibraryImport(HarfBuzz)]
    public static partial GlyphInfo* hb_buffer_get_glyph_infos(nint buffer, out uint length);

    [LibraryImport(HarfBuzz)]
    public static partial GlyphPosition* hb_buffer_get_glyph_positions(nint buffer, out uint length);

    [LibraryImport(HarfBuzz)]
    public static partial void hb_buffer_destroy(nint buffer);

    // fontconfig

    [LibraryImport(Fontconfig)]
    public static partial nint FcInitLoadConfigAndFonts();

    [LibraryImport(Fontconfig)]
    public static partial void FcConfigDestroy(nint config);

    [LibraryImport(Fontconfig)]
    public static partial nint FcPatternCreate();

    [LibraryImport(Fontconfig)]
    public static partial void FcPatternDestroy(nint pattern);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternAddCharSet(nint pattern, string obj, nint charset);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternAddBool(nint pattern, string obj, int value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternAddInteger(nint pattern, string obj, int value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetCharSet(nint pattern, string obj, int n, out nint charset);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetString(nint pattern, string obj, int n, out byte* value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetInteger(nint pattern, string obj, int n, out int value);

    [LibraryImport(Fontconfig, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetBool(nint pattern, string obj, int n, out int value);

    [LibraryImport(Fontconfig)]
    public static partial nint FcCharSetCreate();

    [LibraryImport(Fontconfig)]
    public static partial int FcCharSetAddChar(nint charset, uint ucs4);

    [LibraryImport(Fontconfig)]
    public static partial int FcCharSetHasChar(nint charset, uint ucs4);

    [LibraryImport(Fontconfig)]
    public static partial void FcCharSetDestroy(nint charset);

    [LibraryImport(Fontconfig)]
    public static partial int FcConfigSubstitute(nint config, nint pattern, int kind);

    [LibraryImport(Fontconfig)]
    public static partial void FcDefaultSubstitute(nint pattern);

    [LibraryImport(Fontconfig)]
    public static partial nint FcFontSort(nint config, nint pattern, int trim, nint charsets, out int result);

    [LibraryImport(Fontconfig)]
    public static partial void FcFontSetDestroy(nint fontSet);

    /// <summary>An OpenType table tag, as HarfBuzz takes it: the four letters, the first in the high byte.</summary>
    public static uint Tag(string name) => ((uint)name[0] << 24) | ((uint)name[1] << 16) | ((uint)name[2] << 8) | name[3];
}
