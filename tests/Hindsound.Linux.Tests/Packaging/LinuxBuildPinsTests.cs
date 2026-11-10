using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// What the Linux build downloads is checked before it runs or ships: appimagetool and the AppImage runtime against the
/// sha256 of build-packages.sh, and the LAME archive against the same SHA-256 as the Windows build. The CI only fails when
/// a hash differs while the check is still there, so these tests keep the check itself, and the pins in line.
/// </summary>
public sealed partial class LinuxBuildPinsTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void Fetch_stops_the_build_when_the_download_does_not_have_its_sha256()
    {
        var body = Function(Script("packaging", "linux", "build-packages.sh"), "fetch");

        Assert.Equal("local url=$1 sha=$2 dest=$3", body[0]);
        Assert.StartsWith("curl --fail ", body[1], StringComparison.Ordinal);
        Assert.Contains("--output \"$dest\"", body[1], StringComparison.Ordinal);
        Assert.Matches(CheckThenFail(), body[2]);
        Assert.Equal(3, body.Count);
    }

    [Fact]
    public void Each_download_of_the_build_goes_through_fetch_with_its_own_pin()
    {
        var lines = Script("packaging", "linux", "build-packages.sh");
        var fetches = lines.Where(line => line.StartsWith("fetch ", StringComparison.Ordinal)).ToList();

        Assert.Equal(
            new[]
            {
                "fetch \"$APPIMAGETOOL_URL\" \"$APPIMAGETOOL_SHA256\" \"$work/appimagetool.AppImage\"",
                "fetch \"$RUNTIME_URL\" \"$RUNTIME_SHA256\" \"$work/runtime-x86_64\"",
            },
            fetches);
        // The only download is the one inside fetch.
        Assert.Single(lines, line => line.Contains("curl ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Linux_LAME_build_verifies_the_archive_of_the_Windows_build_and_of_the_notices()
    {
        var linux = Pin(Script("tools", "release", "build-lame-linux.sh"), LinuxPin());

        Assert.Equal(Pin(Script("tools", "release", "build-lame.ps1"), WindowsBuildPin()), linux);
        Assert.Equal(Pin(Script("tools", "release", "check-release-files.ps1"), ReleaseCheckPin()), linux);
        Assert.Equal(Pin(Script("THIRD-PARTY-NOTICES.md"), NoticesPin()), linux);
    }

    [Fact]
    public void The_Linux_LAME_build_refuses_an_archive_with_another_sha256()
    {
        var script = string.Join('\n', Script("tools", "release", "build-lame-linux.sh"));

        Assert.Contains("actual_sha256=\"$(sha256_of \"$archive\")\"\nif [ \"$actual_sha256\" != \"$expected_sha256\" ]; then", script, StringComparison.Ordinal);
        Assert.Contains("Nothing was extracted.\" >&2\n  exit 1\nfi\n", script, StringComparison.Ordinal);
    }

    /// <summary>The lines of a file of the repository, without the CR a Windows clone may add.</summary>
    private static List<string> Script(params string[] path) =>
        File.ReadAllLines(Path.Combine([Root, .. path])).Select(line => line.TrimEnd('\r')).ToList();

    /// <summary>The lines of a shell function, trimmed, between <c>name() {</c> and the first <c>}</c> alone on its line.</summary>
    private static List<string> Function(List<string> lines, string name)
    {
        var start = lines.IndexOf(name + "() {");
        Assert.True(start >= 0, name + "() is not in the script");
        var end = lines.IndexOf("}", start);
        return lines.Skip(start + 1).Take(end - start - 1).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }

    /// <summary>The one SHA-256 the pattern finds in the file.</summary>
    private static string Pin(List<string> lines, Regex pattern)
    {
        var found = lines.Select(line => pattern.Match(line)).Where(match => match.Success).Select(match => match.Groups[1].Value).ToList();
        Assert.Single(found);
        return found[0];
    }

    [GeneratedRegex(@"^echo ""\$sha  \$dest"" \| sha256sum --check( --[a-z]+)* - \|\| fail ""[^""]+""$")]
    private static partial Regex CheckThenFail();

    [GeneratedRegex(@"^expected_sha256=""\$\{LAME_EXPECTED_SHA256:-([0-9a-f]{64})\}""$")]
    private static partial Regex LinuxPin();

    [GeneratedRegex(@"^\s*\[string\]\$ExpectedSha256 = '([0-9a-f]{64})',?$")]
    private static partial Regex WindowsBuildPin();

    [GeneratedRegex(@"^\s*\[string\]\$LameSha256 = '([0-9a-f]{64})',?$")]
    private static partial Regex ReleaseCheckPin();

    [GeneratedRegex(@"^- SHA-256: `([0-9a-f]{64})`$")]
    private static partial Regex NoticesPin();

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
