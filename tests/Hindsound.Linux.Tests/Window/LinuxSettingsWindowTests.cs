using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Window;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.App;

namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// The logic of the Linux window over a fake SDL: what it draws, where its events go, its timers, its scale and its end.
/// Nothing native runs, so these run on any system; the same window on SDL's offscreen driver is in the Linux tests.
/// </summary>
/// <remarks>In the SDL collection: the counts of open windows and bitmaps are the process's.</remarks>
[SupportedOSPlatform("linux")]
[Collection(SdlCollection.Name)]
public sealed unsafe class LinuxSettingsWindowTests
{
    [Fact]
    public void An_offscreen_window_is_the_design_size_on_a_made_up_screen_and_never_shown()
    {
        using var rig = new WindowRig();

        Assert.True(rig.Video.StartedOffscreen);
        Assert.Equal((760, 488), rig.Video.Created);
        Assert.Equal((760, 488), rig.Window.BitmapSize);
        Assert.Equal(100, rig.Window.AppliedZoom);
        Assert.False(rig.Window.IsForeground);
        Assert.NotNull(rig.Loop.Pump);
    }

    [Fact]
    public void Show_draws_the_page_into_the_bitmap_and_puts_all_of_it_on_the_window()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();

        Assert.Contains((0, 0, 760, 488), rig.Video.Presents);
        var bits = (uint*)rig.Window.Bitmap;
        var set = 0;
        for (var i = 0; i < 760 * 488; i += 97)
        {
            set += bits[i] != 0 ? 1 : 0;
        }

