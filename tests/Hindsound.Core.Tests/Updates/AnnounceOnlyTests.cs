using System.Text;
using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>A platform that only tells about a new version: its manifest, its words, and a decider that never installs.</summary>
public class AnnounceOnlyTests
{
    private const string LinuxManifestUrl = "https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-linux-x64.json";
    private const string PageOf020 = "https://github.com/quelquun667/hindsound/releases/tag/v0.2.0";
    private const string SetupOf020 = "https://github.com/quelquun667/hindsound/releases/download/v0.2.0/Hindsound-0.2.0-Setup.exe";

    private static readonly UpdateVersion Installed = new(0, 1, 0);
    private static readonly UpdateVersion Next = new(0, 2, 0);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    // ---- The channels

    [Fact]
    public void Windows_keeps_its_manifest_and_installs()
    {
        Assert.Equal(UpdateUrlPolicy.ManifestUrl, UpdateChannel.WindowsSetup.ManifestUrl);
        Assert.Equal("https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64.json", UpdateChannel.WindowsSetup.ManifestUrl);
        Assert.Equal(UpdateDelivery.Setup, UpdateChannel.WindowsSetup.Delivery);
        Assert.False(UpdateChannel.WindowsSetup.AnnouncesOnly);
    }

    [Fact]
    public void Linux_reads_its_own_manifest_and_only_tells()
    {
        Assert.Equal(LinuxManifestUrl, UpdateChannel.LinuxAnnounce.ManifestUrl);
        Assert.True(UpdateChannel.LinuxAnnounce.AnnouncesOnly);
    }

