using System.Diagnostics;

namespace Hindsound.Updates;

/// <summary>
/// The transport of --selftest-update: the manifest and the Setup come from files of a folder. It cannot open a
/// connection. The body goes through the same loop as the real transport, so the limits, the 64 KB pieces and the
/// stop test are the ones a real download meets.
/// </summary>
internal sealed class FolderTransport(string folder) : IUpdateTransport
{
    public const string ManifestFile = "manifest.json";
    public const string SetupFile = "setup.bin";

    public TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop)
    {
        // The real transport refuses these before any connection: a test should not get further with them.
        if (!UpdateUrlPolicy.IsAllowedHop(url, out _))
        {
            return new TransportResult(TransportStatus.Blocked);
        }

        var path = Path.Combine(folder, UpdateUrlPolicy.IsManifestUrl(url) ? ManifestFile : SetupFile);
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What GitHub answers for a file that is not there.
            return new TransportResult(TransportStatus.Failed, 404);
        }

        using (file)
        {
            var clock = Stopwatch.StartNew();
            return TransferLoops.PumpBody((buffer, _) => ReadOnce(file, buffer), limits, sink, shouldStop, () => clock.Elapsed);
        }
    }

    private static BodyRead ReadOnce(FileStream file, byte[] buffer)
    {
        try
        {
            return new BodyRead(file.Read(buffer, 0, buffer.Length));
        }
        catch (IOException)
        {
            return new BodyRead(0, new TransportResult(TransportStatus.Failed));
        }
    }
}
