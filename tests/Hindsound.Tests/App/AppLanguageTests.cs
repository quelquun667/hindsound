using System.IO;
using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.App;

/// <summary>
/// The language of the interface follows the settings file when it is read again, as after a hand edit while the app runs.
/// The journal says what it was chosen from. In a scope of its own, so that the tests beside it stay in English.
/// </summary>
public sealed class AppLanguageTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private HindsoundApp Built()
    {
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        return new HindsoundApp(options, new Journal(_temp.Combine("settings.log")))
        {
            OpenInstead = _ => { },
            LoadChoicesInstead = () => { },
        };
    }

    private void Write(string language)
    {
        var settings = new AppSettings().Normalize();
        settings.General.Language = language;
        new SettingsStore(_temp.Combine("settings.json")).Save(settings);
    }

    private List<string> LanguageLines() =>
        [.. Journal.ReadShared(_temp.Combine("settings.log")).Where(line => line.Contains("language: setting", StringComparison.Ordinal))];

    [Fact]
    public void The_language_of_the_file_is_taken_at_each_read_of_the_file_edited_by_hand()
    {
        using var scope = Tr.Use(UiLanguage.English);
        var app = Built();
        Write(GeneralSettings.LanguageFrench);
        app.OnWindowActivated();
        Assert.Equal(UiLanguage.French, Tr.Language);

        Write(GeneralSettings.LanguageEnglish);
        File.SetLastWriteTimeUtc(_temp.Combine("settings.json"), DateTime.UtcNow.AddSeconds(5));
        app.OnWindowActivated();

        Assert.Equal(UiLanguage.English, Tr.Language);
        var lines = LanguageLines();
        Assert.Equal(2, lines.Count);
        Assert.Contains("setting fr, system gave ", lines[0]);
        Assert.EndsWith(", chosen French", lines[0]);
        Assert.Contains("setting en, system gave ", lines[1]);
        Assert.EndsWith(", chosen English", lines[1]);
    }

    [Fact]
    public void A_read_of_the_file_that_changes_nothing_writes_no_line()
    {
        using var scope = Tr.Use(UiLanguage.English);
        var app = Built();
        Write(GeneralSettings.LanguageFrench);
        app.OnWindowActivated();

        var settings = new AppSettings().Normalize();
        settings.General.Language = GeneralSettings.LanguageFrench;
        settings.ClipSeconds = 45;
        new SettingsStore(_temp.Combine("settings.json")).Save(settings);
        File.SetLastWriteTimeUtc(_temp.Combine("settings.json"), DateTime.UtcNow.AddSeconds(5));
        app.OnWindowActivated();

        Assert.Equal(45, app.SettingsNow.ClipSeconds);
        Assert.Single(LanguageLines());
        Assert.Equal(UiLanguage.French, Tr.Language);
    }
}
