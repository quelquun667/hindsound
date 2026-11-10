using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>A test run on the default settings must not take the shortcuts of the user.</summary>
public class NoHotkeysTests
{
    [Fact]
    public void The_option_is_read_and_off_by_default()
    {
        Assert.True(AppOptions.Parse(["--no-hotkeys"]).NoHotkeys);
        Assert.False(AppOptions.Parse([]).NoHotkeys);
    }

    [Fact]
    public void Each_run_goes_through_the_layer_it_asked_for()
    {
        using var none = AppOptions.Parse(["--no-hotkeys"]).NewHotkeyLayer();
        using var real = AppOptions.Parse([]).NewHotkeyLayer();

        Assert.IsType<UnregisteredHotkeys>(none);
        Assert.IsType<Win32HotkeyNative>(Assert.IsType<KeyByKeyHotkeys>(real).Registrar);
    }

    [Fact]
    public void The_default_shortcuts_all_look_fine_to_the_app()
    {
        using var manager = new HotkeyManager(AppOptions.Parse(["--no-hotkeys"]).NewHotkeyLayer());

        var results = manager.Apply(new HotkeySettings());

        Assert.Contains(results, result => result.Status != HotkeyStatus.NotSet);
        Assert.All(results, result => Assert.True(result.Ok));
    }

    [Fact]
    public void A_run_without_the_option_still_gets_the_refusal_of_windows()
    {
        var native = new Ui.Window.FakeHotkeys();
        var settings = new HotkeySettings();
        native.OtherApps.Add((HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79));
        using var manager = new HotkeyManager(native);

        var results = manager.Apply(settings);

        Assert.Contains(results, result => !result.Ok);
    }
}
