using System.Diagnostics;
using Hindsound.Updates;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The manifests of the copies that only announce a version (the Linux app, the portable zip for Windows) are written by
/// tools/release/write-announce-manifest.ps1 when a release is made, and read by the app for months after. The very file
/// the script writes goes through the reader of the app here: accepted when it names the page of the release, refused
/// when it names anything else.
/// </summary>
public sealed class AnnounceManifestRoundTripTests : IDisposable
{
    private const string GitHub = "https://github.com/";
    private const string TagPath = "/releases/tag/";

    private static readonly string Root = RepositoryRoot();

    private readonly string folder = Path.Combine(Path.GetTempPath(), "hindsound-announce-test-" + Guid.NewGuid().ToString("N"));

    public AnnounceManifestRoundTripTests() => Directory.CreateDirectory(folder);

    /// <summary>owner/name of the repository the app reads its manifests from.</summary>
    private static string AppRepository
    {
        get
        {
            var prefix = UpdateUrlPolicy.ReleasePagePrefix;
            Assert.StartsWith(GitHub, prefix, StringComparison.Ordinal);
            Assert.EndsWith(TagPath, prefix, StringComparison.Ordinal);
            return prefix[GitHub.Length..^TagPath.Length];
        }
    }

    [Theory]
    [InlineData("hindsound-update-linux-x64.json")]
    [InlineData("hindsound-update-win-x64-portable.json")]
    public void The_manifest_the_release_writes_is_one_the_app_accepts(string name)
    {
        var json = Write(name, "1.2.3", AppRepository);

        Assert.Equal(ManifestStatus.Valid, UpdateManifestReader.Read(json, UpdateDelivery.AnnounceOnly, out var manifest));
        Assert.NotNull(manifest);
        Assert.Equal("1.2.3", manifest.Version.ToString());
        Assert.Equal(UpdateUrlPolicy.ReleasePage(manifest.Version), manifest.Url);
    }

    [Theory]
    [InlineData("someone/hindsound")]
    [InlineData("quelquun667/other")]
    public void A_manifest_naming_the_page_of_another_repository_is_refused(string repository)
    {
        var json = Write("hindsound-update-linux-x64.json", "1.2.3", repository);

        Assert.Equal(ManifestStatus.BadUrl, UpdateManifestReader.Read(json, UpdateDelivery.AnnounceOnly, out var manifest));
        Assert.Null(manifest);
    }

    [Fact]
    public void The_installed_app_never_takes_an_announce_manifest_for_a_Setup()
    {
        var json = Write("hindsound-update-win-x64-portable.json", "1.2.3", AppRepository);

        Assert.Equal(ManifestStatus.BadUrl, UpdateManifestReader.Read(json, UpdateDelivery.Setup, out _));
    }

    [Fact]
    public void The_release_writes_the_manifest_each_channel_reads()
    {
        var assemble = File.ReadAllText(Path.Combine(Root, "tools", "release", "assemble-release.ps1"));
        var check = File.ReadAllText(Path.Combine(Root, "tools", "release", "check-release-files.ps1"));

        Assert.Equal(UpdateDelivery.AnnounceOnly, UpdateChannel.LinuxAnnounce.Delivery);
        Assert.Contains("'" + UpdateChannel.LinuxAnnounce.ManifestName + "'", assemble, StringComparison.Ordinal);
        Assert.Contains("'hindsound-update-win-x64-portable.json'", assemble, StringComparison.Ordinal);
        Assert.Contains("& $writeAnnounce ", assemble, StringComparison.Ordinal);
        Assert.Contains("'" + UpdateChannel.WindowsSetup.ManifestName + "'", check, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private byte[] Write(string name, string version, string repository)
    {
        var script = Path.Combine(Root, "tools", "release", "write-announce-manifest.ps1");
        var output = Path.Combine(folder, name);
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
        }

        foreach (var argument in new[] { "-File", script, "-Version", version, "-Repository", repository, "-OutFile", output })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "write-announce-manifest.ps1 did not end within 60 s");
        Assert.True(process.ExitCode == 0, "write-announce-manifest.ps1 failed: " + standardError + standardOutput.Result);
        return File.ReadAllBytes(output);
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
