using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The texts that tell how to remove the portable copy. The copy keeps its update state in its own folder under
/// %LocalAppData%\Hindsound, which the Setup's uninstaller never deletes: a text that forgets it leaves a file behind.
/// </summary>
public class PortableReadmeTests
{
    [Fact]
    public void The_readme_of_the_zip_keeps_its_lines_at_120_characters_at_most()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "packaging", "windows", "README.txt"));

        Assert.All(lines, line => Assert.True(line.Length <= 120, $"{line.Length} characters: {line}"));
    }

    [Fact]
    public void Remove_it_in_the_readme_of_the_zip_names_the_update_state_of_the_portable_copy()
    {
        var section = Section(
            File.ReadAllLines(Path.Combine(RepositoryRoot(), "packaging", "windows", "README.txt")),
            line => line == "Remove it");

        Assert.Contains(UpdateChannel.WindowsPortable.StateFolderName, section, StringComparison.Ordinal);
        Assert.Contains(@"%LocalAppData%\Hindsound", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_uninstall_paragraph_of_the_portable_zip_in_the_main_readme_names_its_update_state()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "README.md"));
        var start = Array.FindIndex(lines, line => line.StartsWith("**The portable zip", StringComparison.Ordinal));
        Assert.True(start >= 0, "README.md has no paragraph on removing the portable zip");
        var paragraph = string.Join(" ", lines.Skip(start).TakeWhile(line => line.Length > 0));

        Assert.Contains($"`{UpdateChannel.WindowsPortable.StateFolderName}`", paragraph, StringComparison.Ordinal);
        Assert.Contains(@"`%LOCALAPPDATA%\Hindsound`", paragraph, StringComparison.Ordinal);
    }

    /// <summary>The text from the heading that matches to the next heading, underlined with dashes, or the end.</summary>
    private static string Section(string[] lines, Func<string, bool> isHeading)
    {
        var start = Array.FindIndex(lines, line => isHeading(line));
        Assert.True(start >= 0, "README.txt has no such section");
        var body = new List<string>();
        for (var i = start + 2; i < lines.Length; i++)
        {
            if (i + 1 < lines.Length && lines[i + 1].Length > 0 && lines[i + 1].Trim('-').Length == 0)
            {
                break;
            }

            body.Add(lines[i]);
        }

        return string.Join(" ", body);
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("No Hindsound.slnx above " + AppContext.BaseDirectory);
    }
}
