using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The Setup and the portable zip take the texts at the top of licenses/, so that top keeps exactly the texts of the
/// Windows build of 0.5.0: an installed copy gets the same licence files as before. A text that only the Linux packages
/// need (a row of the notices whose "In" cell names Linux and not Windows) lives in licenses/linux/, which both Linux
/// packages carry and no Windows copy does.
/// </summary>
public sealed partial class LicenceFolderSplitTests
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>The top of licenses/ in the tag v0.5.0 (git ls-tree --name-only v0.5.0 licenses/), in ordinal order.</summary>
    private static readonly string[] WindowsTexts =
    [
        "InnoSetup-license.txt",
        "LAME-COPYING.txt",
        "LAME-LICENSE.txt",
        "LAME-README.txt",
        "NAudio-LICENSE.txt",
        "NAudio.Lame-LICENSE.txt",
        "System.Numerics.Tensors-LICENSE.txt",
        "System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt",
        "dotnet-runtime-LICENSE.txt",
        "dotnet-runtime-THIRD-PARTY-NOTICES.txt",
    ];

    [Fact]
    public void The_top_of_the_licences_folder_holds_exactly_the_texts_of_0_5_0()
    {
        var licences = Path.Combine(Root, "licenses");
        var files = Directory.GetFiles(licences, "*.txt").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToList();
        var folders = Directory.GetDirectories(licences).Select(path => Path.GetFileName(path)!).ToList();

        Assert.Equal(WindowsTexts, files);
        Assert.Equal(["linux"], folders);
    }

    [Fact]
    public void Every_text_of_a_Linux_only_row_is_in_the_linux_folder()
    {
        var rows = LinuxOnlyRows();
        var linked = rows.SelectMany(row => LicenceLink().Matches(row[5]).Select(match => (Component: row[1], Link: match.Groups[1].Value))).ToList();

        Assert.Contains(rows, row => row[1] == "Tmds.DBus.Protocol");
        Assert.Contains(linked, text => text.Component == "Tmds.DBus.Protocol");
        foreach (var (component, link) in linked)
        {
            Assert.True(link.StartsWith("licenses/linux/", StringComparison.Ordinal), component + " links " + link + ", outside licenses/linux/");
            Assert.True(File.Exists(Path.Combine(Root, link)), link + " does not exist");
        }
    }

    /// <summary>The rows of the notices whose "In" cell names Linux and not Windows, as cells.</summary>
    private static List<string[]> LinuxOnlyRows() =>
        File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"))
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(row => row.Length > 7
                && row[7].StartsWith("Linux", StringComparison.Ordinal)
                && !row[7].Contains("Windows", StringComparison.Ordinal))
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
