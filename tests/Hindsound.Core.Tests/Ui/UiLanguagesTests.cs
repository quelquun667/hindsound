using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>
/// Which language the interface takes, from the setting and what each system says the user prefers. Pure, so it runs on the
/// three systems, where the Linux and Mac readers themselves only run in their own CI.
/// </summary>
public sealed class UiLanguagesTests
{
    [Theory]
    [InlineData("fr", new string[0], UiLanguage.French)]
    [InlineData("fr", new[] { "en-US" }, UiLanguage.French)]
    [InlineData("en", new[] { "fr-FR" }, UiLanguage.English)]
    [InlineData("system", new[] { "fr-FR", "en-US" }, UiLanguage.French)]
    [InlineData("system", new[] { "en-US", "fr-FR" }, UiLanguage.English)]
    [InlineData("system", new[] { "de-DE", "fr-CA", "en-GB" }, UiLanguage.French)]
    [InlineData("system", new[] { "de-DE", "en-GB", "fr-CA" }, UiLanguage.English)]
    [InlineData("system", new[] { "de-DE" }, UiLanguage.English)]
    [InlineData("system", new string[0], UiLanguage.English)]
    [InlineData("system", new[] { "fr" }, UiLanguage.French)]
    [InlineData("system", new[] { "FR_ca" }, UiLanguage.French)]
    [InlineData("system", new[] { "", " ", "fr-FR" }, UiLanguage.French)]
    [InlineData("system", new[] { "fy-NL", "fr-FR" }, UiLanguage.French)]
    [InlineData("klingon", new[] { "fr-FR" }, UiLanguage.French)]
    [InlineData(null, new[] { "fr-FR" }, UiLanguage.French)]
    public void The_setting_or_else_the_first_preferred_language_the_app_has_decides(string? setting, string[] preferred, UiLanguage expected) =>
        Assert.Equal(expected, UiLanguages.Resolve(setting, preferred));

