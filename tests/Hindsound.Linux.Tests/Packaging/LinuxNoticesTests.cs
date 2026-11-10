using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The licence texts the Linux packages ship: build-packages.sh copies licenses/ and THIRD-PARTY-NOTICES.md into both of
/// them, so every package the Linux app references needs its row in the notices and its text in licenses/.
/// </summary>
public sealed partial class LinuxNoticesTests
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>The git blob of COPYING in tmds/Tmds.DBus at commit 5557e043025de2251128ab8048c9d3f0cf50495e.</summary>
    private const string TmdsCopyingBlob = "d1060c2f634ad623ba500dff09ac3044c9747c82";

    public static TheoryData<string> LinuxPackages()
    {
        var project = XDocument.Load(Path.Combine(Root, "src", "Hindsound.Linux", "Hindsound.Linux.csproj"));
        var data = new TheoryData<string>();
        foreach (var reference in project.Descendants("PackageReference"))
        {
            data.Add((string)reference.Attribute("Include")!);
        }

        return data;
    }

    [Fact]
    public void The_Linux_app_references_at_least_one_package() => Assert.NotEmpty(LinuxPackages());

    [Theory]
    [MemberData(nameof(LinuxPackages))]
    public void Each_package_of_the_Linux_app_has_its_licence_text_in_the_repository(string package)
    {
        var texts = LicenceTexts(package);

        Assert.NotEmpty(texts);
        foreach (var text in texts)
        {
            Assert.True(File.Exists(Path.Combine(Root, text)), text + " is named by the notices but is not in the repository");
        }
    }

    [Theory]
    [MemberData(nameof(LinuxPackages))]
    public void The_package_check_looks_for_the_licence_text_of_each_package_of_the_Linux_app(string package)
    {
        var check = File.ReadAllText(Path.Combine(Root, "packaging", "linux", "check-packages.sh"));
        foreach (var text in LicenceTexts(package))
        {
            Assert.Contains($"has \"$doc/{text}\"", check, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_Tmds_DBus_licence_is_the_upstream_COPYING_unchanged()
    {
        // A Windows clone may have turned its line ends into CRLF; git hashes the text with LF.
        var read = File.ReadAllBytes(Path.Combine(Root, "licenses", "Tmds.DBus.Protocol-COPYING.txt"));
        var bytes = read.Where((value, index) => !(value == '\r' && index + 1 < read.Length && read[index + 1] == '\n')).ToArray();

        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        var blob = Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes]));

        Assert.Equal(TmdsCopyingBlob, blob);
    }

    [Fact]
    public void The_Tmds_DBus_row_gives_the_copyright_lines_of_COPYING()
    {
        var row = Row("Tmds.DBus.Protocol");
        Assert.Equal("Copyright 2006 Alp Toker, Copyright 2010 Other Contributors, Copyright 2016 Tom Deseyn", row[4]);
        Assert.Contains("`COPYING`", row[6], StringComparison.Ordinal);
        Assert.Contains("5557e043025de2251128ab8048c9d3f0cf50495e", row[6], StringComparison.Ordinal);
    }

    [Fact]
    public void The_notices_do_not_say_the_table_is_about_the_Windows_build_alone()
    {
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        Assert.DoesNotContain("The Windows build of Hindsound contains or ships the components below", notices, StringComparison.Ordinal);
        Assert.Contains("says whether a component is in the Windows build, in the Linux packages", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_of_the_notices_says_in_which_build_the_component_is()
    {
        var rows = File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"))
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| Component ", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .ToList();

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Equal(9, row.Length);
            Assert.False(string.IsNullOrWhiteSpace(row[7]), row[1] + " has no \"In\" cell");
        }
    }

    /// <summary>The licences/ files the Text cell of the package's row links to.</summary>
    private static List<string> LicenceTexts(string package) =>
        LicenceLink().Matches(Row(package)[5]).Select(match => match.Groups[1].Value).ToList();

    /// <summary>The cells of the row of a component, the empty cells before the first bar and after the last one included.</summary>
    private static string[] Row(string component)
    {
        var rows = File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"))
            .Where(line => line.StartsWith("| " + component + " ", StringComparison.Ordinal))
            .ToList();
        Assert.True(rows.Count == 1, $"{rows.Count} rows of the notices name {component}");
        return rows[0].Split('|').Select(cell => cell.Trim()).ToArray();
    }

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
