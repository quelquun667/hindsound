using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Nobody sees a minimized window: Windows still asks it to paint, and it must not draw a page for nothing.</summary>
public sealed class NowMinimizedPaintTests : IDisposable
{
    private readonly ScreenRig _rig = new();
    private readonly SettingsWindow _window;
    private readonly NowPanel _panel = new() { Facts = NowRig.Held(NowRig.Loud(5), null, 5) };

    public NowMinimizedPaintTests()
    {
        _window = new SettingsWindow(_rig.Settings, _rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });
        _window.Screen.SetNow(_panel);
    }

    public void Dispose()
    {
        _window.Dispose();
        _rig.Dispose();
    }

    private void Size(nuint kind) => WindowNative.SendMessageW(_window.Hwnd, WindowNative.WM_SIZE, kind, 0);

    private void PaintMessage() => WindowNative.SendMessageW(_window.Hwnd, WindowNative.WM_PAINT, 0, 0);

    [Fact]
    public void A_paint_message_while_minimized_draws_nothing_and_the_one_after_the_restore_draws_the_page()
    {
        Size(WindowNative.SIZE_MINIMIZED);
        _window.Invalidate();
        var before = _panel.Reads;

        PaintMessage();
        PaintMessage();

        Assert.Equal(before, _panel.Reads);

        Size(0);
        PaintMessage();

        Assert.True(_panel.Reads > before);
    }

    [Fact]
    public void A_window_that_was_never_minimized_paints_on_the_first_message()
    {
        PaintMessage();

        Assert.True(_panel.Reads > 0);
    }
}

public class NowRestoreTests
{
    [Fact]
    public void Coming_back_from_minimized_asks_for_one_paint_and_a_repeated_news_asks_for_none()
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(5), null, 5));
        rig.Settle();

        rig.Screen.WindowMinimized(true);
        Assert.Equal(0, rig.Host.Invalidated);

        rig.Screen.WindowMinimized(false);
        Assert.Equal(1, rig.Host.Invalidated);

        rig.Screen.WindowMinimized(false);
        Assert.Equal(1, rig.Host.Invalidated);
    }
}
