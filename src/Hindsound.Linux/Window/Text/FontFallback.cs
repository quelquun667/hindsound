using System.Runtime.InteropServices;
using static Hindsound.Linux.Window.Text.TextNative;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// Asks fontconfig for a font of the system that has a character the fonts of the app lack (a clip named in Japanese).
/// fontconfig is read only on the first such character: a page in Latin letters never scans the fonts of the system.
/// </summary>
internal sealed unsafe class FontFallback : IDisposable
{
    // How many fonts of fontconfig's sorted list are looked at before giving up on a character.
    private const int Candidates = 64;
    private nint _config;
    private bool _started;

    /// <summary>The file and the face index of a font with outlines that has <paramref name="codePoint"/>, or null.</summary>
    /// <param name="weight">fontconfig's weight: 80 regular, 180 semibold.</param>
    public (string Path, int Index)? Find(int codePoint, int weight)
    {
        if (!_started)
        {
            _started = true;
            _config = FcInitLoadConfigAndFonts();
        }

        if (_config == 0)
        {
            return null;
        }

        var pattern = FcPatternCreate();
        var wanted = FcCharSetCreate();
        try
        {
            FcCharSetAddChar(wanted, (uint)codePoint);
            FcPatternAddCharSet(pattern, "charset", wanted);
            FcPatternAddBool(pattern, "scalable", 1);
            FcPatternAddInteger(pattern, "weight", weight);
            FcConfigSubstitute(_config, pattern, FcMatchPattern);
            FcDefaultSubstitute(pattern);
            var set = FcFontSort(_config, pattern, 1, 0, out _);
            if (set == 0)
            {
                return null;
            }

            try
            {
                // FcFontSet: int nfont; int sfont; FcPattern **fonts.
                var count = Math.Min(*(int*)set, Candidates);
                var fonts = *(nint**)(set + 8);
                for (var i = 0; i < count; i++)
                {
                    var font = fonts[i];
                    if (FcPatternGetCharSet(font, "charset", 0, out var has) != FcResultMatch || FcCharSetHasChar(has, (uint)codePoint) == 0)
                    {
                        continue;
                    }

                    if (FcPatternGetBool(font, "scalable", 0, out var scalable) == FcResultMatch && scalable == 0)
                    {
                        continue;
                    }

                    if (FcPatternGetString(font, "file", 0, out var file) != FcResultMatch || file == null)
                    {
                        continue;
                    }

                    var index = FcPatternGetInteger(font, "index", 0, out var i2) == FcResultMatch ? i2 : 0;
                    return (Marshal.PtrToStringUTF8((nint)file)!, index);
                }

                return null;
            }
            finally
            {
                FcFontSetDestroy(set);
            }
        }
        finally
        {
            FcPatternDestroy(pattern);
            FcCharSetDestroy(wanted);
        }
    }

    public void Dispose()
    {
        if (_config != 0)
        {
            FcConfigDestroy(_config);
            _config = 0;
        }
    }
}
