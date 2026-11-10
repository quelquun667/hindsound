using System.Text.Json;
using Hindsound.App;
using Hindsound.Tests.Export;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>
/// The lines of the windowless menu, chosen in an app built off screen and never run. With --settings there is no startup
/// entry to touch: only the setting and its file change.
/// </summary>
public class AppTrayChosenTests
{
    private static bool StartsWithSessionIn(string settingsFile)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(settingsFile));
        return json.RootElement.GetProperty("general").GetProperty("startWithWindows").GetBoolean();
    }

    [Fact]
    public void Start_with_the_session_turns_the_setting_over_and_writes_it_each_time()
    {
        using var temp = new TempFolder();
        var settings = temp.Combine("settings.json");
        var log = temp.Combine("settings.log");
        var options = AppOptions.Parse(["--settings", settings, "--no-mic", "--offscreen"]);
        var app = new HindsoundApp(options, new Journal(log));

        app.OnTrayChosen(TrayCommand.StartWithSession);
        var first = StartsWithSessionIn(settings);
        app.OnTrayChosen(TrayCommand.StartWithSession);
        var second = StartsWithSessionIn(settings);

        Assert.NotEqual(first, second);
        Assert.Equal(2, File.ReadAllLines(log).Count(line => line.Contains("tray menu: StartWithSession, ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_right_click_off_screen_hands_the_command_the_message_names_to_the_app()
    {
        using var temp = new TempFolder();
        var settings = temp.Combine("settings.json");
        var log = temp.Combine("settings.log");
        var options = AppOptions.Parse(["--settings", settings, "--no-mic", "--offscreen"]);
        var app = new HindsoundApp(options, new Journal(log));

        app.OnTrayMessage(Native.WM_CONTEXTMENU, (nuint)TrayCommand.StartWithSession);

        Assert.Contains(File.ReadAllLines(log), line => line.Contains("tray menu: StartWithSession, ", StringComparison.Ordinal));
        Assert.True(File.Exists(settings));
    }
}
