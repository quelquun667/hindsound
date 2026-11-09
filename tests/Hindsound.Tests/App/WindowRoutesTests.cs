using Hindsound.App;
using Hindsound.Export;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>Where each gesture leads, one line per line of the decided table, with the window closed and open.</summary>
public class WindowRoutesTests
{
    private const Section LastPage = Section.Export;

    private static WindowRoute Closed(WindowGesture gesture) => WindowRoutes.Of(gesture, null, LastPage);

    private static WindowRoute OpenOn(WindowGesture gesture, Section view) => WindowRoutes.Of(gesture, view, LastPage);

    [Fact]
    public void Opening_the_app_lands_on_now_when_closed()
    {
        Assert.Equal(new WindowRoute(WindowAction.Show, Section.Now), Closed(WindowGesture.OpenApp));
    }

    [Fact]
    public void Opening_the_app_only_brings_an_open_window_up_on_its_view()
    {
        Section[] all = [Section.Now, Section.Audio, Section.FirstRun, Section.UpdateCard];
        foreach (var view in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, view), OpenOn(WindowGesture.OpenApp, view));
        }
    }

    [Fact]
    public void The_settings_line_of_the_menu_lands_on_the_last_page_seen_when_closed()
    {
        Assert.Equal(new WindowRoute(WindowAction.Show, LastPage), Closed(WindowGesture.SettingsMenu));
        Assert.Equal(new WindowRoute(WindowAction.Show, Section.Hotkeys), WindowRoutes.Of(WindowGesture.SettingsMenu, null, Section.Hotkeys));
    }

    [Fact]
    public void The_settings_line_of_the_menu_lands_on_the_last_page_seen_when_open()
    {
        Section[] all = [Section.Now, Section.Durations];
        foreach (var view in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, LastPage), OpenOn(WindowGesture.SettingsMenu, view));
        }
    }

    [Fact]
    public void Until_library_exists_a_click_on_a_clip_opens_its_folder_and_leaves_the_window_alone()
    {
        Section?[] all = [null, Section.Now, Section.General];
        foreach (var view in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.OpenClipFolder), WindowRoutes.Of(WindowGesture.ClipSavedClick, view, LastPage));
            Assert.Equal(new WindowRoute(WindowAction.OpenClipFolder), WindowRoutes.Of(WindowGesture.OtherClipClick, view, LastPage));
        }
    }

    [Fact]
    public void A_click_on_the_state_of_the_capture_lands_on_now()
    {
        Section?[] all = [null, Section.Now, Section.Export];
        foreach (var view in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.Now), WindowRoutes.Of(WindowGesture.StatusClick, view, LastPage));
        }
    }

    [Fact]
    public void A_click_on_a_refused_hotkey_lands_on_hotkeys_whatever_the_last_page()
    {
        Section?[] all = [null, Section.Now, Section.Export];
        foreach (var view in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.Hotkeys), WindowRoutes.Of(WindowGesture.HotkeyRefusedClick, view, LastPage));
        }
    }

    [Fact]
    public void A_click_on_a_problem_with_the_settings_lands_in_the_settings()
    {
        Section?[] all = [null, Section.Now];
        foreach (var view in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, LastPage), WindowRoutes.Of(WindowGesture.SettingsProblemClick, view, LastPage));
        }
    }

    [Fact]
    public void An_update_notice_shows_the_updates_section_and_the_menu_shows_the_card()
    {
        Assert.Equal(new WindowRoute(WindowAction.ShowUpdates, Section.Now), Closed(WindowGesture.UpdateClick));
        Assert.Equal(new WindowRoute(WindowAction.ShowUpdates, Section.Audio), OpenOn(WindowGesture.UpdateClick, Section.Audio));
        Assert.Equal(new WindowRoute(WindowAction.ShowUpdateCard, Section.Now), Closed(WindowGesture.UpdateMenu));
        Assert.Equal(new WindowRoute(WindowAction.ShowUpdateCard, Section.Audio), OpenOn(WindowGesture.UpdateMenu, Section.Audio));
    }

    [Fact]
    public void The_first_launch_opens_its_card()
    {
        Assert.Equal(new WindowRoute(WindowAction.Show, Section.FirstRun), Closed(WindowGesture.FirstRunCard));
    }

    [Fact]
    public void A_card_is_never_left_by_force()
    {
        WindowGesture[] all = [WindowGesture.SettingsMenu, WindowGesture.StatusClick, WindowGesture.HotkeyRefusedClick, WindowGesture.SettingsProblemClick];
        foreach (var gesture in all)
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.FirstRun), OpenOn(gesture, Section.FirstRun));
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.UpdateCard), OpenOn(gesture, Section.UpdateCard));
        }
    }

    [Fact]
    public void A_card_stays_under_the_update_gestures_for_the_screen_to_refuse()
    {
        Assert.Equal(new WindowRoute(WindowAction.ShowUpdates, Section.FirstRun), OpenOn(WindowGesture.UpdateClick, Section.FirstRun));
        Assert.Equal(new WindowRoute(WindowAction.ShowUpdateCard, Section.FirstRun), OpenOn(WindowGesture.UpdateMenu, Section.FirstRun));
    }

    [Fact]
    public void Every_gesture_has_its_line_and_only_the_clips_leave_the_window_alone()
    {
        foreach (var gesture in Enum.GetValues<WindowGesture>())
        {
            var route = Closed(gesture);
            if (gesture is WindowGesture.ClipSavedClick or WindowGesture.OtherClipClick)
            {
                Assert.Null(route.Section);
            }
            else
            {
                Assert.NotNull(route.Section);
            }
        }
    }

    // ---------------------------------------------------------------- what a click on a notification is

    private static ClipResult Clip(bool silent = false, bool wav = false, bool rescued = false, double seconds = 30) =>
        new([@"C:\clips\a.mp3"], TimeSpan.FromSeconds(seconds), 1000, silent, [0.5f], rescued, wav);

    private static NotificationContent OnClip(ClipResult clip, bool copyWanted = true, bool copied = true) =>
        NotificationStates.ForClip(clip, TimeSpan.FromSeconds(30), copyWanted, copied, 0x112233);

    [Fact]
    public void Whatever_says_clip_saved_is_the_clip_saved_click()
    {
        NotificationContent[] all =
        [
            OnClip(Clip()),
            OnClip(Clip(seconds: 12)),
            OnClip(Clip(silent: true)),
            OnClip(Clip(), copied: false),
            OnClip(Clip(), copyWanted: false, copied: false),
            OnClip(Clip(wav: true)),
        ];

        Assert.Equal(6, all.Select(content => content.Kind).Distinct().Count());
        Assert.All(all, content =>
        {
            Assert.StartsWith("Clip saved", content.Title);
            Assert.Equal(WindowGesture.ClipSavedClick, WindowRoutes.ClickOn(content));
        });
    }

    [Fact]
    public void The_other_words_about_a_clip_are_the_other_clip_click()
    {
        NotificationContent?[] all =
        [
            null,
            OnClip(Clip(rescued: true)),
            NotificationStates.Saving(TimeSpan.FromSeconds(30)),
            NotificationStates.NothingToSave(),
            NotificationStates.SaveFailed(),
            NotificationStates.StillSaving(),
            NotificationStates.LastClipGone(),
            NotificationStates.ClosingAfterClip(),
        ];

        Assert.All(all, content => Assert.Equal(WindowGesture.OtherClipClick, WindowRoutes.ClickOn(content)));
    }

    [Fact]
    public void The_words_about_the_state_of_the_capture_are_the_status_click()
    {
        NotificationContent[] all =
        [
            NotificationStates.CaptureOn(300, "Ctrl+Shift+F10"),
            NotificationStates.CaptureOn(300, null),
            NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", updatedTo: "1.2.3"),
            NotificationStates.Paused(),
            NotificationStates.NoDevice(),
            NotificationStates.DeviceChanged("Speakers"),
            NotificationStates.MicLost(),
            NotificationStates.MicBack(),
        ];

        Assert.All(all, content => Assert.Equal(WindowGesture.StatusClick, WindowRoutes.ClickOn(content)));
    }

    [Fact]
    public void Capture_on_with_a_refused_hotkey_is_the_refused_hotkey_click()
    {
        Assert.Equal(WindowGesture.HotkeyRefusedClick, WindowRoutes.ClickOn(NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", "Save hotkey is taken")));
        Assert.Equal(WindowGesture.HotkeyRefusedClick, WindowRoutes.ClickOn(NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", "Save hotkey is taken", updatedTo: "1.2.3")));
    }

    [Fact]
    public void A_problem_with_the_settings_is_the_settings_problem_click()
    {
        NotificationContent[] all =
        [
            NotificationStates.SettingsDamaged(copyKept: true),
            NotificationStates.SettingsUnreadable(),
            NotificationStates.SettingsHasError(),
            NotificationStates.SettingsNotSaved(),
            NotificationStates.StartupNotChanged(),
        ];

        Assert.All(all, content => Assert.Equal(WindowGesture.SettingsProblemClick, WindowRoutes.ClickOn(content)));
    }

    [Fact]
    public void An_update_notice_is_the_update_click_even_a_failed_one_that_is_also_a_problem()
    {
        NotificationContent[] all =
        [
            NotificationStates.UpdateReady("1.2.3", 0x112233),
            NotificationStates.UpdateAvailable("1.2.3", 0x112233),
            NotificationStates.UpdateFailed("1.2.3", willRetry: true),
        ];

        Assert.All(all, content => Assert.Equal(WindowGesture.UpdateClick, WindowRoutes.ClickOn(content)));
    }
}
