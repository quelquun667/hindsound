using System.Diagnostics;
using System.Security.Cryptography;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// The manifest that the release workflow writes, read by the app. If the two drifted apart, every installed app would
/// refuse the manifest without a word, and the updates would stop for good.
/// </summary>
public class ManifestFromScriptTests
{
    [Fact]
    public void The_manifest_of_write_manifest_is_valid_for_the_app()
    {
        using var temp = new Export.TempFolder();
        var setupBytes = new byte[2 * 1024 * 1024];
        new Random(7).NextBytes(setupBytes);
        var setup = temp.Combine("Hindsound-1.2.3-Setup.exe");
        File.WriteAllBytes(setup, setupBytes);
        var manifestPath = temp.Combine("hindsound-update-win-x64.json");

        RunWriteManifest("-Version", "1.2.3", "-SetupPath", setup, "-Repository", "quelquun667/hindsound", "-OutFile", manifestPath);
        var status = UpdateManifestReader.Read(File.ReadAllBytes(manifestPath), out var manifest);

        Assert.Equal(ManifestStatus.Valid, status);
        Assert.Equal(new UpdateVersion(1, 2, 3), manifest!.Version);
        Assert.Equal(setupBytes.Length, manifest.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(setupBytes)), manifest.Sha256);
        Assert.Equal(UpdateUrlPolicy.ReleasePrefix + "v1.2.3/Hindsound-1.2.3-Setup.exe", manifest.Url);
    }

    /// <summary>Windows PowerShell, as on every Windows PC and runner: the script must run there too.</summary>
    private static void RunWriteManifest(params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepositoryRoot(), "tools", "release", "write-manifest.ps1") }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "write-manifest.ps1 did not finish in 60 s.");
        Assert.True(process.ExitCode == 0, $"write-manifest.ps1 ended with exit code {process.ExitCode}: {errors}");
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("No Hindsound.slnx above " + AppContext.BaseDirectory);
    }
}
