using System.Runtime.InteropServices;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Window;

/// <summary>
/// The real window and its real EDIT, never shown, with messages sent to them directly: nothing reaches the system's
/// keyboard, and the window never asks for the focus, as it is created off screen.
/// </summary>
public sealed class SettingsWindowTests : IDisposable
{
    private const nint Repeat = 0x40000001;
    private const nint FirstPress = 1;
    private const int StyleIndex = -16;
    private const long VisibleStyle = 0x10000000;

    private readonly ScreenRig _rig = new();
    private readonly SettingsWindow _window;

    public SettingsWindowTests()
    {
        _window = new SettingsWindow(_rig.Settings, _rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });
    }

    public void Dispose()
    {
        _window.Dispose();
        _rig.Dispose();
    }

    private SettingsScreen Screen => _window.Screen;

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    private void Send(nint target, uint message, nuint wParam, nint lParam = 0) => WindowNative.SendMessageW(target, message, wParam, lParam);

    private void Wheel(int delta) => Send(_window.Hwnd, WindowNative.WM_MOUSEWHEEL, (nuint)(uint)(delta << 16));

    private bool EditVisible => (Native.GetWindowLongPtrW(_window.EditHwnd, StyleIndex) & VisibleStyle) != 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetText(nint hwnd, char[] text, int count);

    private string EditText()
    {
        var buffer = new char[300];
        return new string(buffer, 0, GetText(_window.EditHwnd, buffer, buffer.Length));
    }

    private void FocusOn(string id)
    {
        for (var i = 0; i < 40 && Screen.FocusId != id; i++)
        {
            Screen.Key(Vk.Tab, 0);
        }

        Assert.Equal(id, Screen.FocusId);
    }

    private void OpenTheColorCode(string typed)
    {
        Screen.ShowSection(Section.General);
        FocusOn("general.accent");
        Screen.Key(Vk.End, 0);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal("general.hex", Screen.FocusId);
        WindowNative.SetWindowTextW(_window.EditHwnd, typed);
        Send(_window.EditHwnd, WindowNative.EM_SETSEL, (nuint)typed.Length, typed.Length);
    }

    [Fact]
    public void The_repeat_bit_of_a_real_message_reaches_the_screen()
    {
        FocusOn("band.save");

        Send(_window.Hwnd, WindowNative.WM_KEYDOWN, Vk.Enter, Repeat);
        Assert.Equal(0, _rig.Backend.Saves);

        Send(_window.Hwnd, WindowNative.WM_KEYDOWN, Vk.Enter, FirstPress);
        Assert.Equal(1, _rig.Backend.Saves);
    }

    [Fact]
    public void The_repeat_bit_also_counts_for_what_the_box_hands_to_the_window()
    {
        Screen.ShowSection(Section.Export);
        FocusOn("export.template");
        var stored = _rig.Settings.Export.FileNameTemplate;
        WindowNative.SetWindowTextW(_window.EditHwnd, "a<b");

        Send(_window.EditHwnd, WindowNative.WM_KEYDOWN, Vk.Escape, Repeat);
        Assert.Equal("a<b", Assert.IsType<TextField>(Screen.Focus).State.Draft);

        Send(_window.EditHwnd, WindowNative.WM_KEYDOWN, Vk.Escape, FirstPress);
        Assert.Equal(stored, Assert.IsType<TextField>(Screen.Focus).State.Draft);
    }

    [Fact]
    public void Small_wheel_deltas_scroll_a_notch_once_they_add_up()
    {
        Screen.ShowSection(Section.General);

        Wheel(-30);
        Wheel(-30);
        Wheel(-30);
        Assert.Equal(0, Screen.Scroll);

        Wheel(-30);
        Assert.True(Screen.Scroll > 0);
    }

    [Fact]
    public void Text_set_in_the_real_box_reaches_the_settings()
    {
        Screen.ShowSection(Section.Export);
        FocusOn("export.template");

        WindowNative.SetWindowTextW(_window.EditHwnd, "probe {date}");

        Assert.Equal("probe {date}", _rig.Settings.Export.FileNameTemplate);
    }

    [Fact]
    public void A_key_on_a_field_scrolled_away_lands_in_the_real_box_with_its_character()
    {
        OpenTheColorCode("#FF880");
        Wheel(-1200);
        Assert.False(EditVisible);

        Send(_window.Hwnd, WindowNative.WM_KEYDOWN, '0', FirstPress);
        Assert.True(EditVisible);
        Send(_window.Hwnd, WindowNative.WM_CHAR, '0', FirstPress);

        Assert.Equal("#FF8800", EditText());
        Assert.Equal("#FF8800", _rig.Settings.General.AccentColor);
    }

    [Fact]
    public void A_character_is_not_handed_to_the_box_unless_its_key_brought_the_field_back()
    {
        OpenTheColorCode("#FF880");

        Send(_window.Hwnd, WindowNative.WM_CHAR, '0', FirstPress);

        Assert.Equal("#FF880", EditText());
    }

    [Fact]
    public void A_key_that_moves_the_caret_also_lands_in_the_real_box()
    {
        OpenTheColorCode("#FF8800");
        Wheel(-1200);

        Send(_window.Hwnd, WindowNative.WM_KEYDOWN, Vk.Home, FirstPress);

        Assert.True(EditVisible);
        Assert.Equal("#FF8800", EditText());
        Assert.Equal(0, (long)WindowNative.SendMessageW(_window.EditHwnd, EmGetSel, 0, 0) & 0xFFFF);
    }

    [Fact]
    public void The_real_host_off_screen_never_takes_the_keyboard()
    {
        OpenTheColorCode("#FF880");
        Screen.UpdateStatus(ScreenRig.Capturing());
        Wheel(-1200);
        Wheel(1200);

        Assert.True(EditVisible);
        Assert.False(_window.IsForeground);
        Assert.Equal(0, GetFocus());
    }

    private const uint EmGetSel = 0x00B0;
}
