using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>The transport of --selftest-update: files of a folder, by the same pieces and the same stop test as the real one.</summary>
public class FolderTransportTests
{
    private const string SetupUrl = "https://github.com/quelquun667/hindsound/releases/download/v99.0.0/setup.exe";

    private static readonly TransferLimits Large = TransferLimits.ForSetup(10 * 1024 * 1024);

    private static byte[] Write(TempFolder folder, string name, int size)
    {
        var body = new byte[size];
        new Random(size).NextBytes(body);
        File.WriteAllBytes(folder.File(name), body);
        return body;
    }

    [Fact]
    public void Manifest_address_reads_the_manifest_and_any_other_the_setup()
    {
        using var folder = new TempFolder();
        var manifest = Write(folder, FolderTransport.ManifestFile, 300);
        var setup = Write(folder, FolderTransport.SetupFile, 5000);
        var transport = new FolderTransport(folder.Path);

        using var first = new MemoryStream();
        using var second = new MemoryStream();
        Assert.Equal(TransportStatus.Completed, transport.Get(UpdateUrlPolicy.ManifestUrl, Large, data => first.Write(data), () => false).Status);
        Assert.Equal(TransportStatus.Completed, transport.Get(SetupUrl, Large, data => second.Write(data), () => false).Status);

        Assert.Equal(manifest, first.ToArray());
        Assert.Equal(setup, second.ToArray());
    }

    [Fact]
    public void Pieces_are_64_KB_at_most()
    {
        using var folder = new TempFolder();
        var setup = Write(folder, FolderTransport.SetupFile, 2 * 1024 * 1024 + 123);
        var pieces = new List<int>();

        new FolderTransport(folder.Path).Get(SetupUrl, Large, data => pieces.Add(data.Length), () => false);

        Assert.All(pieces, length => Assert.InRange(length, 1, 64 * 1024));
        Assert.Equal(setup.Length, pieces.Sum());
    }

    [Fact]
    public void Stop_is_respected_between_pieces()
    {
        using var folder = new TempFolder();
        Write(folder, FolderTransport.SetupFile, 1024 * 1024);
        var tests = 0;
        long received = 0;

        var result = new FolderTransport(folder.Path).Get(SetupUrl, Large, data => received += data.Length, () => ++tests > 2);

        Assert.Equal(TransportStatus.Stopped, result.Status);
        Assert.True(received < 1024 * 1024);
    }

    [Fact]
    public void Missing_file_is_a_failure_not_an_exception()
    {
        using var folder = new TempFolder();

        var result = new FolderTransport(folder.Path).Get(UpdateUrlPolicy.ManifestUrl, TransferLimits.Manifest, _ => { }, () => false);

        Assert.Equal(new TransportResult(TransportStatus.Failed, 404), result);
    }

    [Fact]
    public void Too_large_is_refused()
    {
        using var folder = new TempFolder();
        Write(folder, FolderTransport.ManifestFile, UpdateManifestReader.MaxManifestBytes + 1);

        var result = new FolderTransport(folder.Path).Get(UpdateUrlPolicy.ManifestUrl, TransferLimits.Manifest, _ => { }, () => false);

        Assert.Equal(TransportStatus.TooLarge, result.Status);
    }

    [Theory]
    [InlineData("http://github.com/quelquun667/hindsound/releases/download/v99.0.0/setup.exe")]
    [InlineData("https://example.com/setup.exe")]
    public void Address_outside_the_policy_is_blocked(string url)
    {
        using var folder = new TempFolder();
        Write(folder, FolderTransport.SetupFile, 2000);
        var received = 0;

        var result = new FolderTransport(folder.Path).Get(url, Large, data => received += data.Length, () => false);

        Assert.Equal(TransportStatus.Blocked, result.Status);
        Assert.Equal(0, received);
    }
}
