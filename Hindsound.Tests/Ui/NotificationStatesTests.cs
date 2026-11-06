using Hindsound.Export;
using Hindsound.Settings;
using Hindsound.Ui;
using K = Hindsound.Ui.NotificationKind;
using P = Hindsound.Ui.NotificationPalette;

namespace Hindsound.Tests.Ui;

public class NotificationStatesTests
{
    private const uint Accent = 0x35D6FF;
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);
    private static readonly float[] Loud = Enumerable.Repeat(0.8f, 24).ToArray();

    private static ClipResult Clip(double seconds = 30, bool silent = false, bool rescued = false, bool wav = false, float[]? bars = null) =>
        new(["clip.mp3"], TimeSpan.FromSeconds(seconds), 1, silent, bars ?? Loud, rescued, wav);

    private static void AssertState(NotificationContent content, K kind, string title, string body, uint stripe, NotificationVisual visual, params string[] keys)
    {
        Assert.Equal(kind, content.Kind);
        Assert.Equal(title, content.Title);
        Assert.Equal(body, content.Body);
        Assert.Equal(stripe, content.Stripe);
        Assert.Equal(visual, content.Visual);
        Assert.Equal(keys, content.Keys);
    }

    [Fact]
    public void Settings_damaged() =>
        AssertState(NotificationStates.SettingsDamaged(copyKept: true), K.SettingsDamaged, "Settings file was damaged", "Defaults restored, mic off. Copy kept as .bak", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Settings_damaged_without_a_copy() =>
        AssertState(NotificationStates.SettingsDamaged(copyKept: false), K.SettingsDamaged, "Settings file was damaged", "Defaults restored, mic off", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Settings_unreadable() =>
        AssertState(NotificationStates.SettingsUnreadable(), K.SettingsUnreadable, "Can't read the settings file", "Using defaults, mic off, until it can be read", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Settings_has_an_error() =>
        AssertState(NotificationStates.SettingsHasError(), K.SettingsHasError, "Settings file has an error", "Keeping the current settings", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Settings_not_saved() =>
        AssertState(NotificationStates.SettingsNotSaved(), K.SettingsNotSaved, "Couldn't save your settings", "This change is lost at the next start", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Startup_not_changed() =>
        AssertState(NotificationStates.StartupNotChanged(), K.StartupNotChanged, "Couldn't change Start with Windows", "It stays as it was", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Still_saving() =>
        AssertState(NotificationStates.StillSaving(), K.StillSaving, "Still saving your last clip", "Try again in a moment", P.Amber, NotificationVisual.Triangle);

    [Fact]
    public void Saved_and_copied() =>
        AssertState(NotificationStates.ForClip(Clip(), Thirty, true, true, Accent), K.Saved, "Clip saved (0:30)", "Copied. Paste", Accent, NotificationVisual.Bars, "Ctrl", "V");

    [Fact]
    public void Buffer_not_full() =>
        AssertState(NotificationStates.ForClip(Clip(12), Thirty, true, true, Accent), K.Partial, "Clip saved (0:12 of 0:30)", "Copied. Paste", P.Amber, NotificationVisual.Bars, "Ctrl", "V");

    [Fact]
    public void Silent() =>
        AssertState(NotificationStates.ForClip(Clip(silent: true), Thirty, true, true, Accent), K.Silent, "Clip saved (0:30)", "It's silent. Check Settings > Audio.", P.Amber, NotificationVisual.Bars);

    [Fact]
    public void Copy_failed() =>
        AssertState(NotificationStates.ForClip(Clip(), Thirty, true, false, Accent), K.CopyFailed, "Clip saved (0:30)", "Couldn't copy. Use tray > Copy last clip.", P.Amber, NotificationVisual.Triangle);

    [Fact]
    public void Rescued_wins_over_everything_else() =>
        AssertState(NotificationStates.ForClip(Clip(12, silent: true, rescued: true), Thirty, true, true, Accent), K.Rescued, "Output folder not writable", @"Clip kept in Hindsound\Rescued", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Rescued_and_not_copied_says_both() =>
        AssertState(NotificationStates.ForClip(Clip(rescued: true), Thirty, true, false, Accent), K.Rescued, "Output folder not writable", @"Kept in Hindsound\Rescued. Not copied", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Rescued_with_no_copy_asked_claims_no_failure() =>
        AssertState(NotificationStates.ForClip(Clip(rescued: true), Thirty, false, false, Accent), K.Rescued, "Output folder not writable", @"Clip kept in Hindsound\Rescued", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Silent_and_not_copied_says_both() =>
        AssertState(NotificationStates.ForClip(Clip(silent: true), Thirty, true, false, Accent), K.Silent, "Clip saved (0:30)", "It's silent, and not copied", P.Amber, NotificationVisual.Bars);

    [Fact]
    public void Silent_with_no_copy_asked_claims_no_failure() =>
        AssertState(NotificationStates.ForClip(Clip(silent: true), Thirty, false, false, Accent), K.Silent, "Clip saved (0:30)", "It's silent. Check Settings > Audio.", P.Amber, NotificationVisual.Bars);

    [Fact]
    public void No_copy_asked_never_claims_a_copy()
    {
        var content = NotificationStates.ForClip(Clip(), Thirty, false, false, Accent);
        Assert.Equal(K.NotCopied, content.Kind);
        Assert.DoesNotContain("Copied", content.Body);
        Assert.Empty(content.Keys);
    }

    [Fact]
    public void A_wav_instead_of_an_mp3_is_told()
    {
        var content = NotificationStates.ForClip(Clip(wav: true), Thirty, true, true, Accent);
        Assert.Equal(K.WavFallback, content.Kind);
        Assert.Contains("WAV", content.Body);
        Assert.Equal(P.Amber, content.Stripe);
    }

    [Fact]
    public void No_device() =>
        AssertState(NotificationStates.NoDevice(), K.NoDevice, "No audio device", "Nothing is being captured", P.Hot, NotificationVisual.Triangle);

    [Fact]
    public void Paused() =>
        AssertState(NotificationStates.Paused(), K.Paused, "Capture paused", "Nothing new is being recorded", P.Muted, NotificationVisual.Pause);

    [Fact]
    public void Capture_on() =>
        AssertState(NotificationStates.CaptureOn(300, "Ctrl+Shift+F10"), K.CaptureOn, "Capture on", "Keeping the last 5:00. Save:", P.Hot, NotificationVisual.Dot, "Ctrl", "Shift", "F10");

    [Fact]
    public void Capture_on_follows_the_settings()
    {
        var content = NotificationStates.CaptureOn(90, "Alt+F9");
        Assert.Equal("Keeping the last 1:30. Save:", content.Body);
        Assert.Equal(new[] { "Alt", "F9" }, content.Keys);
    }

    [Fact]
    public void Device_changed() =>
        AssertState(NotificationStates.DeviceChanged("Headset Earphone"), K.DeviceChanged, "Audio device changed", "Now capturing Headset Earphone", P.Muted, NotificationVisual.Headset);

    [Theory]
    [InlineData(29.98, "Clip saved (0:30)", K.Saved)]
    [InlineData(30.01, "Clip saved (0:30)", K.Saved)]
    [InlineData(29.4, "Clip saved (0:29 of 0:30)", K.Partial)]
    [InlineData(0.6, "Clip saved (0:01 of 0:30)", K.Partial)]
    public void A_few_milliseconds_short_is_not_partial(double seconds, string title, K kind)
    {
        var content = NotificationStates.ForClip(Clip(seconds), Thirty, true, true, Accent);
        Assert.Equal(title, content.Title);
        Assert.Equal(kind, content.Kind);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9, "0:09")]
    [InlineData(60, "1:00")]
    [InlineData(300, "5:00")]
    [InlineData(1800, "30:00")]
    public void Durations_read_as_minutes_and_seconds(int seconds, string expected)
    {
        Assert.Equal(expected, NotificationStates.FormatDuration(seconds));
    }

    [Fact]
    public void Lime_is_the_default_accent_of_the_settings_and_of_the_palette()
    {
        Assert.True(AccentColors.TryParseHex(AccentColors.LimeHex, out var r, out var g, out var b));
        Assert.Equal(P.Lime, ((uint)r << 16) | ((uint)g << 8) | b);
    }

    [Fact]
    public void Grey_has_no_glow_and_colours_do()
    {
        Assert.False(NotificationStates.Paused().StripeGlows);
        Assert.True(NotificationStates.NoDevice().StripeGlows);
    }

    [Fact]
    public void Only_three_keys_are_small()
    {
        Assert.True(NotificationStates.CaptureOn(300, "Ctrl+Shift+F10").SmallKeys);
        Assert.False(NotificationStates.ForClip(Clip(), Thirty, true, true, Accent).SmallKeys);
    }
}

public class BarLayoutTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(0f, 2)]
    [InlineData(0.01f, 2)]
    [InlineData(0.5f, 20)]
    [InlineData(1f, 40)]
    [InlineData(2f, 40)]
    [InlineData(float.NaN, 2)]
    public void Heights_are_even_and_stay_in_the_box(float value, int expected)
    {
        Assert.Equal(expected, BarLayout.HeightOf(value));
    }

    [Fact]
    public void A_full_clip_draws_its_real_bars()
    {
        var bars = Enumerable.Range(0, 24).Select(i => i / 23f).ToArray();

        var columns = BarLayout.Arrange(bars, Thirty, Thirty, false);

        Assert.Equal(24, columns.Length);
        Assert.All(columns, c => Assert.True(c.Active));
        Assert.Equal(bars.Select(BarLayout.HeightOf), columns.Select(c => c.Height));
    }

    [Fact]
    public void A_partial_clip_sits_on_the_right_at_its_share_of_the_width()
    {
        var bars = Enumerable.Repeat(0.5f, 24).ToArray();

        var columns = BarLayout.Arrange(bars, TimeSpan.FromSeconds(12), Thirty, false);

        Assert.Equal(14, columns.Count(c => !c.Active));
        Assert.All(columns.Take(14), c => Assert.Equal(new BarColumn(2, false), c));
        Assert.All(columns.Skip(14), c => Assert.Equal(new BarColumn(20, true), c));
    }

    [Fact]
    public void A_partial_clip_keeps_its_loudest_moment()
    {
        var bars = new float[24];
        bars[5] = 1f;

        var columns = BarLayout.Arrange(bars, TimeSpan.FromSeconds(10), Thirty, false);

        Assert.Equal(40, columns.Max(c => c.Height));
    }

    [Fact]
    public void A_very_short_clip_still_shows_a_bar()
    {
        var columns = BarLayout.Arrange(Enumerable.Repeat(0.5f, 24).ToArray(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(300), false);
        Assert.Equal(1, columns.Count(c => c.Active));
    }

    [Fact]
    public void A_silent_clip_is_flat_and_faint()
    {
        var columns = BarLayout.Arrange(Enumerable.Repeat(0.5f, 24).ToArray(), Thirty, Thirty, true);
        Assert.All(columns, c => Assert.Equal(new BarColumn(2, false), c));
    }
}

public class NotificationBitmapTests
{
    private const uint Accent = 0xD4F53C;
    private static readonly float[] Loud = Enumerable.Repeat(0.8f, 24).ToArray();

    private static NotificationContent Saved() =>
        NotificationStates.ForClip(new ClipResult(["c"], TimeSpan.FromSeconds(30), 1, false, Loud, false), TimeSpan.FromSeconds(30), true, true, Accent);

    public static TheoryData<int> Scales => new() { 96, 120, 144, 192 };

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_tab_has_the_size_of_the_plan_at_every_scale(int dpi)
    {
        using var bitmap = new NotificationBitmap(Saved(), dpi / 96.0, Accent, false);
        Assert.Equal((int)Math.Round(360 * dpi / 96.0), bitmap.Width);
        Assert.Equal((int)Math.Round(64 * dpi / 96.0), bitmap.Height);
    }

    [Fact]
    public void The_stripe_is_on_the_edge_side_and_in_the_colour_of_the_state()
    {
        using var saved = new NotificationBitmap(Saved(), 1, Accent, false);
        Assert.Equal(0xFF000000 | Accent, saved.PixelAt(saved.Width - 1, 30));

        using var error = new NotificationBitmap(NotificationStates.NoDevice(), 1, Accent, false);
        Assert.Equal(0xFF000000 | NotificationPalette.Hot, error.PixelAt(error.Width - 1, 30));
    }

    [Fact]
    public void The_cut_corner_is_transparent_on_the_inner_side()
    {
        using var bitmap = new NotificationBitmap(Saved(), 1, Accent, false);
        Assert.Equal(0u, bitmap.PixelAt(0, 0) >> 24);
        Assert.Equal(255u, bitmap.PixelAt(0, 40) >> 24);
    }

    [Fact]
    public void On_the_left_edge_the_tab_is_mirrored()
    {
        using var bitmap = new NotificationBitmap(Saved(), 1, Accent, true);
        Assert.Equal(0xFF000000 | Accent, bitmap.PixelAt(0, 30));
        Assert.Equal(0u, bitmap.PixelAt(bitmap.Width - 1, 0) >> 24);
    }

    [Theory]
    [InlineData(false, 14, 122)]
    [InlineData(true, 250, 18)]
    public void The_layout_follows_the_plan_for_bars(bool mirrored, float visual, float text)
    {
        Assert.Equal(visual, NotificationBitmap.VisualLeft(Saved(), mirrored));
        Assert.Equal(text, NotificationBitmap.TextLeft(Saved(), mirrored));
        Assert.Equal(220, NotificationBitmap.TextWidth(Saved()));
    }

    [Fact]
    public void The_layout_gives_the_text_more_room_beside_an_icon()
    {
        var content = NotificationStates.Paused();
        Assert.Equal(50, NotificationBitmap.TextLeft(content, false));
        Assert.Equal(292, NotificationBitmap.TextWidth(content));
    }

    [Fact]
    public void The_bars_are_in_the_accent_colour_given()
    {
        const uint violet = 0xB79CFF;
        var content = NotificationStates.ForClip(new ClipResult(["c"], TimeSpan.FromSeconds(30), 1, false, Loud, false), TimeSpan.FromSeconds(30), true, true, violet);
        using var bitmap = new NotificationBitmap(content, 1, violet, false);
        bitmap.SetBarsScale(1);

        Assert.Equal(0xFF000000 | violet, bitmap.PixelAt(15, 32));
    }

    [Fact]
    public void The_text_never_runs_under_the_stripe()
    {
        var content = NotificationStates.DeviceChanged(new string('W', 200));
        using var bitmap = new NotificationBitmap(content, 1, Accent, false);

        // The margin before the stripe holds nothing but the fill.
        for (var y = 34; y < 54; y++)
        {
            Assert.Equal(0xFF000000 | NotificationPalette.Fill, bitmap.PixelAt(bitmap.Width - 10, y));
        }
    }

    [Fact]
    public void Each_state_draws_something_different_where_the_icon_goes()
    {
        var kinds = new[]
        {
            NotificationVisual.Triangle, NotificationVisual.Pause, NotificationVisual.Dot, NotificationVisual.Headset, NotificationVisual.Pending,
        };
        var shapes = kinds.Select(k => string.Concat(Enumerable.Range(0, 576).Select(i => NotificationIcons.Distance(k, i % 24 + 0.5f, i / 24 + 0.5f) < 0 ? '#' : '.'))).ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
        Assert.All(shapes, s => Assert.Contains('#', s));
    }
}

public class TrayIconShapeTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void Active_pause_and_error_differ_by_shape(int size)
    {
        var active = TrayIconShape.Coverage(TrayState.Active, size);
        var paused = TrayIconShape.Coverage(TrayState.Paused, size);
        var error = TrayIconShape.Coverage(TrayState.Error, size);

        Assert.NotEqual(active, paused);
        Assert.NotEqual(active, error);
        Assert.NotEqual(paused, error);
        Assert.All(new[] { active, paused, error }, shape => Assert.Contains(shape, c => c > 0.9f));
    }

    [Fact]
    public void The_active_icon_is_an_H_in_three_bars()
    {
        var shape = TrayIconShape.Coverage(TrayState.Active, 16);
        Assert.Equal(1f, shape[0]);
        Assert.Equal(1f, shape[15]);
        Assert.Equal(0f, shape[8]);
        Assert.Equal(1f, shape[7 * 16 + 8]);
    }

    [Fact]
    public void The_pause_icon_has_no_crossbar()
    {
        var shape = TrayIconShape.Coverage(TrayState.Paused, 16);
        Assert.Equal(1f, shape[0]);
        Assert.Equal(0f, shape[7 * 16 + 8]);
    }

    [Theory]
    [InlineData(TrayState.Active, 0x123456u)]
    [InlineData(TrayState.Paused, NotificationPalette.Muted)]
    [InlineData(TrayState.Error, NotificationPalette.Hot)]
    public void Only_the_active_icon_takes_the_accent(TrayState state, uint expected)
    {
        Assert.Equal(expected, TrayIconShape.ColorOf(state, 0x123456));
    }
}

public class ClipboardFilesTests
{
    [Fact]
    public void The_drop_list_is_wide_and_double_null_terminated()
    {
        var bytes = ClipboardFiles.BuildDropFiles([@"C:\a.mp3", @"C:\b.mp3"]);

        Assert.Equal(20, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(1, BitConverter.ToInt32(bytes, 16));
        Assert.Equal("C:\\a.mp3\0C:\\b.mp3\0\0", System.Text.Encoding.Unicode.GetString(bytes, 20, bytes.Length - 20));
    }

    [Fact]
    public void It_tries_five_times_50_ms_apart_then_gives_up()
    {
        var attempts = 0;
        var delays = new List<int>();
        bool? result = null;

        ClipboardFiles.CopyWithRetries(() => { attempts++; return false; }, (ms, next) => { delays.Add(ms); next(); }, done => result = done);

        Assert.Equal(5, attempts);
        Assert.Equal(new[] { 50, 50, 50, 50 }, delays);
        Assert.False(result);
    }

    [Fact]
    public void It_stops_at_the_first_success()
    {
        var attempts = 0;
        bool? result = null;

        ClipboardFiles.CopyWithRetries(() => ++attempts == 3, (_, next) => next(), done => result = done);

        Assert.Equal(3, attempts);
        Assert.True(result);
    }
}

public class ConfirmSoundTests
{
    [Fact]
    public void The_sound_is_a_140_ms_mono_wav_that_never_clips()
    {
        var wav = ConfirmSound.BuildWav();

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(48000, BitConverter.ToInt32(wav, 24));
        var dataBytes = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataBytes);
        Assert.Equal(140, dataBytes / 2 * 1000 / 48000);
        var peak = Enumerable.Range(0, dataBytes / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(wav, 44 + 2 * i)));
        Assert.InRange(peak, 3000, 4200);
    }
}

public class ForegroundWindowTests
{
    private static Native.RECT Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]
    [InlineData(-8, -8, 1928, 1088, true)]
    [InlineData(0, 0, 1920, 1040, false)]
    [InlineData(100, 100, 500, 500, false)]
    public void Only_the_whole_screen_counts(int l, int t, int r, int b, bool expected)
    {
        Assert.Equal(expected, ForegroundWindow.RectCovers(Rect(l, t, r, b), Rect(0, 0, 1920, 1080)));
    }

    [Theory]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("UnityWndClass", false)]
    public void The_desktop_is_not_a_game(string className, bool expected)
    {
        Assert.Equal(expected, ForegroundWindow.IsDesktopClass(className));
    }
}
