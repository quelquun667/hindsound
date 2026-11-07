using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The words of the update notifications, their rank, and room enough for them: Fit would cut a sentence without a word.</summary>
public unsafe class UpdateNoticesTests
{
    private const uint Accent = NotificationPalette.Lime;

    [Fact]
    public void Update_ready_says_it_installs_on_closing()
    {
        var content = NotificationStates.UpdateReady("0.2.0", Accent);

        Assert.Equal(NotificationKind.UpdateReady, content.Kind);
        Assert.Equal("Update ready (0.2.0)", content.Title);
        Assert.Equal("Installs when Hindsound closes. Click for details.", content.Body);
        Assert.Empty(content.Keys);
        Assert.Equal(Accent, content.Stripe);
        Assert.Equal(NotificationVisual.Pending, content.Visual);
        Assert.True(content.ClickShowsUpdates);
    }

    [Fact]
    public void Update_available_promises_nothing_about_closing()
    {
        var content = NotificationStates.UpdateAvailable("0.2.0", Accent);

        Assert.Equal(NotificationKind.UpdateReady, content.Kind);
        Assert.Equal("Update available (0.2.0)", content.Title);
        Assert.Equal("Click to see what's new.", content.Body);
        Assert.Equal(Accent, content.Stripe);
        Assert.Equal(NotificationVisual.Pending, content.Visual);
        Assert.True(content.ClickShowsUpdates);
    }

    [Fact]
    public void Update_failed_says_the_version_that_runs_and_that_it_tries_again()
    {
        var content = NotificationStates.UpdateFailed("0.1.0", willRetry: true);

        Assert.Equal(NotificationKind.UpdateFailed, content.Kind);
        Assert.Equal("Update failed", content.Title);
        Assert.Equal("Still on 0.1.0. It will try again later.", content.Body);
        Assert.Equal(NotificationPalette.Hot, content.Stripe);
        Assert.Equal(NotificationVisual.Triangle, content.Visual);
        Assert.True(content.ClickShowsUpdates);
    }

    [Fact]
    public void Update_failed_without_a_new_try_sends_to_the_settings()
    {
        // In Ask me, or after 3 attempts, nothing tries again by itself: "It will try again later" would be false.
        var content = NotificationStates.UpdateFailed("0.1.0", willRetry: false);

        Assert.Equal("Update failed", content.Title);
        Assert.Equal("Still on 0.1.0. Update from Settings > General.", content.Body);
        Assert.True(content.ClickShowsUpdates);
    }

    [Fact]
    public void No_other_notification_shows_the_updates()
    {
        Assert.False(NotificationStates.CaptureOn(300, "Ctrl+Shift+F10").ClickShowsUpdates);
        Assert.False(NotificationStates.SettingsDamaged(copyKept: true).ClickShowsUpdates);
        Assert.False(NotificationStates.SaveFailed().ClickShowsUpdates);
    }

    [Fact]
    public void A_ready_version_is_an_ordinary_notice_and_a_failure_a_lasting_one()
    {
        Assert.Equal(NoticeRank.Ordinary, NoticeQueue.RankOf(NotificationKind.UpdateReady));
        Assert.Equal(NoticeRank.Lasting, NoticeQueue.RankOf(NotificationKind.UpdateFailed));
    }

    [Fact]
    public void Capture_on_after_the_start_does_not_wipe_out_a_failed_update()
    {
        var queue = new NoticeQueue();
        var failed = NotificationStates.UpdateFailed("0.1.0", willRetry: true);
        var captureOn = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", updatedTo: null);
        queue.Add(failed);
        queue.Shown(failed);

        Assert.False(queue.ShowsNow(captureOn));

        queue.Remove(failed);
        queue.Finished();
        Assert.Same(captureOn, queue.Next());
    }

    [Fact]
    public void A_clip_answer_replaces_a_ready_version_in_place()
    {
        var queue = new NoticeQueue();
        var ready = NotificationStates.UpdateReady("0.2.0", Accent);
        queue.Shown(ready);

        Assert.True(queue.ShowsNow(NotificationStates.SaveFailed()));
        Assert.True(queue.ShowsNow(NotificationStates.CaptureOn(300, "Ctrl+Shift+F10")));
    }

    [Fact]
    public void Each_new_notice_has_a_dump_name()
    {
        // The five pictures of --dump-notifications: the same calls as the dump, so each one is drawn without an exception.
        foreach (var content in Samples("0.2.0"))
        {
            using var bitmap = new NotificationBitmap(content, 1.5, Accent, mirrored: false);
            Assert.True(bitmap.Width > 0);
        }
    }

    public static TheoryData<double, string> Scales => new()
    {
        { 1.0, "0.2.0" },
        { 1.5, "0.2.0" },
        { 1.0, "10.20.30" },
        { 1.5, "10.20.30" },
    };

    [Theory]
    [MemberData(nameof(Scales))]
    public void No_text_is_cut_by_fit(double scale, string version)
    {
        // The same painter, engine and fonts as NotificationBitmap.Draw, on a bitmap of the same size.
        var s = (float)scale;
        var width = (int)Math.Round(360 * scale);
        var height = (int)Math.Round(64 * scale);
        using var target = new Target(width, height);
        using var painter = new TextPainter(target.Dc, target.Pixels, TextEngine.Shared, clearType: false);
        var titleFont = new FontSpec(Face.Title, 19 * s);
        var bodyFont = new FontSpec(Face.Body, 13 * s);

        foreach (var content in Samples(version))
        {
            var room = NotificationBitmap.TextWidth(content) * s;
            Assert.Equal(content.Title, painter.Fit(titleFont, content.Title, room));
            if (content.Keys.Count == 0)
            {
                Assert.Equal(content.Body, painter.Fit(bodyFont, content.Body, room));
            }
        }
    }

    private static NotificationContent[] Samples(string version) =>
    [
        NotificationStates.UpdateReady(version, Accent),
        NotificationStates.UpdateAvailable(version, Accent),
        NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", updatedTo: version),
        NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", "Save hotkey is taken", updatedTo: version),
        NotificationStates.UpdateFailed(version, willRetry: true),
        NotificationStates.UpdateFailed(version, willRetry: false),
    ];

    // A 32-bit DIB selected in a memory DC, as NotificationBitmap makes its own.
    private sealed class Target : IDisposable
    {
        private readonly nint _dib, _old;

        public Target(int width, int height)
        {
            Native.BITMAPINFOHEADER info = default;
            info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
            info.Width = width;
            info.Height = -height;
            info.Planes = 1;
            info.BitCount = 32;
            Dc = Native.CreateCompatibleDC(0);
            void* bits;
            _dib = Native.CreateDIBSection(Dc, &info, 0, &bits, 0, 0);
            Assert.NotEqual(0, _dib);
            _old = Native.SelectObject(Dc, _dib);
            Pixels = new Pixels((uint*)bits, width, height);
        }

        public nint Dc { get; }

        public Pixels Pixels { get; }

        public void Dispose()
        {
            Native.SelectObject(Dc, _old);
            Native.DeleteObject(_dib);
            Native.DeleteDC(Dc);
        }
    }
}