    [Theory]
    [InlineData("../hindsound.json")]
    [InlineData("a/b.json")]
    [InlineData("hindsound.exe")]
    [InlineData(".json")]
    [InlineData("hindsound.json?x=1")]
    [InlineData("")]
    public void A_manifest_name_that_is_not_a_plain_json_file_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => UpdateUrlPolicy.ManifestUrlFor(name));
    }

    // ---- The words

    [Fact]
    public void The_announcement_says_the_version_is_out_and_a_click_downloads_it()
    {
        var content = ReleaseNotice.Content(Next, 0x123456);

        Assert.Equal("Hindsound 0.2.0 is out", content.Title);
        Assert.Equal("Click to download it.", content.Body);
        Assert.Equal(NotificationKind.UpdateReady, content.Kind);
        Assert.False(content.ClickShowsUpdates);
        Assert.False(content.ClickOpensSettings);
        Assert.Null(content.ClipPath);
    }

    [Fact]
    public void The_release_page_is_built_from_the_version()
    {
        Assert.Equal(PageOf020, UpdateUrlPolicy.ReleasePage(Next));
        Assert.True(UpdateUrlPolicy.IsReleasePage(PageOf020, Next));
        Assert.False(UpdateUrlPolicy.IsReleasePage(PageOf020 + "/", Next));
        Assert.False(UpdateUrlPolicy.IsReleasePage("https://github.com/quelquun667/hindsound/releases/tag/v0.3.0", Next));
        Assert.False(UpdateUrlPolicy.IsReleasePage(null, Next));
    }

    // ---- The manifest

    [Fact]
    public void A_page_manifest_needs_no_hash_nor_size()
    {
        var status = UpdateManifestReader.Read(Manifest(PageOf020), UpdateDelivery.AnnounceOnly, out var manifest);

        Assert.Equal(ManifestStatus.Valid, status);
        Assert.Equal(Next, manifest!.Version);
        Assert.Equal(PageOf020, manifest.Url);
    }

    [Theory]
    [InlineData(SetupOf020)]
    [InlineData("https://github.com/quelquun667/hindsound/releases/tag/v0.3.0")]
    [InlineData("https://github.com/someone/hindsound/releases/tag/v0.2.0")]
    [InlineData("https://example.org/quelquun667/hindsound/releases/tag/v0.2.0")]
    [InlineData("http://github.com/quelquun667/hindsound/releases/tag/v0.2.0")]
    [InlineData("https://github.com/quelquun667/hindsound/releases/tag/v0.2.0?x=1")]
    public void A_page_manifest_naming_anything_but_its_own_release_page_is_refused(string url)
    {
        Assert.Equal(ManifestStatus.BadUrl, UpdateManifestReader.Read(Manifest(url), UpdateDelivery.AnnounceOnly, out var manifest));
        Assert.Null(manifest);
    }

    [Fact]
    public void A_page_manifest_still_needs_its_product_version_and_date()
    {
        Assert.Equal(ManifestStatus.WrongProduct, UpdateManifestReader.Read(Encoding.UTF8.GetBytes("{\"product\":\"Other\",\"version\":\"0.2.0\",\"url\":\"" + PageOf020 + "\",\"published\":\"2026-11-06T12:00:00Z\"}"), UpdateDelivery.AnnounceOnly, out _));
        Assert.Equal(ManifestStatus.BadVersion, UpdateManifestReader.Read(Encoding.UTF8.GetBytes("{\"product\":\"Hindsound\",\"version\":\"0.2\",\"url\":\"" + PageOf020 + "\",\"published\":\"2026-11-06T12:00:00Z\"}"), UpdateDelivery.AnnounceOnly, out _));
        Assert.Equal(ManifestStatus.BadPublished, UpdateManifestReader.Read(Encoding.UTF8.GetBytes("{\"product\":\"Hindsound\",\"version\":\"0.2.0\",\"url\":\"" + PageOf020 + "\"}"), UpdateDelivery.AnnounceOnly, out _));
    }

    [Fact]
    public void The_windows_reader_refuses_a_page_manifest()
    {
        Assert.Equal(ManifestStatus.BadUrl, UpdateManifestReader.Read(Manifest(PageOf020), out _));
        Assert.Equal(ManifestStatus.BadUrl, UpdateManifestReader.Read(Manifest(PageOf020), UpdateDelivery.Setup, out _));
    }

    [Fact]
    public void The_check_of_a_page_channel_asks_its_own_manifest_and_reads_it_by_its_rules()
    {
        var transport = new FakeTransport(Manifest(PageOf020));

        var result = UpdateDownloader.Check(transport, UpdateChannel.LinuxAnnounce, () => false);

        Assert.Equal([LinuxManifestUrl], transport.Urls);
        Assert.Equal([TransferLimits.Manifest], transport.Limits);
        Assert.Equal(CheckStatus.Found, result.Status);
        Assert.Equal(Next, result.Value!.Version);
    }

    [Fact]
    public void The_check_without_a_channel_is_the_windows_one()
    {
        var transport = new FakeTransport(Manifest(PageOf020));

        var result = UpdateDownloader.Check(transport, () => false);

        Assert.Equal([UpdateUrlPolicy.ManifestUrl], transport.Urls);
        Assert.Equal(CheckStatus.ManifestRefused, result.Status);
        Assert.Equal(ManifestStatus.BadUrl, result.Manifest);
    }

    // ---- The decider

    [Fact]
    public void Install_automatically_only_tells_where_nothing_installs()
    {
        Assert.Equal(UpdateMode.Ask, UpdateDecider.EffectiveMode(UpdateDelivery.AnnounceOnly, UpdateMode.Auto));
        Assert.Equal(UpdateMode.Ask, UpdateDecider.EffectiveMode(UpdateDelivery.AnnounceOnly, UpdateMode.Ask));
        Assert.Equal(UpdateMode.Off, UpdateDecider.EffectiveMode(UpdateDelivery.AnnounceOnly, UpdateMode.Off));
        Assert.Equal(UpdateMode.Auto, UpdateDecider.EffectiveMode(UpdateDelivery.Setup, UpdateMode.Auto));
    }

    [Fact]
    public void A_new_version_in_auto_is_announced_not_downloaded()
    {
        var known = Known(UpdateEvent.Tick, UpdateMode.Auto);

        Assert.Equal(UpdateAction.Announce, UpdateDecider.Decide(known));
        Assert.Equal(UpdateAction.Download, UpdateDecider.Decide(known with { Delivery = UpdateDelivery.Setup }));
    }

    [Fact]
    public void A_version_already_announced_is_not_announced_again()
    {
        Assert.Equal(UpdateAction.None, UpdateDecider.Decide(Known(UpdateEvent.Tick, UpdateMode.Auto) with { Announced = Next }));
    }

    [Fact]
    public void A_due_check_is_made_and_off_makes_none()
    {
        var due = Known(UpdateEvent.Tick, UpdateMode.Auto) with { LastCheck = Now - TimeSpan.FromHours(25), Manifest = null };

        Assert.Equal(UpdateAction.Check, UpdateDecider.Decide(due));
        Assert.Equal(UpdateAction.None, UpdateDecider.Decide(due with { Mode = UpdateMode.Off }));
        Assert.Equal(UpdateAction.Check, UpdateDecider.Decide(due with { Event = UpdateEvent.CheckNowClicked }));
    }

    [Fact]
    public void A_game_delays_the_announcement()
    {
        Assert.Equal(UpdateAction.WaitThirtyMinutes, UpdateDecider.Decide(Known(UpdateEvent.Tick, UpdateMode.Auto) with { FullScreen = true }));
    }

    [Theory]
    [InlineData(UpdateEvent.UpdateClicked)]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void Nothing_ever_downloads_or_installs(UpdateEvent trigger)
    {
        var known = Known(trigger, UpdateMode.Auto) with { Downloaded = Next };

        Assert.Equal(UpdateAction.None, UpdateDecider.Decide(known));
        Assert.NotEqual(UpdateAction.None, UpdateDecider.Decide(known with { Delivery = UpdateDelivery.Setup }));
    }

    // ---- The wiring

    [Fact]
    public void Any_copy_started_as_a_user_starts_it_announces_where_the_platform_only_tells()
    {
        var options = AppOptions.Parse([]);
        var noInstaller = Path.GetTempPath();

        Assert.Equal(UpdateWiring.AnnouncesOnly, UpdateWiring.Why(true, options, noInstaller, UpdateChannel.LinuxAnnounce));
        Assert.Equal(UpdateWiring.AnnouncesOnly, UpdateWiring.Why(true, options, null, UpdateChannel.LinuxAnnounce));
        Assert.Equal(UpdateWiring.TestRun, UpdateWiring.Why(false, options, noInstaller, UpdateChannel.LinuxAnnounce));
        Assert.Equal(UpdateWiring.NotInstalled, UpdateWiring.Why(true, options, noInstaller, UpdateChannel.WindowsSetup));
        Assert.Equal(UpdateWiring.NotInstalled, UpdateWiring.Why(true, options, noInstaller));
    }

    [Fact]
    public void Only_an_installed_copy_or_an_announcing_one_is_active()
    {
        Assert.True(UpdateWiring.IsActive(UpdateWiring.Installed));
        Assert.True(UpdateWiring.IsActive(UpdateWiring.AnnouncesOnly));
        Assert.False(UpdateWiring.IsActive(UpdateWiring.NotInstalled));
        Assert.False(UpdateWiring.IsActive(UpdateWiring.TestRun));
        Assert.False(UpdateWiring.IsActive(UpdateWiring.TestFolder));
    }

    private static byte[] Manifest(string url) => Encoding.UTF8.GetBytes(
        "{\"product\":\"Hindsound\",\"version\":\"0.2.0\",\"url\":\"" + url + "\",\"published\":\"2026-11-06T12:00:00Z\"}");

    // 0.2.0 is known from a check an hour ago, nothing is on screen or being saved.
    private static UpdateSituation Known(UpdateEvent trigger, UpdateMode mode) => new()
    {
        Event = trigger,
        Mode = mode,
        Now = Now,
        AppStartedAt = Now - TimeSpan.FromHours(2),
        LastCheck = Now - TimeSpan.FromHours(1),
        Installed = Installed,
        Manifest = Next,
        HighestSeen = Next,
        Delivery = UpdateDelivery.AnnounceOnly,
    };
}
