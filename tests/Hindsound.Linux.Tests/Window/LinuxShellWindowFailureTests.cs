using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Window;
using Hindsound.Settings;
using Hindsound.Unix.App;
using Hindsound.Unix.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// SDL loaded, but its video fails on this session: the shell gives no window and says why, and the app goes on. Over a
/// fake SDL, so these run on any system.
/// </summary>
/// <remarks>In the SDL collection: the count of open windows is the process's.</remarks>
[SupportedOSPlatform("linux")]
[Collection(SdlCollection.Name)]
public sealed class LinuxShellWindowFailureTests
{
    [Fact]
    public void Video_that_does_not_start_gives_no_window_and_one_line_in_the_log()
    {
        var video = new FakeVideo { StartFails = "x11 not available" };
        var lines = new List<string>();
        var windows = UnixSettingsWindow.OpenWindows;

        var window = LinuxShell.OpenOrNone(() => NewWindow(video), lines.Add);

        Assert.Null(window);
        Assert.Equal(["window: none, SDL video did not start: x11 not available"], lines);
        Assert.Equal(windows, UnixSettingsWindow.OpenWindows);
    }

    [Fact]
    public void A_window_SDL_cannot_make_is_closed_and_gives_no_window()
    {
        var video = new FakeVideo { CreateFails = "no compositor" };
        var lines = new List<string>();
        var windows = UnixSettingsWindow.OpenWindows;

        var window = LinuxShell.OpenOrNone(() => NewWindow(video), lines.Add);

        Assert.Null(window);
        Assert.Equal(["window: none, SDL made no window: no compositor"], lines);
        Assert.Equal(1, video.Closes);
        Assert.Equal(windows, UnixSettingsWindow.OpenWindows);
    }

    private static UnixSettingsWindow NewWindow(FakeVideo video)
    {
        var request = new SettingsWindowRequest(new AppSettings().Normalize(), new CountingBackend(), () => 0f, false, true, default, () => { });
        return new UnixSettingsWindow(request, new UnixLoop(() => video.Clock), () => new BlockText(), video);
    }
}
