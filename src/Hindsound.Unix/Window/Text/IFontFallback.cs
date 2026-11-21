namespace Hindsound.Unix.Window.Text;

/// <summary>
/// Finds a font of the system for a character the fonts of the app lack (a clip named in Japanese). Each system has its
/// own: fontconfig on Linux, CoreText on macOS. Used on the loop thread of the window only, by <see cref="FreeTypeText"/>,
/// which disposes it.
/// </summary>
internal interface IFontFallback : IDisposable
{
    /// <summary>Throws <see cref="TextUnavailableException"/> when the library the fallback needs cannot be loaded.</summary>
    void CheckLoadable();

    /// <summary>The file and the face index of a font with outlines that has <paramref name="codePoint"/>, or null. The file
    /// is the system's bytes ended by a zero, kept as they are: a name that is not valid UTF-8 still opens.</summary>
    /// <param name="weight">fontconfig's weight: 80 regular, 180 semibold.</param>
    (byte[] Path, int Index)? Find(int codePoint, int weight);
}
