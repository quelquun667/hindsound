using System.Diagnostics;
using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>The flow on a platform that only tells: the same daily check, one announcement per version, never a download.</summary>
public class UpdateFlowAnnounceOnlyTests
{
    private const string PageOf020 = "https://github.com/quelquun667/hindsound/releases/tag/v0.2.0";

    private static readonly UpdateVersion Next = FlowRig.Next;

    [Fact]
    public void Auto_finds_a_new_version_and_announces_it_without_a_download()
    {
        using var rig = new PageRig();

        rig.CheckFinds();

        Assert.Equal([(Next, UpdateMode.Ask)], rig.Host.Announced);
        Assert.Empty(rig.Host.Passes);
        Assert.Equal([UpdateChannel.LinuxAnnounce.ManifestUrl], rig.Transport.Urls);
        Assert.Empty(rig.Started);
        Assert.Empty(Directory.EnumerateFiles(rig.Folder, "*.exe"));
    }

    [Fact]
    public void A_click_opens_the_page_of_the_announced_version()
    {
        using var rig = new PageRig();
        Assert.Null(rig.Flow.ReleasePageToOpen);

        rig.CheckFinds();

        Assert.Equal(PageOf020, rig.Flow.ReleasePageToOpen);
    }

    [Fact]
    public void A_version_shown_once_is_not_announced_again_at_the_next_check()
    {
        using var rig = new PageRig();
        rig.CheckFinds();
        rig.Flow.Shown();
        Assert.Equal(Next.ToString(), rig.Store.Load().AnnouncedVersion);

        rig.Host.Now += TimeSpan.FromHours(25);
        rig.CheckFinds();

        Assert.Single(rig.Host.Announced);
        Assert.Equal(2, rig.Transport.Urls.Count);
    }

    [Fact]
    public void Update_and_quit_do_nothing()
    {
        using var rig = new PageRig();
        rig.CheckFinds();

        Assert.Equal(ClickKind.Nothing, rig.Flow.UpdateClicked().Kind);
        Assert.False(rig.Flow.ApplyOnQuit());

        Assert.Empty(rig.Host.Passes);
        Assert.Empty(rig.Started);
        Assert.Single(rig.Transport.Urls);
    }

    [Fact]
    public void The_menu_offers_no_update_to_confirm()
    {
        using var rig = new PageRig();
        rig.CheckFinds();

        Assert.Null(rig.Flow.MenuVersion);
    }

    [Fact]
    public void A_switch_between_auto_and_ask_keeps_the_waiting_announcement()
    {
        using var rig = new PageRig();
        rig.CheckFinds();

        rig.Host.Mode = UpdateMode.Ask;
        rig.Flow.SettingsChanged();

        Assert.Equal(0, rig.Host.Withdrawn);
    }

    [Fact]
    public void Off_arms_no_check()
    {
        using var rig = new PageRig(UpdateMode.Off);

        Assert.Empty(rig.Host.Timers);
        Assert.Empty(rig.Transport.Urls);
    }

    [Fact]
    public void The_windows_flow_opens_no_page()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();

        Assert.Null(rig.Flow.ReleasePageToOpen);
        Assert.Equal([UpdateUrlPolicy.ManifestUrl], rig.Transport.Urls);
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

    /// <summary>A Linux flow at 0.1.0, launched 3 minutes ago: the first check is due.</summary>
    private sealed class PageRig : IDisposable
    {
        private readonly TempFolder _temp = new();

        public PageRig(UpdateMode mode = UpdateMode.Auto)
        {
            Folder = _temp.Path;
            Store = new UpdateStateStore(Path.Combine(Folder, "state.json"));
            Host.Now = FlowRig.Now;
            Host.Mode = mode;
            Flow = new UpdateFlow(Store, Folder, FlowRig.Installed, Transport, Started.Add, Host, FlowRig.Now - TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(60))
            {
                StateRetryPause = TimeSpan.Zero,
                Channel = UpdateChannel.LinuxAnnounce,
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
