using System.Runtime.CompilerServices;
using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A closed window leaves nothing of the Now page behind: the screen, its wave and the lanes go with it.</summary>
public class NowReleasedTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Window, WeakReference Screen, WeakReference Wave) OpenOnNowAndClose()
    {
        using var rig = new ScreenRig();
        var window = new SettingsWindow(rig.Settings, rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });
        window.Screen.SetNow(new NowPanel { Facts = NowRig.Held(NowRig.Loud(5), null, 5) });

        // The timer message of the wave: the wave and its two lanes are made by the first one.
        WindowNative.SendMessageW(window.Hwnd, Hindsound.Ui.Native.WM_TIMER, 2, 0);
        var wave = window.Screen.Wave;
        Assert.NotNull(wave);
        Assert.NotEqual(WaveLane.Empty, wave.SystemBars[^1]);

        var weak = (new WeakReference(window), new WeakReference(window.Screen), new WeakReference(wave));
        window.Dispose();
        return weak;
    }

    [Fact]
    public void After_closing_no_object_of_the_page_is_kept_alive()
    {
        var (window, screen, wave) = OpenOnNowAndClose();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(wave.IsAlive, "the wave is still alive");
        Assert.False(screen.IsAlive, "the screen is still alive");
        Assert.False(window.IsAlive, "the window is still alive");
    }
}
