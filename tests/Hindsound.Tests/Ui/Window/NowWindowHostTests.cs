using System.Runtime.InteropServices;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The real window's side of the cost of the Now page: the second timer exists only while the wave is wanted, and a tick hands Windows the
/// rounded-out rectangle it drew in, never the whole window. The screen's own tests see the host through a double and cannot say either.
/// </summary>
public sealed class NowWindowHostTests : IDisposable
{
    private const nuint WaveTimer = 2;

    private readonly ScreenRig _rig = new();
    private readonly SettingsWindow _window;
    private readonly NowPanel _panel = new() { Facts = NowRig.Held(NowRig.Loud(5), null, 5) };

    public NowWindowHostTests()
    {
        _window = new SettingsWindow(_rig.Settings, _rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });
        _window.Screen.SetNow(_panel);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetUpdateRect(nint hwnd, out Rect rect, int erase);

    [DllImport("user32.dll")]
    private static extern int ValidateRect(nint hwnd, nint rect);

    [DllImport("user32.dll")]
    private static extern int KillTimer(nint hwnd, nuint id);

    public void Dispose()
    {
        _window.Dispose();
        _rig.Dispose();
    }

    // Killing a timer that does not exist fails: that is how to ask Windows whether one is set. It is gone after the question.
    private bool WaveTimerWasSet() => KillTimer(_window.Hwnd, WaveTimer) != 0;

    private void SizeMessage(nuint kind) => WindowNative.SendMessageW(_window.Hwnd, WindowNative.WM_SIZE, kind, 0);

    private Rect UpdateRect()
    {
        _ = GetUpdateRect(_window.Hwnd, out var rect, 0);
        return rect;
    }

    [Fact]
    public void The_timer_is_set_while_the_wave_is_wanted()
    {
        Assert.True(_window.Screen.WantsWave);
        Assert.True(WaveTimerWasSet());
    }

    [Fact]
    public void Minimizing_kills_the_timer_and_restoring_sets_it_again()
    {
        SizeMessage(WindowNative.SIZE_MINIMIZED);
        Assert.False(WaveTimerWasSet());

        SizeMessage(0);
        Assert.True(WaveTimerWasSet());
    }

    [Fact]
    public void Another_page_kills_the_timer()
    {
        _window.Screen.ShowSection(Section.Hotkeys);

        Assert.False(WaveTimerWasSet());
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(168u)]
    [InlineData(192u)]
    public void An_area_reaches_Windows_rounded_outwards_at_the_scale_of_the_window(uint dpi)
    {
        _window.Show();
        _window.ProbeDpiChange(dpi);
        var scale = dpi / 96f;
        var area = new Box(41.3f, 17.6f, 333.3f, 29.7f);
        _ = ValidateRect(_window.Hwnd, 0);
        Assert.Equal(0, GetUpdateRect(_window.Hwnd, out _, 0));

        _window.InvalidateArea(area);

        Assert.NotEqual(0, GetUpdateRect(_window.Hwnd, out var rect, 0));
        Assert.Equal((int)MathF.Floor(area.X * scale), rect.Left);
        Assert.Equal((int)MathF.Floor(area.Y * scale), rect.Top);
        Assert.Equal((int)MathF.Ceiling(area.Right * scale), rect.Right);
        Assert.Equal((int)MathF.Ceiling(area.Bottom * scale), rect.Bottom);
    }

    [Fact]
    public void A_tick_that_changes_the_wave_invalidates_its_area_and_not_the_window()
    {
        _window.Show();
        _window.ProbeDpiChange(144);
        _ = ValidateRect(_window.Hwnd, 0);

        WindowNative.SendMessageW(_window.Hwnd, Hindsound.Ui.Native.WM_TIMER, WaveTimer, 0);

        var area = NowWave.WaveArea;
        var rect = UpdateRect();
        Assert.Equal((int)MathF.Floor(area.X * 1.5f), rect.Left);
        Assert.Equal((int)MathF.Floor(area.Y * 1.5f), rect.Top);
        Assert.Equal((int)MathF.Ceiling(area.Right * 1.5f), rect.Right);
        // The first look also finds a new line under the wave: the union ends at the bottom of that one.
        Assert.Equal((int)MathF.Ceiling(NowWave.SaveArea.Bottom * 1.5f), rect.Bottom);
    }
}
