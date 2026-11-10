namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The AppImage carries the type2 runtime of the AppImage project, and the notices do not record its licence yet, nor
/// those of the libraries built into it. Until they do, no workflow that publishes a release may attach an AppImage: the
/// CI keeps building one as an artifact, but a release would hand it out without the texts its licences ask for.
/// </summary>
public sealed class AppImageReleaseGuardTests
{
    private static readonly string Root = RepositoryRoot();

    private const string NotRecorded = "Not recorded yet";

    [Fact]
    public void No_release_attaches_an_AppImage_while_the_runtime_licence_is_not_recorded()
    {
        if (!RuntimeLicenceCell().StartsWith(NotRecorded, StringComparison.Ordinal))
        {
            return;
        }

        var publishing = ReleaseWorkflows();
        Assert.NotEmpty(publishing);
        foreach (var (name, text) in publishing)
        {
            Assert.False(
                text.Contains("appimage", StringComparison.OrdinalIgnoreCase) || text.Contains("build-packages.sh", StringComparison.Ordinal),
                name + " publishes a release and names the AppImage, whose runtime licence the notices do not record yet");
        }
    }

    [Fact]
    public void The_release_workflow_is_one_that_publishes()
    {
        Assert.Contains(ReleaseWorkflows(), workflow => workflow.Name == "release.yml");
    }

    /// <summary>The licence cell of the runtime's row in the notices.</summary>
    private static string RuntimeLicenceCell()
    {
        var row = File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"))
            .Single(line => line.StartsWith("| AppImage type2 runtime ", StringComparison.Ordinal));
        return row.Split('|')[3].Trim();
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
