using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class WinHttpTransportTests
{
    private const string RealUrl = "https://github.com/microsoft/winget-cli/releases/latest/download/DesktopAppInstaller_Dependencies.json";
    private static readonly TransferLimits Roomy = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), 1024 * 1024);

    [Theory]
    [InlineData("http://github.com/a")]
    [InlineData("https://example.org/a")]
    [InlineData("https://127.0.0.1/a")]
    public void AnAddressOutOfGitHubMakesNoConnection(string url)
    {
        var received = 0;

        var result = new WinHttpTransport().Get(url, Roomy, data => received += data.Length, () => false);

        Assert.Equal(TransportStatus.Blocked, result.Status);
        Assert.Equal(0, received);
    }

    [Fact]
    public void AStopBeforeTheFirstRequestMakesNoConnection()
    {
        var result = new WinHttpTransport().Get(RealUrl, Roomy, _ => { }, () => true);

        Assert.Equal(TransportStatus.Stopped, result.Status);
    }

    /// <summary>Accepts the connection and never says a word: the case that held HttpClient past 125 s in the trials.</summary>
    [Fact]
    public void ASilentServerEndsWithinTheDelay()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var address = new Uri($"https://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/");
        var accepted = server.AcceptTcpClientAsync();

        var limits = new TransferLimits(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 1024);
        var watch = Stopwatch.StartNew();
        var result = new WinHttpTransport().Run(
            address.AbsoluteUri,
            limits,
            _ => { },
            () => false,
            (_, request, _) => request(address).Result);
        watch.Stop();

        Assert.Equal(TransportStatus.TimedOut, result.Status);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void AnExhaustedDelayMakesNoConnection()
    {
        var limits = new TransferLimits(TimeSpan.Zero, TimeSpan.FromSeconds(1), 1024);

        var result = new WinHttpTransport().Get(RealUrl, limits, _ => { }, () => false);

        Assert.Equal(TransportStatus.TimedOut, result.Status);
    }

    // ---- On the real network

    [Fact]
    [Trait("Category", "Device")]
    public void ARealGetFollowsTheRedirectsToTheAssetHost()
    {
        using var body = new MemoryStream();

        var result = new WinHttpTransport().Get(RealUrl, Roomy, data => body.Write(data), () => false);

        Assert.Equal(new TransportResult(TransportStatus.Completed), result);
        Assert.Contains("Dependencies", Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    [Trait("Category", "Device")]
    public void ARealGetIsRefusedFromTheAnnouncedLengthWithoutReadingABody()
    {
        var received = 0;
        var limits = Roomy with { MaxBytes = 10 };

        var result = new WinHttpTransport().Get(RealUrl, limits, data => received += data.Length, () => false);

        Assert.Equal(TransportStatus.TooLarge, result.Status);
        Assert.Equal(0, received);
    }

    [Fact]
    [Trait("Category", "Device")]
    public void AMissingFileOnGitHubIsAFailureWithItsStatus()
    {
        var result = new WinHttpTransport().Get(
            "https://github.com/microsoft/winget-cli/releases/latest/download/no-such-file-for-hindsound.json",
            Roomy,
            _ => { },
            () => false);

        Assert.Equal(new TransportResult(TransportStatus.Failed, 404), result);
    }

    [Fact]
    [Trait("Category", "Device")]
    public void ARealDownloadWritesAndChecksTheFileInATemporaryFolder()
    {
        using var folder = new TempFolder();
        using var body = new MemoryStream();
        Assert.Equal(TransportStatus.Completed, new WinHttpTransport().Get(RealUrl, Roomy, data => body.Write(data), () => false).Status);
        var manifest = new UpdateManifest(
            new UpdateVersion(0, 0, 1),
            RealUrl,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body.ToArray())),
            body.Length,
            DateTimeOffset.UnixEpoch);

        var result = UpdateDownloader.Download(manifest, folder.Path, new WinHttpTransport(), () => false);

        Assert.Equal(DownloadStatus.Ready, result.Status);
        Assert.Equal(body.ToArray(), File.ReadAllBytes(result.Path!));
        Assert.Equal(["setup-0.0.1.exe"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }
}
