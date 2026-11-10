using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The Setup and the portable zip take licenses\*.txt, the top of the folder only. The texts of the AppImage runtime and
/// of the libraries built into it belong to the Linux packages alone, so they live in licenses/linux/: a Windows copy
/// keeps the licence files it had, and both Linux packages still carry them, at the path the notices link.
/// </summary>
public sealed partial class LinuxOnlyLicenceFolderTests
{
    private static readonly string Root = RepositoryRoot();

    private const string InAppImage = "Linux (AppImage only)";

    [Fact]
    public void Every_text_of_an_AppImage_only_row_is_in_the_linux_folder()
    {
        var linked = AppImageOnlyRows().SelectMany(row => LicenceLink().Matches(row[5]).Select(match => match.Groups[1].Value)).ToList();

        Assert.NotEmpty(linked);
        foreach (var link in linked)
        {
            Assert.StartsWith("licenses/linux/", link, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(Root, link)), link + " does not exist");
        }
    }

    [Fact]
    public void The_top_of_the_licences_folder_holds_no_text_of_an_AppImage_only_row()
    {
        var appImageOnly = AppImageOnlyRows()
            .SelectMany(row => LicenceLink().Matches(row[5]).Select(match => Path.GetFileName(match.Groups[1].Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var top = Directory.GetFiles(Path.Combine(Root, "licenses"), "*.txt").Select(Path.GetFileName).ToList();

        Assert.NotEmpty(top);
        Assert.DoesNotContain(top, name => appImageOnly.Contains(name!));
    }

    [Fact]
    public void The_Setup_takes_the_top_of_the_licences_folder_only()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "installer", "Hindsound.iss"))
            .Where(line => line.Contains(@"\licenses\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var line = Assert.Single(lines);
        Assert.Contains(@"{#RepoRoot}\licenses\*.txt""", line, StringComparison.Ordinal);
        Assert.DoesNotContain("recursesubdirs", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Both_Linux_packages_get_the_linux_folder_at_the_path_the_notices_link()
    {
        var build = File.ReadAllText(Path.Combine(Root, "packaging", "linux", "build-packages.sh"));
        var check = File.ReadAllText(Path.Combine(Root, "packaging", "linux", "check-packages.sh"));

        Assert.Contains(@"install -m 644 -- ""$repo""/licenses/*.txt ""$doc/licenses/""", build, StringComparison.Ordinal);
        Assert.Contains(@"install -m 644 -- ""$repo""/licenses/linux/*.txt ""$doc/licenses/linux/""", build, StringComparison.Ordinal);
        Assert.Contains(@"""$doc/licenses/linux""", build, StringComparison.Ordinal);
        Assert.Contains(@"has ""$doc/licenses/linux/$licence""", check, StringComparison.Ordinal);
    }

    private static List<string[]> AppImageOnlyRows() =>
        File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"))
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(row => row.Length > 7 && row[7] == InAppImage)
            .ToList();

    [GeneratedRegex(@"\]\((licenses/[^)]+)\)")]
    private static partial Regex LicenceLink();

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
