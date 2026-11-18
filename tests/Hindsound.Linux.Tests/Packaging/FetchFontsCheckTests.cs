using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// "fetch-fonts.sh --check" is what the CI runs on fonts a cache gave back and what check-packages.sh runs on the fonts of
/// both packages. It must hold each font to the SHA-256 of the pinned one, not only to the magic number of a TrueType file.
/// </summary>
public sealed class FetchFontsCheckTests
{
    private static readonly string Root = RepositoryRoot();

    private static readonly string[] Fonts =
    [
        "Selawik-Regular.ttf", "Selawik-Semibold.ttf", "BarlowSemiCondensed-SemiBold.ttf", "CascadiaMono-Regular.ttf",
    ];

    [Fact]
    public void The_script_pins_the_SHA_256_of_each_of_its_four_fonts()
    {
        var script = Script();
        var fonts = Regex.Match(script, @"(?m)^readonly FONTS='([^']+)'\r?$").Groups[1].Value.Split(' ');
        Assert.Equal(Fonts, fonts);

        foreach (var font in Fonts)
        {
            Assert.Matches(@"(?m)^\s+" + Regex.Escape(font) + @"\) echo [0-9a-f]{64} ;;\r?$", script);
        }
    }

    [Fact]
    public void The_check_compares_each_font_with_its_pin()
    {
        var script = Script().Replace("\r\n", "\n");
        var start = script.IndexOf("check_fonts() {", StringComparison.Ordinal);
        var body = script[start..script.IndexOf("\n}\n", start, StringComparison.Ordinal)];

        Assert.Contains("actual=\"$(sha256_of \"$dir/$font\")\"", body, StringComparison.Ordinal);
        Assert.Contains("if [ \"$actual\" != \"$(pinned_sha256 \"$font\")\" ]; then", body, StringComparison.Ordinal);
        Assert.Contains("exit 1", body[body.IndexOf("pinned_sha256", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    /// <summary>Four files with the magic number of a TrueType font that are not the fonts: the check must refuse them.</summary>
    [Fact]
    [Trait("Category", "Linux")]
    public void The_check_refuses_valid_looking_TrueType_files_that_are_not_the_pinned_fonts()
    {
        var folder = Directory.CreateTempSubdirectory("fonts-check-");
        try
        {
            foreach (var font in Fonts)
            {
                File.WriteAllBytes(Path.Combine(folder.FullName, font), [0x00, 0x01, 0x00, 0x00, 0x4e, 0x6f, 0x74, 0x20, 0x69, 0x74]);
            }

            var start = new ProcessStartInfo("bash")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add(Path.Combine(Root, "tools", "linux", "fetch-fonts.sh"));
            start.ArgumentList.Add("--check");
            start.ArgumentList.Add(folder.FullName);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), "fetch-fonts.sh --check did not end");

            Assert.Equal(1, process.ExitCode);
            Assert.Contains("expected", error, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private static string Script() => File.ReadAllText(Path.Combine(Root, "tools", "linux", "fetch-fonts.sh"));

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }
}
