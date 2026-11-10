using System.Runtime.InteropServices;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The double click on a row of the Library goes through the real window: its class must ask for it, and the message must reach the screen.</summary>
public sealed class SettingsWindowDoubleClickTests
{
    private const int GclStyle = -26;
    private const float RowX = 300, RowY = 138;

    private sealed class NoNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint hwnd, int index);

    private static nint Point(float x, float y) => (nint)(((int)y << 16) | (int)x);

    [Fact]
    public void The_class_of_the_window_takes_double_clicks_and_one_plays_the_row_once()
    {
        // The window is created and never shown, so nothing appears and nothing takes the focus.
        using var rig = new ScreenRig();
        var library = new PlayingLibraryFake();
        var screen = new ScreenInfo(default, default, 96);
        using var window = new SettingsWindow(rig.Settings, rig.Backend, () => 0, firstRun: false, offscreen: true, screen, () => { });
        window.ProbeDpiChange(96);
        window.Screen.UpdateStatus(ScreenRig.Capturing());
        window.Screen.SetNow(new NoNow());
        window.Screen.SetLibrary(library);
        window.Screen.ShowSection(Section.Library);

        Assert.NotEqual(0, GetClassLongPtr(window.Hwnd, GclStyle) & WindowNative.CS_DBLCLKS);

        var at = Point(RowX, RowY);
        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_LBUTTONDOWN, 1, at);
        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_LBUTTONUP, 0, at);
        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_LBUTTONDBLCLK, 1, at);
        WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_LBUTTONUP, 0, at);

        Assert.Equal(library.Lines!.Lines[0].Main.Path, Assert.Single(library.Played).Main.Path);
    }
}
