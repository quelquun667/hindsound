using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>A run without hotkeys must not write, in its journal, that Windows holds a shortcut.</summary>
public class NoHotkeysJournalTests
{
    [Fact]
    public void A_shortcut_the_empty_layer_accepted_is_not_journaled_as_plainly_registered()
    {
        var options = AppOptions.Parse(["--no-hotkeys"]);
        using var manager = new HotkeyManager(options.NewHotkeyLayer());

        var lines = manager.Apply(new HotkeySettings())
            .Where(result => result.Status != HotkeyStatus.NotSet)
            .Select(result => result.JournalLine(options.NoHotkeys))
            .ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains("nothing registered with Windows", line));
        Assert.All(lines, line => Assert.StartsWith("hotkey ", line));
    }

    [Fact]
    public void A_normal_run_keeps_the_plain_line()
    {
        var result = new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+Shift+F10", HotkeyStatus.Registered, null);

        Assert.Equal("hotkey SaveDefault Ctrl+Shift+F10: Registered", result.JournalLine(false));
    }

    [Fact]
    public void A_refusal_is_journaled_as_it_is_even_without_hotkeys()
    {
        var result = new HotkeyResult(HotkeyAction.Save1m, "Ctrl+1", HotkeyStatus.Invalid, "x");

        Assert.Equal("hotkey Save1m Ctrl+1: Invalid", result.JournalLine(true));
    }
}
