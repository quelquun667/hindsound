using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Window;
using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// The settings window on SDL's offscreen driver, on a real Linux with no display: it opens, draws, takes a click and a key
/// from its own queue, writes the probe's picture, closes with its bitmap freed and SDL's video stopped, and opens again.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(SdlCollection.Name)]
public sealed unsafe class LinuxSettingsWindowOnLinuxTests
{
    private const int GuardMs = 10_000;

    // Runs the loop until what is queued and posted now is done, with a guard against a loop that never ends.
    private static void Turn(LinuxLoop loop)
    {
        var guarded = false;
        using var guard = new Timer(_ =>
        {
            guarded = true;
            loop.Quit();
        }, null, GuardMs, Timeout.Infinite);
        loop.After(50, loop.Quit);
        loop.Run();
        Assert.False(guarded, "the loop had to be stopped by the guard");
    }

    private static LinuxSettingsWindow Open(LinuxLoop loop, Action closed)
    {
        // The first size follows the zoom of the settings, 120 % by default as on Windows: the test states 100 %, the design size.
        var settings = new AppSettings().Normalize();
        settings.General.WindowZoom = 100;
        var request = new SettingsWindowRequest(settings, new CountingBackend(), () => 0f, false, true, default, closed);
        return LinuxSettingsWindow.Open(request, loop, () => new BlockText());
    }

    private static void PushClick(LinuxSettingsWindow window, uint windowId, float x, float y)
    {
        foreach (var type in new[] { SdlNative.EventMouseButtonDown, SdlNative.EventMouseButtonUp })
        {
            var e = default(SdlNative.Event);
            e.Button.Type = type;
            e.Button.WindowId = windowId;
            e.Button.Button = SdlNative.ButtonLeft;
            e.Button.Clicks = 1;
            e.Button.X = x * window.Scale;
            e.Button.Y = y * window.Scale;
            Assert.True(SdlNative.PushEvent(&e), SdlNative.Error());
        }
    }

    [Fact]
    public void The_window_opens_draws_takes_a_click_and_a_key_writes_its_picture_and_closes_with_nothing_left()
    {
        Assert.True(SdlLibrary.TryLoad(), SdlLibrary.LastError);
        var bitmaps = LinuxSettingsWindow.LiveBitmaps;
        var folder = Directory.CreateTempSubdirectory("hindsound-window-");
        using var loop = new LinuxLoop();
        try
        {
            var closed = 0;
            var window = Open(loop, () => closed++);
            Assert.NotEqual(0u, SdlNative.WasInit(SdlNative.InitVideo));
            Assert.NotNull(loop.Pump);
            window.SetIcon(0x22C55E);
            window.Show();
            Turn(loop);
            Assert.True((760, 488) == window.BitmapSize, $"bitmap {window.BitmapSize}, zoom {window.AppliedZoom}, scale {window.Scale}");
            Assert.Equal("760x488, bitmap 760x488", window.ClientSizeText());

            var path = Path.Combine(folder.FullName, "probe.png");
            window.ProbePicture(path);
            Assert.True(new FileInfo(path).Length > 1000);

            var seen = new HashSet<Section> { window.Screen.Section };
            for (var y = SettingsScreen.BandHeight + 4; y < SettingsScreen.ViewBottom; y += 24)
            {
                window.PushWindowEvent(SdlNative.EventWindowExposed);
                PushClick(window, IdOf(window), SettingsScreen.NavWidth / 2, y);
                Turn(loop);
                seen.Add(window.Screen.Section);
            }

            Assert.True(seen.Count >= 3, "pages seen: " + string.Join(", ", seen));

            var focus = window.Screen.Focus?.Id;
            new LinuxSettingsProbe().Key(window, Vk.Tab);
            Turn(loop);
            Assert.NotEqual(focus, window.Screen.Focus?.Id);

            window.Close();
            Turn(loop);
            Assert.Equal(1, closed);
            Assert.True(window.IsClosed);
            Assert.Null(loop.Pump);
            Assert.Equal(bitmaps, LinuxSettingsWindow.LiveBitmaps);
            Assert.Equal(0u, SdlNative.WasInit(SdlNative.InitVideo));

            var again = Open(loop, () => closed++);
            again.Show();
            Turn(loop);
            again.Dispose();
            Assert.Equal(2, closed);
            Assert.Equal(0u, SdlNative.WasInit(SdlNative.InitVideo));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // SDL frees its queue's lock when video stops with the window: a wake pushed from another thread at that moment must not
    // overlap it. The other thread posts all along while the window opens and closes again and again.
    [Fact]
    public void Posts_from_another_thread_while_the_window_opens_and_closes_leave_SDL_stopped()
    {
        Assert.True(SdlLibrary.TryLoad(), SdlLibrary.LastError);
        var bitmaps = LinuxSettingsWindow.LiveBitmaps;
        using var loop = new LinuxLoop();
        var stop = false;
        var posts = 0;
        var poster = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                loop.Post(() => { });
                posts++;
                Thread.SpinWait(200);
            }
        });
        poster.Start();
        try
        {
            var closed = 0;
            for (var i = 0; i < 200; i++)
            {
                var window = Open(loop, () => closed++);
                RunGuarded(loop);
                window.Dispose();
                RunGuarded(loop);
            }

            Assert.Equal(200, closed);
        }
        finally
        {
            Volatile.Write(ref stop, true);
            Assert.True(poster.Join(GuardMs));
        }

        Assert.True(posts > 0);
        Assert.Null(loop.Pump);
        Assert.Equal(bitmaps, LinuxSettingsWindow.LiveBitmaps);
        Assert.Equal(0u, SdlNative.WasInit(SdlNative.InitVideo));
    }

    // Runs what is posted now, with a guard against a loop that never ends.
    private static void RunGuarded(LinuxLoop loop)
    {
        var guarded = false;
        using var guard = new Timer(_ =>
        {
            guarded = true;
            loop.Quit();
        }, null, GuardMs, Timeout.Infinite);
        loop.Post(loop.Quit);
        loop.Run();
        Assert.False(guarded, "the loop had to be stopped by the guard");
    }

    private static uint IdOf(LinuxSettingsWindow window) => window.WindowId;
}
