using System.Text.RegularExpressions;

namespace Hindsound.Tests.Ui;

/// <summary>
/// Rules of the translations that no behaviour test can see, as the sources show them: every entry point lets the setting
/// change the language of the process, and no text of <c>Tr.Of</c> is an interpolated string, which formats with the culture of
/// the PC on Windows. Read from the sources, so that they run on every system.
/// </summary>
public sealed partial class LanguageSourceTests
{
    private static string Root()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex(@"static\s+int\s+Main\s*\(string\[\]\s*\w+\)\s*\{\s*AppStartup\.Begin\(\);")]
    private static partial Regex MainBeginsWithTheStartup();

    [Theory]
    [InlineData("src/Hindsound/App/Program.cs")]
    [InlineData("src/Hindsound.Linux/Program.cs")]
    [InlineData("src/Hindsound.Mac/Program.cs")]
    public void Each_entry_point_begins_with_the_startup_that_lets_the_language_change_the_process(string program)
    {
        var text = File.ReadAllText(Path.Combine(Root(), program));
        Assert.True(MainBeginsWithTheStartup().IsMatch(text), $"{program}: Main must begin with AppStartup.Begin(); without it the language setting is read and the interface stays English");
    }

    [Fact]
    public void The_startup_makes_the_language_a_setting_of_the_whole_process_and_nothing_else_does_it()
    {
        var root = Root();
        Assert.Matches(@"Begin\(\)\s*=>\s*Tr\.ForProcess\(\);", File.ReadAllText(Path.Combine(root, "src", "Hindsound.Core", "App", "AppStartup.cs")));

        var others = Sources()
            .Where(file => Path.GetFileName(file) is not ("AppStartup.cs" or "Tr.cs") && File.ReadAllText(file).Contains("Tr.ForProcess(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file));
        Assert.Empty(others);
    }

    /// <summary>The lines where an argument of <c>Tr.Of(</c> is an interpolated string, 1-based; comments are not looked at.</summary>
    internal static List<int> InterpolatedTexts(string source)
    {
        var clean = CommentLines().Replace(source, match => new string(' ', match.Length));
        var lines = new List<int>();
        foreach (Match call in TrOf().Matches(clean))
        {
            var depth = 1;
            for (var i = call.Index + call.Length; i < clean.Length && depth > 0; i++)
            {
                switch (clean[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        break;
                    case '$' or '@' when IsInterpolatedStart(clean, i):
                        lines.Add(clean.Take(i).Count(c => c == '\n') + 1);
                        depth = 0;
                        break;
                    case '"':
                        i = SkipString(clean, i);
                        break;
                }
            }
        }

        return lines;
    }

    private static bool IsInterpolatedStart(string text, int i)
    {
        var j = i;
        var dollar = false;
        while (j < text.Length && text[j] is '$' or '@')
        {
            dollar |= text[j] == '$';
            j++;
        }

        return dollar && j < text.Length && text[j] == '"';
    }

    /// <summary>The index of the quote that ends the plain or verbatim string whose opening quote is at <paramref name="quote"/>.</summary>
    private static int SkipString(string text, int quote)
    {
        var verbatim = quote > 0 && text[quote - 1] == '@';
        for (var i = quote + 1; i < text.Length; i++)
        {
            if (text[i] == '"' && verbatim && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
            }
            else if (text[i] == '"')
            {
                return i;
            }
            else if (text[i] == '\\' && !verbatim)
            {
                i++;
            }
        }

        return text.Length;
    }

    [GeneratedRegex(@"\bTr\.Of\(")]
    private static partial Regex TrOf();

    [GeneratedRegex(@"^[ \t]*//.*$", RegexOptions.Multiline)]
    private static partial Regex CommentLines();

    [Fact]
    public void No_text_of_Tr_Of_is_an_interpolated_string()
    {
        var offences = new List<string>();
        foreach (var file in Sources())
        {
            offences.AddRange(InterpolatedTexts(File.ReadAllText(file)).Select(line => $"{Path.GetRelativePath(Root(), file)}:{line}"));
        }

        Assert.True(offences.Count == 0, "Tr.Of takes plain strings: an interpolated one formats with the culture of the PC on Windows (1,5 s in English on a French PC). Use Tr.Format, Tr.Number, Tr.Size or Tr.Plural:\n" + string.Join('\n', offences));
    }

    [Theory]
    [InlineData("var a = Tr.Of(\"Play\", \"Lire\");", 0)]
    [InlineData("var a = Tr.Of(\"a {x}\", \"b $\" + c);", 0)]
    [InlineData("var a = Tr.Of(\"say \\\"hi\\\"\", \"b\");", 0)]
    [InlineData("var a = Tr.Of($\"{x:0.0} s\", \"b\");", 1)]
    [InlineData("var a = Tr.Of(\"a\",\n    $\"{x} s\");", 2)]
    [InlineData("var a = Tr.Of(\"a\", @$\"{x}\");", 1)]
    [InlineData("var a = Tr.Of(\"a\", Other($\"{x}\"));", 1)]
    [InlineData("// Tr.Of($\"{x}\", \"b\")", 0)]
    [InlineData("var a = Tr.Format($\"a {x}\", $\"b {x}\");", 0)]
    public void The_scan_finds_an_interpolated_text_and_only_that(string source, int expectedLine)
    {
        var found = InterpolatedTexts(source);
        Assert.Equal(expectedLine == 0 ? [] : [expectedLine], found);
    }
}
