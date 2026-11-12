using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// THIRD-PARTY-NOTICES.md is also read as plain text, in usr/share/doc/hindsound of the AppImage and at the top of the
/// tar.gz, where only the text of a link shows. A link whose text is a path into licenses/ must therefore show the path
/// it leads to.
/// </summary>
public sealed partial class NoticesPathLinkTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void The_notices_hold_links_whose_text_is_a_path_into_licenses() =>
        Assert.NotEmpty(PathLink().Matches(Notices()));

    [Fact]
    public void Each_link_whose_text_is_a_path_into_licenses_shows_the_path_it_leads_to()
    {
        foreach (Match link in PathLink().Matches(Notices()))
        {
            // [licenses/](licenses) names the folder itself, with or without its slash.
            var shown = link.Groups["text"].Value;
            var target = link.Groups["target"].Value;
            Assert.True(shown.TrimEnd('/') == target.TrimEnd('/'),$"the link [{shown}]({target}) shows a path other than its target");
            Assert.True(Path.Exists(Path.Combine(Root, shown)), shown + " is shown by the notices but is not in the repository");
        }
    }

    private static string Notices() => File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));

    [GeneratedRegex(@"\[(?<text>licenses/[^\]]*)\]\((?<target>[^)]*)\)")]
    private static partial Regex PathLink();

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