    [Theory]
    [InlineData("fr_FR.UTF-8", "fr-FR")]
    [InlineData("fr_CA@euro", "fr-CA")]
    [InlineData("fr_FR.ISO-8859-15@euro", "fr-FR")]
    [InlineData("en_US", "en-US")]
    [InlineData("fr", "fr")]
    [InlineData(" de_DE.UTF-8 ", "de-DE")]
    [InlineData("C", null)]
    [InlineData("C.UTF-8", null)]
    [InlineData("POSIX", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_posix_locale_becomes_a_tag_and_C_names_no_language(string? locale, string? expected) =>
        Assert.Equal(expected, UiLanguages.FromPosixLocale(locale));

    private static IReadOnlyList<string> Linux(params (string Name, string Value)[] set) =>
        UiLanguages.FromLinuxEnvironment(name => set.FirstOrDefault(v => v.Name == name).Value);

    [Fact]
    public void On_Linux_LANGUAGE_comes_first_in_its_order_then_the_locale_of_the_messages()
    {
        Assert.Equal(["fr-FR", "en", "en-US"], Linux(("LANGUAGE", "fr_FR:en"), ("LANG", "en_US.UTF-8")));
        Assert.Equal(["en-GB", "fr-FR"], Linux(("LANGUAGE", "en_GB::"), ("LANG", "fr_FR.UTF-8")));
        Assert.Equal(UiLanguage.French, UiLanguages.Resolve("system", Linux(("LANGUAGE", "de:fr_FR:en"), ("LANG", "en_US.UTF-8"))));
    }

    [Fact]
    public void On_Linux_LC_ALL_wins_over_LC_MESSAGES_which_wins_over_LANG()
    {
        Assert.Equal(["fr-CA"], Linux(("LC_ALL", "fr_CA@euro"), ("LC_MESSAGES", "en_US.UTF-8"), ("LANG", "de_DE.UTF-8")));
        Assert.Equal(["en-US"], Linux(("LC_MESSAGES", "en_US.UTF-8"), ("LANG", "fr_FR.UTF-8")));
        Assert.Equal(["fr-FR"], Linux(("LANG", "fr_FR.UTF-8")));
        Assert.Empty(Linux());
    }

    [Fact]
    public void On_Linux_a_C_or_POSIX_locale_gives_nothing_and_LANGUAGE_is_then_ignored_as_gettext_does()
    {
        Assert.Empty(Linux(("LANG", "C")));
        Assert.Empty(Linux(("LANG", "C.UTF-8"), ("LANGUAGE", "fr_FR")));
        Assert.Empty(Linux(("LC_ALL", "POSIX"), ("LANG", "fr_FR.UTF-8")));
        Assert.Equal(UiLanguage.English, UiLanguages.Resolve("system", Linux(("LC_ALL", "C"), ("LANG", "fr_FR.UTF-8"))));
    }

    [Fact]
    public void The_Windows_list_is_read_up_to_its_double_null()
    {
        Assert.Equal(["fr-FR", "en-US"], UiLanguages.FromMultiString("fr-FR\0en-US\0\0"));
        Assert.Equal(["de-DE"], UiLanguages.FromMultiString("de-DE\0\0"));
        Assert.Empty(UiLanguages.FromMultiString("\0\0"));
        Assert.Empty(UiLanguages.FromMultiString(string.Empty));
    }

    [Fact]
    public void A_list_from_a_system_keeps_its_order_without_empty_or_repeated_tags() =>
        Assert.Equal(["fr-FR", "en-US"], UiLanguages.Clean(["fr-FR", null, " ", "en-US", "FR-fr"]));

    [Fact]
    public void The_journal_line_says_the_setting_what_the_system_gave_and_the_choice_in_English()
    {
        Assert.Equal("language: setting system, system gave fr-FR en-US, chosen French", UiLanguages.JournalLine("system", ["fr-FR", "en-US"], UiLanguage.French));
        Assert.Equal("language: setting en, system gave nothing, chosen English", UiLanguages.JournalLine("en", [], UiLanguage.English));
    }

    [Theory]
    [InlineData("{}", "system")]
    [InlineData("{ \"general\": { \"accentColor\": \"#4CD9F5\" } }", "system")]
    [InlineData("{ \"general\": { \"language\": \"fr\" } }", "fr")]
    [InlineData("{ \"general\": { \"language\": \"EN\" } }", "en")]
    [InlineData("{ \"general\": { \"language\": \"de\" } }", "system")]
    [InlineData("{ \"general\": { \"language\": null } }", "system")]
    public void A_missing_or_unknown_language_in_the_file_reads_as_system(string json, string expected)
    {
        using var folder = new Hindsound.Tests.Export.TempFolder();
        var path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, json);

        Assert.Equal(expected, new SettingsStore(path).Load().General.Language);
    }

    [Fact]
    public void The_language_is_written_in_the_file_beside_the_other_keys_of_general()
    {
        using var folder = new Hindsound.Tests.Export.TempFolder();
        var path = Path.Combine(folder.Path, "settings.json");
        var settings = new AppSettings().Normalize();
        settings.General.Language = GeneralSettings.LanguageFrench;
        new SettingsStore(path).Save(settings);

        Assert.Contains("\"language\": \"fr\"", File.ReadAllText(path));
        Assert.Equal("fr", new SettingsStore(path).Load().General.Language);
    }

    /// <summary>
    /// What 0.12.0 does with a file this version wrote: its store reads with the same options, which leave a key it does not
    /// know aside. A key unknown to this version stands for "language" there: the file loads, and the rest of it is kept.
    /// </summary>
    [Fact]
    public void A_key_the_version_does_not_know_is_left_aside_and_the_rest_of_the_file_kept()
    {
        using var folder = new Hindsound.Tests.Export.TempFolder();
        var path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, "{ \"general\": { \"languageOfALaterVersion\": \"fr\", \"accentColor\": \"#4CD9F5\" }, \"clipSeconds\": 45 }");

        var store = new SettingsStore(path);
        var settings = store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
        Assert.Equal("#4CD9F5", settings.General.AccentColor);
        Assert.Equal(45, settings.ClipSeconds);
    }
}
