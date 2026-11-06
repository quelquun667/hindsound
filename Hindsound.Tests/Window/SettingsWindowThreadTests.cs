using Hindsound.Ui.Window;

namespace Hindsound.Tests.Window;

/// <summary>The tests of the window run side by side, each on its thread: one window must never take the messages of another.</summary>
public sealed class SettingsWindowThreadTests
{
    private static SettingsWindow Open(ScreenRig rig) =>
        new(rig.Settings, rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });

    private static void PressSave(SettingsWindow window)
    {
        for (var i = 0; i < 40 && window.Screen.FocusId != "band.save"; i++)
        {
            window.Screen.Key(Vk.Tab, 0);
        }

        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_KEYDOWN, Vk.Enter, 1);
    }

    [Fact]
    public void A_window_opened_on_another_thread_leaves_the_messages_of_this_one_alone()
    {
        using var rig = new ScreenRig();
        using var window = Open(rig);

        var other = new Thread(() =>
        {
            using var otherRig = new ScreenRig();
            Open(otherRig);
        });
        other.Start();
        other.Join();

        PressSave(window);

        Assert.Equal(1, rig.Backend.Saves);
    }

    [Fact]
    public void A_window_closed_on_another_thread_leaves_the_messages_of_this_one_alone()
    {
        using var rig = new ScreenRig();
        using var window = Open(rig);

        var other = new Thread(() =>
        {
            using var otherRig = new ScreenRig();
            using var closing = Open(otherRig);
        });
        other.Start();
        other.Join();

        PressSave(window);

        Assert.Equal(1, rig.Backend.Saves);
    }
}
