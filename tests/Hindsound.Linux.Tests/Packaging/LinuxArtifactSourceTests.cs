namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The artifact of the linux-package job is what users download, and it ships libmp3lame.so.0 under the LGPL: the LAME
/// archive the library was built from goes into it, next to the packages.
/// </summary>
public sealed class LinuxArtifactSourceTests
{
    private const string Work = "\"${{ runner.temp }}/lame-work\"";
    private const string Packages = "${{ runner.temp }}/packages/";

    [Fact]
    public void The_artifact_holds_the_LAME_archive_the_shipped_library_was_built_from()
    {
        var job = LinuxPackageJob();

        var build = job.IndexOf("bash tools/release/build-lame-linux.sh " + Work + " ", StringComparison.Ordinal);
        var copy = job.IndexOf("cp -- \"${{ runner.temp }}/lame-work/lame-3.100.tar.gz\" \"" + Packages + "\"", StringComparison.Ordinal);
        var upload = job.IndexOf("uses: actions/upload-artifact@", StringComparison.Ordinal);

        Assert.True(build >= 0, "the job does not build LAME in " + Work);
        Assert.True(copy > build, "the job does not copy the verified archive into " + Packages + " after the build");
        Assert.True(upload > copy, "the archive is copied after the upload");
        Assert.Contains("path: " + Packages + "\n", job[upload..], StringComparison.Ordinal);
    }

    /// <summary>The text of the linux-package job, up to the end of the file or the next job.</summary>
    private static string LinuxPackageJob()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = ci.IndexOf("\n  linux-package:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "ci.yml has no linux-package job");
        var next = System.Text.RegularExpressions.Regex.Match(ci[(start + 1)..], "\n  [a-z-]+:\n");
        return next.Success ? ci.Substring(start, next.Index + 1) : ci[start..];
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