        Assert.True(set > 100, $"only {set} pixels were drawn");
    }

    [Fact]
    public void Many_asks_for_a_paint_make_one_paint_and_an_area_puts_only_its_pixels_on_the_window()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Window.Invalidate();
        rig.Window.Invalidate();
        rig.Drain();
        Assert.Single(rig.Video.Presents);

        rig.Window.InvalidateArea(new Box(10, 20, 30, 40));
        rig.Window.InvalidateArea(new Box(100, 20, 10, 10));
        rig.Drain();
        Assert.Equal((10, 20, 100, 40), rig.Video.Presents[^1]);
    }

    [Fact]
    public void A_click_on_a_row_of_the_side_bar_changes_the_page()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        var seen = new HashSet<Section> { rig.Window.Screen.Section };
        for (var y = SettingsScreen.BandHeight + 4; y < SettingsScreen.ViewBottom; y += 8)
        {
            rig.Click(SettingsScreen.NavWidth / 2, y);
            seen.Add(rig.Window.Screen.Section);
        }

        Assert.True(seen.Count >= 4, "pages seen: " + string.Join(", ", seen));
    }

    [Fact]
    public void A_key_the_probe_sends_goes_through_the_window_queue_to_the_page()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        var before = rig.Window.Screen.Focus?.Id;

        new LinuxSettingsProbe().Key(rig.Window, Vk.Tab);
        Assert.Single(rig.Video.Queue);
        rig.Drain();

        Assert.Empty(rig.Video.Queue);
        Assert.NotEqual(before, rig.Window.Screen.Focus?.Id);
    }

    [Fact]
    public void Text_typed_in_a_field_reaches_the_page_and_text_input_follows_the_field()
    {
        using var rig = new WindowRig(offscreen: false, setUp: v => v.Focused = true);
        rig.Window.Show();
        rig.Drain();
        rig.Window.Screen.ShowSection(Section.Export);
        rig.Drain();
        for (var i = 0; i < 40 && !rig.Window.Field.Visible; i++)
        {
            rig.Window.Screen.Key(Vk.Tab, 0);
            rig.Drain();
        }

        Assert.True(rig.Window.Field.Visible, "no text field on the Export page");
        Assert.True(rig.Window.Field.Focused);
        Assert.True(rig.Video.TextInputOn);

        rig.Send(rig.TextInput("clip"));
        rig.Drain();
        Assert.Equal("clip", rig.Window.Field.Text);
        Assert.NotEmpty(rig.Backend.Changes);

        rig.Send(rig.WindowEvent(SdlNative.EventWindowFocusLost));
        Assert.False(rig.Window.Field.Focused);
        Assert.False(rig.Video.TextInputOn);
    }

    [Fact]
    public void The_meter_and_the_wave_tick_every_100_ms_on_the_loop_until_they_are_not_wanted()
    {
        using var rig = new WindowRig();
        rig.Window.MeterWanted(true);
        rig.Window.WaveWanted(true);
        rig.RunFor(350);
        Assert.Equal(3, rig.MicReads);

        rig.Window.MeterWanted(false);
        rig.RunFor(500);
        Assert.Equal(3, rig.MicReads);

        rig.Window.MeterWanted(true);
        rig.Window.MeterWanted(false);
        rig.Window.MeterWanted(true);
        rig.RunFor(250);
        Assert.Equal(5, rig.MicReads);
    }

    [Fact]
    public void A_window_that_fails_to_make_its_pump_leaves_no_window_bitmap_nor_video()
    {
        var video = new FakeVideo { NewPumpFails = new InvalidOperationException("no event type left") };
        using var loop = new UnixLoop(() => video.Clock);
        var windows = LinuxSettingsWindow.OpenWindows;
        var bitmaps = LinuxSettingsWindow.LiveBitmaps;
        var closed = 0;

        var error = Assert.Throws<InvalidOperationException>(() => OpenOn(video, loop, () => closed++));

        Assert.Equal("no event type left", error.Message);
        Assert.Equal(1, video.Closes);
        Assert.Null(loop.Pump);
        Assert.Equal(windows, LinuxSettingsWindow.OpenWindows);
        Assert.Equal(bitmaps, LinuxSettingsWindow.LiveBitmaps);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void A_window_that_fails_after_taking_the_loops_wait_gives_it_back()
    {
        var video = new FakeVideo();
        using var loop = new UnixLoop(() => video.Clock);

        // The wait the loop had before throws when the window's pump replaces it: the window has taken the loop's wait by then.
        loop.Pump = new FailingWakePump();
        var windows = LinuxSettingsWindow.OpenWindows;
        var bitmaps = LinuxSettingsWindow.LiveBitmaps;

        Assert.Throws<InvalidOperationException>(() => OpenOn(video, loop, () => { }));

        Assert.Null(loop.Pump);
        Assert.Equal(1, video.Closes);
        Assert.Equal(windows, LinuxSettingsWindow.OpenWindows);
        Assert.Equal(bitmaps, LinuxSettingsWindow.LiveBitmaps);
    }

    private static LinuxSettingsWindow OpenOn(FakeVideo video, UnixLoop loop, Action closed)
    {
        var request = new SettingsWindowRequest(new AppSettings().Normalize(), new CountingBackend(), () => 0f, false, true, default, closed);
        return new LinuxSettingsWindow(request, loop, () => new BlockText(), video);
    }

    private sealed class FailingWakePump : ILoopPump
    {
        public void Wait(int timeoutMs)
        {
        }

        public void Wake() => throw new InvalidOperationException("the old wait failed to wake");
    }

    [Fact]
    public void A_new_scale_makes_a_bitmap_at_that_scale_and_draws_the_page_again()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        var first = rig.Window.Bitmap;
        var bitmaps = LinuxSettingsWindow.LiveBitmaps;

        rig.Window.ProbeScaleChange(192);
        rig.Drain();

        Assert.Equal((1520, 976), rig.Window.BitmapSize);
        Assert.Equal(1520, rig.Video.Places[^1].W);
        Assert.Equal((0, 0, 1520, 976), rig.Video.Presents[^1]);
        Assert.NotEqual(first, rig.Window.Bitmap);
        Assert.Equal(bitmaps, LinuxSettingsWindow.LiveBitmaps);
        Assert.Equal(2f, rig.Window.Scale);
    }

    [Fact]
    public void On_a_screen_1024_wide_the_window_takes_the_largest_zoom_that_fits_and_its_mouse_follows_the_density()
    {
        using var rig = new WindowRig(offscreen: false, zoom: 140, setUp: v =>
        {
            v.Area = new ScreenRect(0, 0, 1024, 740);
            v.FrameSize = new WindowBorders(30, 0, 0, 0);
        });

        Assert.Equal(120, rig.Window.AppliedZoom);
        Assert.Equal((912, 586), rig.Window.BitmapSize);
        var bounds = rig.Video.Bounds;
        Assert.True(bounds.X >= 0 && bounds.X + bounds.W <= 1024 && bounds.Y >= 30 && bounds.Y + bounds.H <= 740, bounds.ToString());
    }

    [Fact]
    public void A_title_bar_that_X11_reports_only_once_the_window_is_shown_makes_the_zoom_and_the_place_fit_again()
    {
        using var rig = new WindowRig(offscreen: false, zoom: 140, setUp: v => v.Area = new ScreenRect(0, 0, 1920, 700));
        Assert.Equal(140, rig.Window.AppliedZoom);

        rig.Video.FrameSize = new WindowBorders(30, 0, 0, 0);
        rig.Send(rig.WindowEvent(SdlNative.EventWindowShown));
        rig.Drain();

        Assert.Equal(120, rig.Window.AppliedZoom);
        var bounds = rig.Video.Bounds;
        Assert.True(bounds.X >= 0 && bounds.X + bounds.W <= 1920 && bounds.Y >= 30 && bounds.Y + bounds.H <= 700, bounds.ToString());
    }

    [Fact]
    public void A_wayland_output_at_twice_the_density_gets_a_window_of_half_the_pixels()
    {
        using var rig = new WindowRig(offscreen: false, setUp: v =>
        {
            v.Scale = 2;
            v.Density = 2;
        });

        Assert.Equal((1520, 976), rig.Window.BitmapSize);
        Assert.Equal((760, 488), rig.Video.Bounds is var b ? (b.W, b.H) : default);
        Assert.Equal("1520x976, bitmap 1520x976", rig.Window.ClientSizeText());
    }

    [Fact]
    public void Close_ends_the_window_once_frees_its_bitmap_and_gives_the_loop_back_and_a_new_one_opens_after()
    {
        var windows = LinuxSettingsWindow.OpenWindows;
        var bitmaps = LinuxSettingsWindow.LiveBitmaps;
        var texts = BlockText.Disposed;
        using (var rig = new WindowRig())
        {
            Assert.Equal(windows + 1, LinuxSettingsWindow.OpenWindows);
            Assert.Equal(bitmaps + 1, LinuxSettingsWindow.LiveBitmaps);
            rig.Window.Show();
            rig.Window.MeterWanted(true);
            rig.Send(rig.WindowEvent(SdlNative.EventWindowCloseRequested));
            Assert.False(rig.Window.IsClosed);
            rig.Drain();

            Assert.True(rig.Window.IsClosed);
            Assert.Equal(1, rig.ClosedCount);
            Assert.Equal(1, rig.Video.Closes);
            Assert.Null(rig.Loop.Pump);
            Assert.Equal(0, rig.Window.Bitmap);
            Assert.Equal(windows, LinuxSettingsWindow.OpenWindows);
            Assert.Equal(bitmaps, LinuxSettingsWindow.LiveBitmaps);
            Assert.True(BlockText.Disposed > texts);

            rig.Window.Close();
            rig.Window.Dispose();
            rig.Video.Clock += 1000;
            rig.Drain();
            Assert.Equal(1, rig.ClosedCount);
            Assert.Equal(0, rig.MicReads);
        }

        using var again = new WindowRig();
        again.Window.Show();
        again.Drain();
        Assert.Equal(windows + 1, LinuxSettingsWindow.OpenWindows);
        Assert.NotEmpty(again.Video.Presents);
    }

    [Fact]
    public void The_probe_picture_is_the_bitmap_the_window_shows()
    {
        var folder = Directory.CreateTempSubdirectory("hindsound-window-");
        try
        {
            using var rig = new WindowRig();
            rig.Window.Show();
            var path = Path.Combine(folder.FullName, "window.png");
            rig.Window.ProbePicture(path);

            var png = File.ReadAllBytes(path);
            Assert.Equal(0x89, png[0]);
            Assert.Equal(760, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
            Assert.Equal(488, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_icon_is_drawn_at_the_display_scale_and_again_after_a_new_scale()
    {
        using var rig = new WindowRig();
        rig.Window.SetIcon(0x22C55E);
        Assert.Equal("(64, 64)", new LinuxSettingsProbe().IconWidths(rig.Window));

        rig.Window.ProbeScaleChange(144);
        Assert.Equal(2, rig.Video.Icons);
        Assert.Equal(96, rig.Window.IconSideNow);
    }

    [Fact]
    public void Focus_lost_deactivates_the_page_and_focus_gained_says_the_window_came_back()
    {
        using var rig = new WindowRig();
        var activated = 0;
        rig.Window.Activated = () => activated++;
        rig.Window.CaptureMouse();

        new LinuxSettingsProbe().Activate(rig.Window);
        rig.Drain();
        Assert.Equal(1, activated);

        rig.Send(rig.WindowEvent(SdlNative.EventWindowFocusLost));
        Assert.False(rig.Window.Field.Focused);
    }

    [Fact]
    public void While_the_folder_dialog_is_open_the_window_takes_no_input_but_still_paints()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        var section = rig.Window.Screen.Section;
        rig.Video.Folder = "/home/user/clips";
        rig.Video.Queue.Enqueue(rig.Button(SdlNative.EventMouseButtonDown, SettingsScreen.NavWidth / 2, SettingsScreen.ViewBottom - 20));
        rig.Video.Queue.Enqueue(rig.WindowEvent(SdlNative.EventWindowExposed));

        Assert.Equal("/home/user/clips", rig.Window.PickFolder("/home/user"));
        rig.Drain();
        Assert.Equal(section, rig.Window.Screen.Section);
        Assert.Contains((0, 0, 760, 488), rig.Video.Presents.Skip(1));
    }

    [Fact]
    public void While_the_folder_dialog_is_open_the_work_posted_to_the_loop_runs()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        var presents = rig.Video.Presents.Count;
        var ran = false;
        rig.Video.Folder = "/home/user/clips";

        // What a hotkey, the tray or a second launch posts while the dialog is open, and a paint asked before it.
        rig.Loop.Post(() => ran = true);
        rig.Window.Invalidate();
        rig.Window.PickFolder("/home/user");

        Assert.True(ran, "a posted action waited for the dialog to close");
        Assert.True(rig.Video.Presents.Count > presents, "the window did not paint while the dialog was open");
    }

    [Fact]
    public void A_close_asked_while_the_folder_dialog_is_open_waits_for_its_answer()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        rig.Video.Folder = "/home/user/clips";
        rig.Window.Close();

        Assert.Equal("/home/user/clips", rig.Window.PickFolder("/home/user"));
        Assert.False(rig.Window.IsClosed, "the window went under the dialog");

        rig.Drain();
        Assert.True(rig.Window.IsClosed);
        Assert.Equal(1, rig.Video.Closes);
    }

    [Fact]
    public void A_window_the_desktop_hides_without_minimizing_it_stops_painting_until_it_is_exposed()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();

        rig.Send(rig.WindowEvent(SdlNative.EventWindowOccluded));
        Assert.True(rig.Window.Screen.Minimized);
        var presents = rig.Video.Presents.Count;
        rig.Window.Invalidate();
        rig.Drain();
        Assert.Equal(presents, rig.Video.Presents.Count);

        rig.Send(rig.WindowEvent(SdlNative.EventWindowExposed));
        rig.Drain();
        Assert.False(rig.Window.Screen.Minimized);
        Assert.True(rig.Video.Presents.Count > presents);
    }

    [Fact]
    public void An_exposed_window_that_was_minimized_by_the_app_is_still_minimized()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();

        rig.Send(rig.WindowEvent(SdlNative.EventWindowMinimized));
        rig.Send(rig.WindowEvent(SdlNative.EventWindowExposed));

        Assert.True(rig.Window.Screen.Minimized);
    }

    [Fact]
    public void Events_of_another_window_are_not_ours()
    {
        using var rig = new WindowRig();
        rig.Window.Show();
        rig.Drain();
        var e = rig.WindowEvent(SdlNative.EventWindowCloseRequested);
        e.Window.WindowId = 99;
        rig.Send(e);
        rig.Drain();
        Assert.False(rig.Window.IsClosed);
    }
}
