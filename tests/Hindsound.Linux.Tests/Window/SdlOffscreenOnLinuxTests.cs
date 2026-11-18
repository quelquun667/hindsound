using System.Runtime.Versioning;
using Hindsound.Linux.App;
using Hindsound.Linux.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// SDL 3 on a real Linux with no display: the offscreen driver makes a hidden window, draws our pixels into it, and its
/// event queue does the waiting of the loop, woken from other threads.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(SdlCollection.Name)]
public sealed unsafe class SdlOffscreenOnLinuxTests
{
    private const int GuardMs = 10_000;
    private const int Width = 320;
    private const int Height = 200;

    private static void RunGuarded(LinuxLoop loop)
    {
        var guarded = false;
        using var guard = new Timer(_ =>
        {
            guarded = true;
            loop.Quit();
        }, null, GuardMs, Timeout.Infinite);
        loop.Run();
        Assert.False(guarded, "the loop had to be stopped by the guard");
    }

    [Fact]
    public void A_hidden_offscreen_window_takes_our_pixels_and_its_queue_wakes_the_loop_from_other_threads()
    {
        Assert.True(SdlLibrary.TryLoad(), SdlLibrary.LastError);
        Assert.NotNull(SdlLibrary.LoadedFrom);
        Assert.True(SdlNative.SetHint(SdlNative.HintVideoDriver, SdlNative.OffscreenDriver), SdlNative.Error());
        Assert.True(SdlNative.SetHint(SdlNative.HintAppId, SdlNative.AppId), SdlNative.Error());
        Assert.True(SdlNative.InitSubSystem(SdlNative.InitVideo), SdlNative.Error());
        try
        {
            var window = SdlNative.CreateWindow("Hindsound", Width, Height, SdlNative.WindowHidden);
            Assert.True(window != 0, SdlNative.Error());
            try
            {
                Assert.NotEqual(0ul, SdlNative.GetWindowFlags(window) & SdlNative.WindowHidden);
                Assert.NotEqual(0u, SdlNative.GetWindowID(window));
                Assert.True(SdlNative.SetWindowTitle(window, "Hindsound: été"), SdlNative.Error());
                Assert.True(SdlNative.GetWindowDisplayScale(window) > 0);
                Assert.True(SdlNative.GetWindowPixelDensity(window) > 0);

                int w, h;
                Assert.True(SdlNative.GetWindowSizeInPixels(window, &w, &h), SdlNative.Error());
                Assert.Equal((Width, Height), (w, h));

                SdlNative.Rect usable;
                Assert.True(SdlNative.GetDisplayUsableBounds(SdlNative.GetPrimaryDisplay(), &usable), SdlNative.Error());
                Assert.True(usable.W > 0 && usable.H > 0);

                DrawsOurPixels(window);
                ThePumpWakesTheLoop();
            }
            finally
            {
                SdlNative.DestroyWindow(window);
            }
        }
        finally
        {
            SdlNative.QuitSubSystem(SdlNative.InitVideo);
        }

        Assert.Equal(0u, SdlNative.WasInit(SdlNative.InitVideo));
    }

    // Our buffer, wrapped in a surface, copied to the window's own surface and shown.
    private static void DrawsOurPixels(nint window)
    {
        var target = SdlNative.GetWindowSurface(window);
        Assert.True(target is not null, SdlNative.Error());
        Assert.Equal((Width, Height), (target->W, target->H));

        var pixels = new uint[Width * Height];
        Array.Fill(pixels, 0xFF336699u);
        pixels[(Height - 1) * Width + Width - 1] = 0xFFA0B0C0u;
        fixed (uint* bits = pixels)
        {
            var ours = SdlNative.CreateSurfaceFrom(Width, Height, SdlNative.PixelFormatArgb8888, bits, Width * 4);
            Assert.True(ours is not null, SdlNative.Error());
            try
            {
                Assert.True(SdlNative.BlitSurface(ours, null, target, null), SdlNative.Error());
            }
            finally
            {
                SdlNative.DestroySurface(ours);
            }
        }

        var rect = new SdlNative.Rect(0, 0, Width, Height);
        Assert.True(SdlNative.UpdateWindowSurfaceRects(window, &rect, 1), SdlNative.Error());
        Assert.True(SdlNative.UpdateWindowSurface(window), SdlNative.Error());

        // The offscreen surface is 32 bits of xRGB or ARGB: the colour reads back the same, alpha aside.
        Assert.Contains(target->Format, new[] { SdlNative.PixelFormatXrgb8888, SdlNative.PixelFormatArgb8888 });
        var row = (byte*)target->Pixels + (Height - 1) * target->Pitch;
        Assert.Equal(0x336699u, *(uint*)target->Pixels & 0xFFFFFF);
        Assert.Equal(0xA0B0C0u, ((uint*)row)[Width - 1] & 0xFFFFFF);
    }

    // The loop waits in SDL's queue: a post from another thread wakes it, and an event pushed from another thread reaches the
    // window's handler on the loop thread, never the wake itself.
    private static void ThePumpWakesTheLoop()
    {
        using var loop = new LinuxLoop();
        var pump = new SdlPump();
        var ownType = SdlNative.RegisterEvents(1);
        Assert.NotEqual(0u, ownType);
        Assert.NotEqual(pump.WakeType, ownType);

        var handled = new List<(uint Type, int Code, bool OnLoop)>();
        var posted = false;
        pump.Handler = (in SdlNative.Event e) =>
        {
            handled.Add((e.Type, e.User.Code, loop.OnLoopThread));
            if (e.Type == ownType)
            {
                loop.Post(loop.Quit);
            }
        };
        loop.Pump = pump;

        var other = new Thread(() =>
        {
            Thread.Sleep(100);

            // Many posts, one wake in the queue at a time: each still runs.
            for (var i = 0; i < 50; i++)
            {
                loop.Post(() => posted = true);
            }

            Thread.Sleep(100);
            var e = default(SdlNative.Event);
            e.User.Type = ownType;
            e.User.Code = 7;
            SdlNative.PushEvent(&e);
        });
        other.Start();

        RunGuarded(loop);
        other.Join();
        loop.Pump = null;

        Assert.True(posted);
        Assert.DoesNotContain(handled, h => h.Type == pump.WakeType);
        Assert.Contains((ownType, 7, true), handled);
    }
}
