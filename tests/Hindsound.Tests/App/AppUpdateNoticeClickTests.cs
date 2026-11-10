using Hindsound.App;
using Hindsound.Tests.Export;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// A click on an update notice of Windows, on an app built off screen and never run: it opens the window on its updates,
/// as before, and never the release page.
/// </summary>
public class AppUpdateNoticeClickTests
{
    private const uint Accent = 0x00FF88;

    private static HindsoundApp Built(TempFolder temp, List<Section> opened)
    {
        var options = AppOptions.Parse(["--settings", temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        return new HindsoundApp(options, new Journal(temp.Combine("settings.log"))) { OpenInstead = opened.Add };
    }

    private static List<string> Log(TempFolder temp) =>
        File.Exists(temp.Combine("settings.log")) ? Journal.ReadShared(temp.Combine("settings.log")) : [];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_click_on_an_update_notice_opens_the_window_and_no_page(bool ready)
    {
        using var temp = new TempFolder();
        var opened = new List<Section>();
        var app = Built(temp, opened);
        var notice = ready ? NotificationStates.UpdateReady("0.6.0", Accent) : NotificationStates.UpdateAvailable("0.6.0", Accent);

        app.NotificationClicked(notice);

        Assert.Equal([Section.Now], opened);
        Assert.Contains(Log(temp), line => line.Contains("window gesture UpdateClick: ShowUpdates", StringComparison.Ordinal));
        Assert.DoesNotContain(Log(temp), line => line.Contains("release page", StringComparison.Ordinal));
    }
}
