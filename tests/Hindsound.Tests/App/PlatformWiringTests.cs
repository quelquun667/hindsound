using System.Reflection;
using Hindsound.App;
using Hindsound.Tests.Export;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The entry point installs the Windows layer, and the app asks the desktop parts it is given: nothing else fails if either
/// is left out, because the test assembly installs the layer itself and every other test hands its own fakes.
/// </summary>
[Collection("UiLoop")]
public class PlatformWiringTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    // Different monitor and work area, so a notice placed from the wrong screen, or with the wrong "covers", is another rectangle.
    private static readonly ScreenInfo Active = new(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 96);
    private static readonly ScreenInfo Cursor = new(new ScreenRect(1920, 0, 3840, 1080), new ScreenRect(1920, 0, 3840, 1040), 144);
    private static readonly ScreenInfo Clicked = new(new ScreenRect(-1280, 0, 0, 1024), new ScreenRect(-1280, 0, 0, 984), 120);

    [Fact]
    public void Main_installs_the_Windows_layer_before_it_reads_the_options()
    {
        var main = typeof(Program).GetMethod("Main", Hidden)!;
        var calls = PortableBoundaryTests.MembersInBody(main).OfType<MethodBase>().ToList();

        var install = calls.FindIndex(call => call.DeclaringType == typeof(WindowsPlatform) && call.Name == nameof(WindowsPlatform.Install));
        var parse = calls.FindIndex(call => call.DeclaringType == typeof(AppOptions) && call.Name == nameof(AppOptions.Parse));

        Assert.True(install >= 0, "Main must call WindowsPlatform.Install");
        Assert.True(parse >= 0, "Main must call AppOptions.Parse");
        Assert.True(install < parse, "the platform layer comes first: Parse and everything after it may need a real part");
    }

    [Fact]
    public void The_Windows_layer_fills_every_default_the_portable_code_reaches_for()
    {
        // The test assembly installs it as it loads; an Install that skips one leaves it null here.
        WindowsPlatform.Install();

        Assert.NotNull(PlatformDefaults.CaptureFactory);
        Assert.NotNull(PlatformDefaults.DeviceWatcher);
        Assert.NotNull(PlatformDefaults.ProcessSnapshot);
        Assert.NotNull(PlatformDefaults.RunKey);
        Assert.NotNull(PlatformDefaults.LaunchPath);
        Assert.NotNull(PlatformDefaults.Hotkeys);
        Assert.NotNull(PlatformDefaults.UpdateNetwork);
    }

    /// <summary>Answers from fixed values, and says which of the two <c>ForegroundCovers</c> was asked, and for which screen.</summary>
    private sealed class RecordingScreens(Func<ScreenInfo, bool> coversScreen, bool coversOwn) : IScreens
    {
        public List<string> Asked { get; } = [];

        public ForegroundInfo Foreground() => new("game.exe", false);

        public bool ForegroundCovers()
        {
            Asked.Add("covers()");
            return coversOwn;
        }

        public bool ForegroundCovers(ScreenInfo screen)
        {
            Asked.Add($"covers({screen.Dpi})");
            return coversScreen(screen);
        }

        public ScreenInfo? ActiveScreen()
        {
            Asked.Add("active");
            return Active;
        }

        public ScreenInfo? CursorScreen()
        {
            Asked.Add("cursor");
            return Cursor;
        }

        public ScreenInfo PrimaryScreen()
        {
            Asked.Add("primary");
            return Active;
        }
    }

    private sealed class FixedQuiet(UserNotificationState state) : IQuietState
    {
        public int Reads { get; private set; }

        public UserNotificationState Read()
        {
            Reads++;
            return state;
        }
    }

    private sealed class SilentSound : IConfirmSound
    {
        public bool Play() => false;
    }

    private sealed class NoOpener : IFileOpener
    {
        public string? Open(string target) => null;
    }

    private sealed class FixedMeter : IProcessMeter
    {
        public string Memory() => "memory";

        public double CpuMilliseconds() => 0;
    }

    private static HindsoundApp Built(TempFolder temp, IScreens screens, IQuietState quiet)
    {
        var options = AppOptions.Parse(["--settings", temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        return new HindsoundApp(options, new Journal(temp.Combine("settings.log")), desktop: new DesktopParts(screens, quiet, new SilentSound(), new NoOpener(), new FixedMeter()));
    }

    /// <summary>The line the app journals as it picks the area, before it reaches the window, which a built-only app does not have.</summary>
    private static string AreaLine(TempFolder temp, HindsoundApp app, ScreenInfo? clicked)
    {
        var present = typeof(HindsoundApp).GetMethod("PresentNow", Hidden)!;
        try
        {
            present.Invoke(app, [SampleClip.Notification(0xFF00FF00), false, clicked]);
        }
        catch (TargetInvocationException)
        {
            // The notification window is made when the loop runs; the journal line is written before it is reached.
        }

        return File.ReadAllLines(temp.Combine("settings.log")).Single(line => line.Contains("notification area:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_notice_for_a_clicked_screen_asks_whether_the_foreground_covers_that_screen()
    {
        using var temp = new TempFolder();
        var screens = new RecordingScreens(screen => screen == Clicked, coversOwn: false);
        var app = Built(temp, screens, new FixedQuiet(UserNotificationState.AcceptsNotifications));

        var line = AreaLine(temp, app, Clicked);

        Assert.Equal(["covers(120)"], screens.Asked);
        Assert.Contains("notification area: -1280,0,0,1024 at 120 dpi, foreground covers the screen True", line);
    }

    [Fact]
    public void A_notice_with_no_click_goes_to_the_screen_of_the_active_window_and_not_the_cursor()
    {
        using var temp = new TempFolder();
        var screens = new RecordingScreens(screen => screen == Active, coversOwn: false);
        var app = Built(temp, screens, new FixedQuiet(UserNotificationState.AcceptsNotifications));

        // Without a click, the app asks whether its hidden window is the active one: it needs the window, which only Run makes.
        using var loop = new UiLoop();
        typeof(HindsoundApp).GetField("_loop", Hidden)!.SetValue(app, loop);

        var line = AreaLine(temp, app, clicked: null);

        Assert.Equal(["active", "covers(96)"], screens.Asked);
        Assert.Contains("notification area: 0,0,1920,1080 at 96 dpi, foreground covers the screen True", line);
    }

    [Theory]
    [InlineData(UserNotificationState.RunningD3dFullScreen, false, true, false)]
    [InlineData(UserNotificationState.AcceptsNotifications, true, true, false)]
    [InlineData(UserNotificationState.AcceptsNotifications, false, false, false)]
    [InlineData(UserNotificationState.PresentationMode, false, false, true)]
    public void The_update_pass_reads_the_quiet_state_and_whether_the_foreground_covers_its_own_screen(
        UserNotificationState state, bool coversOwn, bool fullScreen, bool presentation)
    {
        using var temp = new TempFolder();

        // Asked for a screen, this one says the opposite, so the two questions cannot be mixed up.
        var screens = new RecordingScreens(_ => !coversOwn, coversOwn);
        var quiet = new FixedQuiet(state);
        var app = Built(temp, screens, quiet);

        var surface = ((IUpdateHost)app).Surface();

        Assert.Equal(fullScreen, surface.FullScreen);
        Assert.Equal(presentation, surface.Presentation);
        Assert.Equal(1, quiet.Reads);
        Assert.Equal(["covers()"], screens.Asked);
    }

    [Fact]
    public void The_portable_assembly_carries_the_version_of_the_exe()
    {
        // The updater compares UpdateWiring.InstalledVersion, read from the portable assembly, with the releases: a version set
        // on the exe project alone would leave it at 1.0.0 and no 0.x release would ever look newer.
        Assert.Equal(typeof(Program).Assembly.GetName().Version, typeof(UpdateWiring).Assembly.GetName().Version);
    }
}
