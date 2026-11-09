using System.IO;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.App;

/// <summary>What the app itself writes to its journal about the shortcuts of a run without hotkeys.</summary>
public sealed class AppHotkeysJournalTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Log => _temp.Combine("settings.log");

    private string JournalOf(bool noHotkeys)
    {
        string[] more = noHotkeys ? ["--no-hotkeys"] : [];
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen", .. more]);
        var app = new HindsoundApp(options, new Journal(Log));

        app.NoteHotkeys([new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+Shift+F10", HotkeyStatus.Registered, null)]);

        return File.ReadAllText(Log);
    }

    [Fact]
    public void A_run_without_hotkeys_journals_a_shortcut_as_accepted_and_not_registered()
    {
        Assert.Contains("nothing registered with Windows in this run", JournalOf(noHotkeys: true));
    }

    [Fact]
    public void A_normal_run_journals_the_plain_line()
    {
        var journal = JournalOf(noHotkeys: false);

        Assert.Contains("hotkey SaveDefault Ctrl+Shift+F10: Registered", journal);
        Assert.DoesNotContain("nothing registered", journal);
    }
}
