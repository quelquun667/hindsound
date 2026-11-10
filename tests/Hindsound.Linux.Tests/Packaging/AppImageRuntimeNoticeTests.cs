using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The AppImage starts with the type2 runtime that build-packages.sh downloads and hands to appimagetool. That runtime is
/// third-party code shipped to users, but it is not in the publish, so the notices must name it on their own: the very
/// release and file the script pins, in the AppImage alone.
/// </summary>
public sealed partial class AppImageRuntimeNoticeTests
{
    private static readonly string Root = RepositoryRoot();

    private const string Component = "AppImage type2 runtime";

    [Fact]
    public void The_notices_have_a_row_for_the_AppImage_runtime()
    {
        var rows = NoticeLines().Where(line => line.StartsWith("| " + Component + " ", StringComparison.Ordinal)).ToList();

        Assert.Single(rows);
    }

    [Fact]
    public void The_runtime_row_names_the_release_the_build_pins()
    {
        var url = Pin(RuntimeUrl());
        var release = ReleaseOf().Match(url);
        Assert.True(release.Success, url + " is not a release download of AppImage/type2-runtime");

        var row = Row();
        Assert.Equal(release.Groups[1].Value, row[2]);
        Assert.Contains("`" + url + "`", row[6], StringComparison.Ordinal);
    }

    [Fact]
    public void The_notices_give_the_sha256_the_build_checks_the_runtime_against()
    {
        var sha = Pin(RuntimeSha());

        Assert.Contains("`" + sha + "`", Row()[6], StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_is_in_the_AppImage_only()
    {
        Assert.Equal("Linux (AppImage only)", Row()[7]);
    }

    [Fact]
    public void The_build_puts_the_pinned_runtime_at_the_head_of_the_AppImage()
    {
        var script = string.Join('\n', BuildScript());

        Assert.Contains("fetch \"$RUNTIME_URL\" \"$RUNTIME_SHA256\" \"$work/runtime-x86_64\"", script, StringComparison.Ordinal);
        Assert.Contains("--runtime-file \"$work/runtime-x86_64\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_counts_what_the_packaging_adds_to_the_publish()
    {
        var notices = string.Join('\n', NoticeLines());

        Assert.Contains("and from what build-packages.sh adds to it", notices, StringComparison.Ordinal);
    }

    /// <summary>The cells of the runtime's row, the empty cells before the first bar and after the last one included.</summary>
    private static string[] Row()
    {
        var row = NoticeLines().Single(line => line.StartsWith("| " + Component + " ", StringComparison.Ordinal));
        return row.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static string Pin(Regex pattern)
    {
        var found = BuildScript().Select(line => pattern.Match(line)).Where(match => match.Success).ToList();
        Assert.Single(found);
        return found[0].Groups[1].Value;
    }

    private static List<string> BuildScript() =>
        File.ReadAllLines(Path.Combine(Root, "packaging", "linux", "build-packages.sh")).Select(line => line.TrimEnd('\r')).ToList();

    private static List<string> NoticeLines() =>
        File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")).Select(line => line.TrimEnd('\r')).ToList();

    [GeneratedRegex(@"^readonly RUNTIME_URL='([^']+)'$")]
    private static partial Regex RuntimeUrl();

    [GeneratedRegex(@"^readonly RUNTIME_SHA256='([0-9a-f]{64})'$")]
    private static partial Regex RuntimeSha();

    [GeneratedRegex(@"^https://github\.com/AppImage/type2-runtime/releases/download/([^/]+)/runtime-x86_64$")]
    private static partial Regex ReleaseOf();

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
