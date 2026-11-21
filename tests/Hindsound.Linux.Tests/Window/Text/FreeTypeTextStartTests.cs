using Hindsound.Linux.Window.Text;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>Where the text finds its fonts, and what it says when it cannot start: on any system.</summary>
public sealed class FreeTypeTextStartTests : IDisposable
{
    private static readonly string[] FontFiles =
        ["Selawik-Regular.ttf", "Selawik-Semibold.ttf", "BarlowSemiCondensed-SemiBold.ttf", "CascadiaMono-Regular.ttf"];

    // A folder of this test's own: a shared temporary folder may hold anything.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hindsound-text-" + Guid.NewGuid().ToString("N"));

    public FreeTypeTextStartTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_fonts_are_in_the_folder_the_variable_names_or_else_next_to_the_app()
    {
        var app = Path.Combine("opt", "hindsound");

        Assert.Equal(Path.Combine("ci", "fonts"), FreeTypeText.FontDirectory(Path.Combine("ci", "fonts"), app));
        Assert.Equal(Path.Combine(app, "fonts"), FreeTypeText.FontDirectory(null, app));
        Assert.Equal(Path.Combine(app, "fonts"), FreeTypeText.FontDirectory(string.Empty, app));
    }

    [Fact]
    public void A_missing_font_is_named_without_its_path()
    {
        File.WriteAllBytes(Path.Combine(_folder, "Selawik-Regular.ttf"), []);

        var error = Assert.Throws<TextUnavailableException>(() => LinuxText.Create(_folder));

        Assert.Contains("BarlowSemiCondensed-SemiBold.ttf", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_folder, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fonts_that_are_there_but_unreadable_or_a_missing_library_are_named_without_a_path()
    {
        foreach (var file in FontFiles)
        {
            File.WriteAllBytes(Path.Combine(_folder, file), [1, 2, 3]);
        }

        var error = Assert.Throws<TextUnavailableException>(() => LinuxText.Create(_folder));

        // Windows has no FreeType: the library is named. Linux has it, and the first font it opens is the headings'.
        Assert.Contains(OperatingSystem.IsWindows() ? "libfreetype.so.6" : "BarlowSemiCondensed-SemiBold.ttf", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_folder, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
