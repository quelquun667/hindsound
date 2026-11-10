using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>The Updates section of a copy that only tells, from fixed facts: a Download button, no What's new, no card.</summary>
public class UpdatePanelAnnounceTests
{
    private static readonly UpdateVersion Installed = new(0, 1, 0);
    private static readonly UpdateVersion Next = new(0, 2, 0);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static UpdatePanelFacts Found(UpdateMode mode, bool announces = true) => new()
    {
        Installed = Installed,
        Mode = mode,
        Now = Now,
        LastCheck = Now - TimeSpan.FromHours(1),
        HighestSeen = Next,
        Manifest = Next,
        AnnouncesOnly = announces,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_version_out_offers_download_and_skip_but_no_whats_new(bool off)
    {
        var view = UpdatePanel.Compute(Found(off ? UpdateMode.Off : UpdateMode.Ask));

        Assert.True(view.Active);
        Assert.True(view.AnnouncesOnly);
        Assert.Equal(Next, view.Offered);
        Assert.Equal("Version 0.2.0 is available.", view.StatusLine);
        Assert.True(view.ShowUpdate);
        Assert.True(view.UpdateEnabled);
        Assert.False(view.ShowWhatsNew);
        Assert.True(view.ShowSkip);
        Assert.True(view.SkipEnabled);
        Assert.True(view.CheckEnabled);
        Assert.Null(view.CardLine);
        Assert.False(view.CardBusy);
    }

    [Fact]
    public void Download_stays_clickable_during_a_check()
    {
        var view = UpdatePanel.Compute(Found(UpdateMode.Ask) with { Pass = UpdatePassKind.Check });

        Assert.Equal("Checking...", view.StatusLine);
        Assert.False(view.CheckEnabled);
        Assert.False(view.SkipEnabled);
        Assert.True(view.UpdateEnabled);
    }

    [Fact]
    public void A_skipped_version_says_so_and_greys_skip()
    {
        var view = UpdatePanel.Compute(Found(UpdateMode.Ask) with { Skipped = Next });

        Assert.Equal("Version 0.2.0 is skipped.", view.StatusLine);
        Assert.False(view.SkipEnabled);
        Assert.True(view.UpdateEnabled);
    }

    [Fact]
    public void A_failed_check_keeps_its_line()
    {
        var view = UpdatePanel.Compute(Found(UpdateMode.Ask) with { Problem = UpdateProblem.CheckFailed });

        Assert.Equal("Couldn't check. It will try again tomorrow.", view.StatusLine);
    }

    [Fact]
    public void Without_a_version_only_check_now_shows()
    {
        var view = UpdatePanel.Compute(Found(UpdateMode.Ask) with { HighestSeen = null, Manifest = null });

        Assert.True(view.AnnouncesOnly);
        Assert.Null(view.Offered);
        Assert.False(view.ShowUpdate);
        Assert.False(view.ShowSkip);
        Assert.False(view.ShowWhatsNew);
        Assert.StartsWith("Up to date.", view.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void The_installed_copy_keeps_its_section()
    {
        var off = UpdatePanel.Compute(Found(UpdateMode.Off, announces: false));
        var ask = UpdatePanel.Compute(Found(UpdateMode.Ask, announces: false) with { Pass = UpdatePassKind.Check });

        Assert.False(off.AnnouncesOnly);
        Assert.True(off.ShowWhatsNew);
        Assert.False(off.UpdateEnabled);
        Assert.Equal("Version 0.2.0 is available. Switch to Ask me to download it.", off.StatusLine);
        Assert.False(ask.UpdateEnabled);
        Assert.False(UpdatePanel.Inactive(Installed).AnnouncesOnly);
    }
}
