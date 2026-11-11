namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The runtime at the head of the AppImage is statically linked with libfuse, under the LGPL-2.1, and the notices meet
/// section 6 (d) of that licence by offering the source from the same place as the AppImage. So every workflow that
/// publishes a release and attaches the AppImage attaches the source of libfuse and of the runtime with it, and the check
/// of the files of a release refuses one without them: an AppImage never goes out alone.
/// </summary>
public sealed class AppImageReleaseGuardTests
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>The source of libfuse and of the AppImage runtime, as fetch-runtime-sources.sh names them.</summary>
    private static readonly string[] RuntimeSources = ["fuse-3.15.0.tar.xz", "type2-runtime-20251108-source.tar.gz"];

    [Fact]
    public void Every_release_that_attaches_the_AppImage_attaches_its_runtime_source()
    {
        var attaching = ReleaseWorkflows()
            .Where(workflow => workflow.Text.Contains(".AppImage", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(attaching);
        foreach (var (name, text) in attaching)
        {
            var uploads = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Where(line => line.Contains("gh release upload ", StringComparison.Ordinal) || line.Contains("action-gh-release", StringComparison.Ordinal))
                .ToList();
            Assert.True(uploads.Count > 0, name + " names the AppImage but uploads nothing with gh release upload");
            foreach (var upload in uploads.Where(line => line.Contains(".AppImage", StringComparison.Ordinal)))
            {
                foreach (var source in RuntimeSources)
                {
                    Assert.True(upload.Contains("/" + source + "\"", StringComparison.Ordinal), name + " attaches the AppImage without " + source);
                }
            }

            Assert.Contains(uploads, line => line.Contains(".AppImage", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_check_of_the_files_of_a_release_requires_the_runtime_source()
    {
        var check = File.ReadAllText(Path.Combine(Root, "tools", "release", "check-release-files.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("$fuseName = '" + RuntimeSources[0] + "'\n", check, StringComparison.Ordinal);
        Assert.Contains("$runtimeSourceName = '" + RuntimeSources[1] + "'\n", check, StringComparison.Ordinal);
        Assert.Contains("$manifestName, $announceNames[0], $announceNames[1], $lameName, $fuseName, $runtimeSourceName, $sumsName\n)", check, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_workflow_is_one_that_publishes()
    {
        Assert.Contains(ReleaseWorkflows(), workflow => workflow.Name == "release.yml");
    }

    /// <summary>The workflows that create, edit or upload to a GitHub release.</summary>
    private static List<(string Name, string Text)> ReleaseWorkflows() =>
        Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml")
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)))
            .Where(workflow => workflow.Text.Contains("gh release", StringComparison.Ordinal)
                || workflow.Text.Contains("action-gh-release", StringComparison.Ordinal))
            .ToList();

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
