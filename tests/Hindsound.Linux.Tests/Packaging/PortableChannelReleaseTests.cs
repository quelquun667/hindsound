using Hindsound.Updates;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The portable Windows copy reads the manifest its channel names, for months after a release. The release scripts write
/// and check that file under a name of their own: both must be the name of the channel, or the copy finds no manifest and
/// never announces a version.
/// </summary>
public sealed class PortableChannelReleaseTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void The_portable_channel_only_announces()
    {
        Assert.Equal(UpdateDelivery.AnnounceOnly, UpdateChannel.WindowsPortable.Delivery);
    }

    [Fact]
    public void The_release_writes_the_manifest_the_portable_channel_reads()
    {
        var assemble = File.ReadAllText(Path.Combine(Root, "tools", "release", "assemble-release.ps1"));

        Assert.Contains("'" + UpdateChannel.WindowsPortable.ManifestName + "'", assemble, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_checks_the_manifest_the_portable_channel_reads()
    {
        var check = File.ReadAllText(Path.Combine(Root, "tools", "release", "check-release-files.ps1"));

        Assert.Contains("'" + UpdateChannel.WindowsPortable.ManifestName + "'", check, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Hindsound.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }
}
