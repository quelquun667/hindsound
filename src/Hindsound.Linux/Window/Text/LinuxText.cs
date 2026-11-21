using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.Window.Text;

/// <summary>The text of the window on Linux: the shared <see cref="FreeTypeText"/>, with fontconfig for the fonts of the system.</summary>
internal static class LinuxText
{
    /// <summary>The fonts next to the program, or in the folder <see cref="FreeTypeText.FontDirectoryVariable"/> names.</summary>
    public static FreeTypeText Create() => new(new FontFallback());

    /// <summary>The fonts of <paramref name="fontDirectory"/>.</summary>
    public static FreeTypeText Create(string fontDirectory) => new(fontDirectory, new FontFallback());
}
