using System.Runtime.Versioning;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Mac.Window;

/// <summary>
/// The text of the window on macOS: the shared <see cref="FreeTypeText"/> with CoreText for the fonts of the system. The
/// fonts of the app are in Contents/Resources/fonts of the bundle (Contents/MacOS holds code only, or the signature
/// breaks), or in <c>fonts</c> next to the program outside a bundle, or where <see cref="FreeTypeText.FontDirectoryVariable"/> says.
/// </summary>
internal static class MacText
{
    [SupportedOSPlatform("macos")]
    public static FreeTypeText Create() => new(FontDirectory(Environment.GetEnvironmentVariable(FreeTypeText.FontDirectoryVariable), AppContext.BaseDirectory), new CoreTextFallback());

    /// <summary>The folder of the fonts for a program in <paramref name="programDirectory"/>.</summary>
    internal static string FontDirectory(string? variable, string programDirectory) =>
        FreeTypeText.FontDirectory(variable, InBundle(programDirectory) ? Resources(programDirectory) : programDirectory);

    /// <summary>Whether the program is the one of a bundle: in Contents/MacOS, with Contents/Resources beside it.</summary>
    internal static bool InBundle(string programDirectory) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(programDirectory)) == "MacOS" && Directory.Exists(Resources(programDirectory));

    private static string Resources(string programDirectory) => Path.GetFullPath(Path.Combine(programDirectory, "..", "Resources"));
}
