using System.Diagnostics;
using System.Text;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// The flow of the portable Windows copy: its own manifest, an announcement in the words of Linux, a click that opens the page
/// of the release, and never a download.
/// </summary>
public class UpdateFlowPortableTests
{
    private const string PageOf020 = "https://github.com/quelquun667/hindsound/releases/tag/v0.2.0";
    private const string PortableManifestUrl = "https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64-portable.json";

    private static readonly UpdateVersion Next = FlowRig.Next;

    [Fact]
    public void A_new_version_is_announced_from_the_portable_manifest_without_a_download()
    {
        using var rig = new PortableRig();

        rig.CheckFinds();

        Assert.Equal([PortableManifestUrl], rig.Transport.Urls);
        Assert.Equal([(Next, UpdateMode.Ask)], rig.Host.Announced);
        Assert.Empty(rig.Host.Passes);
        Assert.Empty(rig.Started);
        Assert.Empty(Directory.EnumerateFiles(rig.Folder, "*.exe"));
        Assert.Null(rig.Flow.MenuVersion);
    }

    [Fact]
    public void A_click_on_the_announcement_opens_the_page_of_the_release()
    {
        using var rig = new PortableRig();
        Assert.Null(rig.Flow.ReleasePageToOpen);

        rig.CheckFinds();

        Assert.Equal(PageOf020, rig.Flow.ReleasePageToOpen);
    }

    [Fact]
    public void The_announcement_has_the_words_of_linux()
    {
        var content = ReleaseNotice.Content(Next, 0x00FF88);

        Assert.Equal("Hindsound 0.2.0 is out", content.Title);
        Assert.Equal("Click to download it.", content.Body);
        Assert.Equal(NotificationKind.UpdateReady, content.Kind);
    }

    [Fact]
    public void The_section_of_the_flow_offers_download_and_no_card()
    {
        using var rig = new PortableRig(UpdateMode.Off);
        rig.Flow.CheckNow();
        rig.Host.FinishPass();

        var view = rig.Flow.View;

        Assert.True(view.AnnouncesOnly);
        Assert.Equal(Next, view.Offered);
        Assert.Equal("Version 0.2.0 is available.", view.StatusLine);
        Assert.True(view.UpdateEnabled);
        Assert.False(view.ShowWhatsNew);
        Assert.Equal(ClickKind.Nothing, rig.Flow.UpdateClicked().Kind);
        Assert.Empty(rig.Started);
    }

    [Fact]
    public void The_installed_flow_keeps_its_section()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();

        Assert.False(rig.Flow.View.AnnouncesOnly);
        Assert.Null(rig.Flow.ReleasePageToOpen);
    }

    /// <summary>Serves the manifest of 0.2.0 that names its release page, and writes down what it was asked.</summary>
    private sealed class PageTransport : IUpdateTransport
    {
        public List<string> Urls { get; } = [];

        public TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop)
        {
            Urls.Add(url);
            sink(Encoding.UTF8.GetBytes("{\"product\":\"Hindsound\",\"version\":\"0.2.0\",\"url\":\"" + PageOf020 + "\",\"published\":\"2026-11-06T12:00:00Z\"}"));
            return new TransportResult(TransportStatus.Completed);
        }
    }

    /// <summary>A portable Windows flow at 0.1.0, launched 3 minutes ago: the first check is due.</summary>
    private sealed class PortableRig : IDisposable
    {
        private readonly TempFolder _temp = new();

        public PortableRig(UpdateMode mode = UpdateMode.Auto)
        {
            Folder = _temp.Path;
            Store = new UpdateStateStore(Path.Combine(Folder, "state.json"));
            Host.Now = FlowRig.Now;
            Host.Mode = mode;
            Flow = new UpdateFlow(Store, Folder, FlowRig.Installed, Transport, Started.Add, Host, FlowRig.Now - TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(60))
            {
                StateRetryPause = TimeSpan.Zero,
                Channel = UpdateChannel.WindowsPortable,
            };
            Flow.AtLaunch(false, default);
            Flow.Start();
        }

        public string Folder { get; }

        public UpdateStateStore Store { get; }

        public FakeUpdateHost Host { get; } = new();

        public PageTransport Transport { get; } = new();

        public List<ProcessStartInfo> Started { get; } = [];

        public UpdateFlow Flow { get; }

        public void CheckFinds()
        {
            Host.FireLast();
            Host.FinishPass();
        }

        public void Dispose() => _temp.Dispose();
    }
}
