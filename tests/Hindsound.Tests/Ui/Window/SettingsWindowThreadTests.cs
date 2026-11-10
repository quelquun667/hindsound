using System.Runtime.InteropServices;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

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

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] state);

    // The key state of this thread is what GetKeyState reads, and it follows the real keyboard: Ctrl held there is Ctrl held
    // here, for this thread only and without a key sent to any other window.
    [Fact]
    public void Ctrl_held_on_the_real_keyboard_does_not_reach_the_window_off_screen()
    {
        const int ctrl = 0x11, leftCtrl = 0xA2;
        using var rig = new ScreenRig();
        using var window = Open(rig);
        var before = new byte[256];
        Assert.True(GetKeyboardState(before));
        var held = (byte[])before.Clone();
        held[ctrl] = held[leftCtrl] = 0x80;
        try
        {
            Assert.True(SetKeyboardState(held));
            Assert.True(WindowNative.GetKeyState(ctrl) < 0);
            PressSave(window);
        }
        finally
        {
            SetKeyboardState(before);
        }

        Assert.Equal("band.save", window.Screen.FocusId);
        Assert.Equal(1, rig.Backend.Saves);
    }
}
