using System.IO;
using System.Security.Cryptography;

namespace Hindsound.Updates;

/// <summary>The files of the updates folder. Only names built here are ever touched.</summary>
internal static class UpdateFiles
{
    private const string PartPattern = "*.part";
    private const string SetupPattern = "setup-*.exe";

    public static string SetupPath(string folder, UpdateVersion version) => Path.Combine(folder, $"setup-{version}.exe");

    /// <summary>The version of the Setup on disk, by its recorded size. The hash waits for the moment it is about to run.</summary>
    public static UpdateVersion? DownloadedVersion(UpdateStateData state, string folder)
    {
        if (state.Pending is not { } pending || !UpdateVersion.TryParse(pending.Version, out var version))
        {
            return null;
        }

        var file = new FileInfo(SetupPath(folder, version));
        return file.Exists && file.Length == pending.Size ? version : null;
    }

    /// <summary>Is this exactly the announced file? Read from where the stream stands, the start for a stream just opened.</summary>
    public static bool Matches(Stream file, long size, string sha256) =>
        file.Length == size && string.Equals(Convert.ToHexString(SHA256.HashData(file)), sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes the pieces and every other Setup. A file still in use stays for the next pass.</summary>
    public static void Clean(string folder, UpdateVersion? keep)
    {
        var kept = keep is { } version ? SetupPath(folder, version) : null;
        try
        {
            foreach (var part in Directory.EnumerateFiles(folder, PartPattern))
            {
                TryDelete(part);
            }

            foreach (var setup in Directory.EnumerateFiles(folder, SetupPattern))
            {
                if (!string.Equals(setup, kept, Library.PathRules.Comparison))
                {
                    TryDelete(setup);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No folder, or no right to list it: nothing that can be cleaned today.
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use, or already out of reach. The next pass tries again.
        }
    }
}

internal enum CheckStatus
{
    /// <summary>A valid manifest was read. Whether it is news is the decider's question.</summary>
    Found,
    TransportFailed,
    ManifestRefused,
}

internal readonly record struct CheckResult(CheckStatus Status, TransportResult Transport, ManifestStatus Manifest, UpdateManifest? Value);

internal enum DownloadStatus
{
    Ready,

    /// <summary>The bytes arrived and are not the announced ones.</summary>
    Mismatch,

    /// <summary>The transport did not finish, see the transport result.</summary>
    Interrupted,

    /// <summary>The disk refused the file.</summary>
    DiskFailed,
}

internal readonly record struct DownloadResult(DownloadStatus Status, TransportResult Transport, string? Path);

/// <summary>The two network passes. Meant to run inside <see cref="UpdatePass"/>, never on the interface thread.</summary>
internal static class UpdateDownloader
{
    public static CheckResult Check(IUpdateTransport transport, Func<bool> shouldStop)
    {
        using var body = new MemoryStream();
        var result = transport.Get(UpdateUrlPolicy.ManifestUrl, TransferLimits.Manifest, data => body.Write(data), shouldStop);
        if (result.Status != TransportStatus.Completed)
        {
            return new CheckResult(CheckStatus.TransportFailed, result, default, null);
        }

        var status = UpdateManifestReader.Read(body.GetBuffer().AsSpan(0, (int)body.Length), out var manifest);
        return new CheckResult(status == ManifestStatus.Valid ? CheckStatus.Found : CheckStatus.ManifestRefused, result, status, manifest);
    }

    /// <summary>Always starts from zero. Whatever happens, no piece and no other Setup is left behind.</summary>
    public static DownloadResult Download(UpdateManifest manifest, string folder, IUpdateTransport transport, Func<bool> shouldStop)
    {
        var target = UpdateFiles.SetupPath(folder, manifest.Version);
        var part = target + ".part";
        try
        {
            Directory.CreateDirectory(folder);
            return Fetch(manifest, target, part, transport, shouldStop);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DownloadResult(DownloadStatus.DiskFailed, default, null);
        }
        finally
        {
            UpdateFiles.Clean(folder, manifest.Version);
        }
    }

    private static DownloadResult Fetch(UpdateManifest manifest, string target, string part, IUpdateTransport transport, Func<bool> shouldStop)
    {
        TransportResult result;
        string hash;
        long received = 0;
        using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            result = transport.Get(
                manifest.Url,
                TransferLimits.ForSetup(manifest.Size),
                data =>
                {
                    received += data.Length;
                    sha256.AppendData(data);
                    output.Write(data);
                },
                shouldStop);
            hash = Convert.ToHexString(sha256.GetHashAndReset());
        }

        if (result.Status != TransportStatus.Completed)
        {
            return new DownloadResult(DownloadStatus.Interrupted, result, null);
        }

        if (received != manifest.Size || !string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadResult(DownloadStatus.Mismatch, result, null);
        }

        File.Move(part, target, overwrite: true);
        return new DownloadResult(DownloadStatus.Ready, result, target);
    }
}
