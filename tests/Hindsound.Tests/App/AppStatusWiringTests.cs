using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Settings;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>
/// The app itself, built but never run: what the menu and the notices read from the last status is read at the moment they
/// are made. KeptWiringTests covers the rules; this covers that the app hands them its last status.
/// </summary>
public class AppStatusWiringTests
{
    private static AudioEngineStatus Holding(double seconds, CaptureActivity activity = CaptureActivity.Capturing) =>
        ScreenRig.Capturing(activity: activity) with { Buffered = TimeSpan.FromSeconds(seconds) };

    private static HindsoundApp Built(TempFolder temp, AudioEngineStatus? lastStatus)
    {
        var options = AppOptions.Parse(["--settings", temp.Combine("settings.json"), "--no-mic"]);
        var app = new HindsoundApp(options, new Journal(temp.Combine("settings.log")));
        if (lastStatus is not null)
        {
            // The status is only ever set by the engine's callback, which needs the whole window loop.
            typeof(HindsoundApp).GetField("_lastStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(app, lastStatus);
        }

        return app;
    }

    [Fact]
    public void The_menu_of_the_app_reads_the_memory_from_its_last_status()
    {
        using var temp = new TempFolder();
        var app = Built(temp, Holding(12));

        var items = app.MenuItemsNow();

        Assert.Contains(items, item => item.Command == TrayCommand.Save5m && item.Text.Contains("only 0:12", StringComparison.Ordinal) && item.Enabled);
        Assert.Equal(TrayMenu.ItemsFor(Holding(12), false, false, null), items);
    }

    [Fact]
    public void Before_any_status_the_menu_of_the_app_promises_nothing_either_way()
    {
        using var temp = new TempFolder();
        var app = Built(temp, null);

        Assert.Equal(TrayMenu.ItemsFor(null, false, false, null), app.MenuItemsNow());
        Assert.DoesNotContain(app.MenuItemsNow(), item => item.Text.Contains("only", StringComparison.Ordinal));
    }

    [Fact]
    public void A_notice_that_waited_is_made_again_from_the_last_status_of_the_app()
    {
        using var temp = new TempFolder();
        var app = Built(temp, Holding(12));
        var waited = NotificationStates.CaptureOn(300, "F24");

        var fresh = app.FreshNow(waited);

        Assert.Equal(StatusTransitions.FreshFrom(waited, new AppSettings(), [], null, Holding(12)).Body, fresh.Body);
        Assert.NotEqual(StatusTransitions.FreshFrom(waited, new AppSettings(), [], null, null).Body, fresh.Body);
    }

    [Fact]
    public void A_status_notice_of_the_app_says_what_the_status_that_raised_it_holds()
    {
        using var temp = new TempFolder();
        var app = Built(temp, null);

        var content = app.NoticeContentFor(StatusNotice.Paused, Holding(12, CaptureActivity.Paused));

        Assert.NotNull(content);
        Assert.Equal(NotificationStates.Paused(12).Body, content.Body);
        Assert.NotEqual(NotificationStates.Paused(0).Body, content.Body);
    }
}
