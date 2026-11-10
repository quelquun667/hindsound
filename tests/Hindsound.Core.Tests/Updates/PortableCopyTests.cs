using System.Text;
using Hindsound.App;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// The portable Windows copy: portable.txt next to the exe picks a channel that only tells, an installed copy stays installed,
/// and a test run keeps its rules. Its manifest is read by the rules of Linux's, and its state lives apart.
/// </summary>
public class PortableCopyTests
{
    private const string PortableManifestUrl = "https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64-portable.json";
    private const string PageOf020 = "https://github.com/quelquun667/hindsound/releases/tag/v0.2.0";
    private const string SetupOf020 = "https://github.com/quelquun667/hindsound/releases/download/v0.2.0/Hindsound-0.2.0-Setup.exe";

    private static readonly UpdateVersion Next = new(0, 2, 0);

    // ---- The choice of the channel

    [Fact]
    public void Without_portable_txt_a_copy_keeps_the_installed_channel()
    {
        using var folder = new TempFolder();

        Assert.Equal(UpdateChannel.WindowsSetup, PortableCopy.WindowsChannel(folder.Path, out var ignored));
        Assert.False(ignored);
    }

    [Fact]
    public void Portable_txt_next_to_the_exe_picks_the_portable_channel()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File(PortableCopy.MarkerName), "portable");

        Assert.Equal(UpdateChannel.WindowsPortable, PortableCopy.WindowsChannel(folder.Path, out var ignored));
        Assert.False(ignored);
    }

    [Fact]
    public void An_installed_copy_stays_installed_and_says_portable_txt_was_ignored()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File(PortableCopy.MarkerName), "portable");
        File.WriteAllText(folder.File(InstallKind.UninstallerName), "uninstaller");

        Assert.Equal(UpdateChannel.WindowsSetup, PortableCopy.WindowsChannel(folder.Path, out var ignored));
        Assert.True(ignored);
        Assert.Equal(UpdateWiring.Installed, UpdateWiring.Why(true, AppOptions.Parse([]), folder.Path, UpdateChannel.WindowsSetup));
    }

    [Fact]
    public void An_installed_copy_without_portable_txt_has_nothing_to_say()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File(InstallKind.UninstallerName), "uninstaller");

        Assert.Equal(UpdateChannel.WindowsSetup, PortableCopy.WindowsChannel(folder.Path, out var ignored));
        Assert.False(ignored);
    }

    [Fact]
    public void A_folder_named_portable_txt_is_not_the_marker()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File(PortableCopy.MarkerName));

        Assert.Equal(UpdateChannel.WindowsSetup, PortableCopy.WindowsChannel(folder.Path, out _));
    }

    [Fact]
    public void No_folder_keeps_the_installed_channel()
    {
        Assert.Equal(UpdateChannel.WindowsSetup, PortableCopy.WindowsChannel(null, out var ignored));
        Assert.False(ignored);
    }

    [Fact]
    public void A_portable_copy_started_as_a_user_starts_it_announces_and_is_active()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File(PortableCopy.MarkerName), "portable");
        var channel = PortableCopy.WindowsChannel(folder.Path, out _);

        var why = UpdateWiring.Why(true, AppOptions.Parse([]), folder.Path, channel);

        Assert.Equal(UpdateWiring.AnnouncesOnly, why);
        Assert.True(UpdateWiring.IsActive(why));
    }

    [Fact]
    public void A_test_run_of_a_portable_copy_keeps_its_rules()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File(PortableCopy.MarkerName), "portable");
        var channel = PortableCopy.WindowsChannel(folder.Path, out _);
        var settings = folder.File("settings.json");

        Assert.Equal(UpdateWiring.TestRun, UpdateWiring.Why(false, AppOptions.Parse(["--settings", settings]), folder.Path, channel));
        Assert.Equal(UpdateWiring.TestFolder, UpdateWiring.Why(false, AppOptions.Parse(["--settings", settings, "--selftest-update", folder.Path]), folder.Path, channel));
        Assert.False(UpdateWiring.IsActive(UpdateWiring.TestRun));
    }

    // ---- The channel itself

    [Fact]
    public void The_portable_channel_reads_its_own_manifest_and_only_tells()
    {
        Assert.Equal("hindsound-update-win-x64-portable.json", UpdateChannel.WindowsPortable.ManifestName);
        Assert.Equal(PortableManifestUrl, UpdateChannel.WindowsPortable.ManifestUrl);
        Assert.True(UpdateChannel.WindowsPortable.AnnouncesOnly);
        Assert.False(UpdateChannel.WindowsSetup.AnnouncesOnly);
        Assert.NotEqual(UpdateChannel.WindowsSetup, UpdateChannel.WindowsPortable);
        Assert.NotEqual(UpdateChannel.LinuxAnnounce, UpdateChannel.WindowsPortable);
    }

    [Fact]
    public void The_portable_state_lives_apart_and_the_others_keep_theirs()
    {
        Assert.Equal(UpdateStateStore.DefaultFolder, UpdateStateStore.FolderFor(UpdateChannel.WindowsSetup));
        Assert.Equal(UpdateStateStore.DefaultFolder, UpdateStateStore.FolderFor(UpdateChannel.LinuxAnnounce));

        var portable = UpdateStateStore.FolderFor(UpdateChannel.WindowsPortable);
        Assert.NotEqual(UpdateStateStore.DefaultFolder, portable);
        Assert.Equal("updates-portable", Path.GetFileName(portable));
        Assert.Equal(Path.GetDirectoryName(UpdateStateStore.DefaultFolder), Path.GetDirectoryName(portable));
    }

    // ---- Its manifest

    [Fact]
    public void The_portable_check_asks_its_manifest_and_accepts_a_page_of_its_release()
    {
        var transport = new FakeTransport(Manifest(PageOf020));

        var result = UpdateDownloader.Check(transport, UpdateChannel.WindowsPortable, () => false);

        Assert.Equal([PortableManifestUrl], transport.Urls);
        Assert.Equal([TransferLimits.Manifest], transport.Limits);
        Assert.Equal(CheckStatus.Found, result.Status);
        Assert.Equal(Next, result.Value!.Version);
        Assert.Equal(PageOf020, result.Value.Url);
    }

    [Theory]
    [InlineData(SetupOf020)]
    [InlineData("https://github.com/quelquun667/hindsound/releases/tag/v0.3.0")]
    [InlineData("https://github.com/someone/hindsound/releases/tag/v0.2.0")]
    [InlineData("http://github.com/quelquun667/hindsound/releases/tag/v0.2.0")]
    public void The_portable_check_refuses_what_linux_refuses(string url)
    {
        var transport = new FakeTransport(Manifest(url));

        var result = UpdateDownloader.Check(transport, UpdateChannel.WindowsPortable, () => false);

        Assert.Equal(CheckStatus.ManifestRefused, result.Status);
        Assert.Equal(ManifestStatus.BadUrl, result.Manifest);
    }

    private static byte[] Manifest(string url) => Encoding.UTF8.GetBytes(
        "{\"product\":\"Hindsound\",\"version\":\"0.2.0\",\"url\":\"" + url + "\",\"published\":\"2026-11-06T12:00:00Z\"}");
}
