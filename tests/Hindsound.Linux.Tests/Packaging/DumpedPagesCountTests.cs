using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The checks of a release (the Mac zip, the deb) start the app with --dump-settings and compare the pictures it wrote with a
/// number written in the script. A scene added to the dump changes that number; this fails here, with the right number, rather
/// than in a Mac or deb job at the end of the CI.
/// </summary>
public sealed unsafe class DumpedPagesCountTests
{
    private static readonly string[] Scripts =
    [
        Path.Combine("packaging", "mac", "check-zip.sh"),
        Path.Combine("packaging", "linux", "try-deb.sh"),
    ];

    private sealed class NoText : ICanvasText
    {
        public float Measure(FontSpec font, string text) => text.Length * 0.6f * MathF.Floor(font.Size + 0.5f);

        public float LineHeight(FontSpec font) => MathF.Round(1.25f * MathF.Floor(font.Size + 0.5f));

        public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
        {
        }

        public void Dispose()
        {
        }
    }

    // One picture per scene name, so the count of pictures is the count of distinct names.
    private static int ScenesDrawn()
    {
        var names = new List<string>();
        var memory = new List<nint>();
        try
        {
            SettingsDump.Render(1, (width, height, scale) =>
            {
                var bits = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
                memory.Add((nint)bits);
                return new PixelCanvas(bits, width, height, scale, new NoText());
            }, (name, canvas) => names.Add(name));
        }
        finally
        {
            foreach (var bits in memory)
            {
                NativeMemory.Free((void*)bits);
            }
        }

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        return names.Count;
    }

    public static TheoryData<string> ScriptPaths()
    {
        var data = new TheoryData<string>();
        foreach (var script in Scripts)
        {
            data.Add(script);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ScriptPaths))]
    public void A_release_script_expects_as_many_pages_as_the_dump_draws(string script)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), script));
        var matches = Regex.Matches(text, @"^readonly PAGES=(\d+)\s*$", RegexOptions.Multiline);
        Assert.True(matches.Count == 1, script + ": expected one line 'readonly PAGES=<n>', found " + matches.Count);
        Assert.Equal(ScenesDrawn(), int.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_release_notes_name_the_number_of_pages_the_dump_draws()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "release", "README.md"));
        var matches = Regex.Matches(text, @"\((\d+) pages\)");
        Assert.True(matches.Count == 1, "tools/release/README.md: expected one '(<n> pages)', found " + matches.Count);
        Assert.Equal(ScenesDrawn(), int.Parse(matches[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

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
