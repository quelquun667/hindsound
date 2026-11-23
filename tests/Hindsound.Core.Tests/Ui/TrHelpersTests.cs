using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui;

/// <summary>What the translators write with: counts that agree, the "de" of a name, and a size that never breaks in two.</summary>
public sealed class TrHelpersTests
{
    [Theory]
    [InlineData(UiLanguage.English, 0, "0 files")]
    [InlineData(UiLanguage.English, 1, "1 file")]
    [InlineData(UiLanguage.English, 2, "2 files")]
    [InlineData(UiLanguage.English, 2000, "2,000 files")]
    [InlineData(UiLanguage.French, 0, "0 fichier")]
    [InlineData(UiLanguage.French, 1, "1 fichier")]
    [InlineData(UiLanguage.French, 2, "2 fichiers")]
    [InlineData(UiLanguage.French, 2000, "2\u00A0000 fichiers")]
    public void A_count_takes_the_singular_of_its_language(UiLanguage language, long count, string expected)
    {
        using var scope = Tr.Use(language);
        Assert.Equal(expected, Tr.Plural(count, "{0} file", "{0} files", "{0} fichier", "{0} fichiers"));
    }

    [Theory]
    [InlineData("Discord", "de Discord")]
    [InlineData("OBS", "d'OBS")]
    [InlineData("obs64", "d'obs64")]
    [InlineData("Opera", "d'Opera")]
    [InlineData("Édge", "d'Édge")]
    [InlineData("Hindsound", "de Hindsound")]
    [InlineData("Hades", "de Hades")]
    [InlineData("Spotify", "de Spotify")]
    [InlineData("7-Zip", "de 7-Zip")]
    [InlineData("", "de ")]
    public void A_name_after_de_is_elided_before_a_vowel_and_not_before_an_h(string name, string expected)
    {
        Assert.Equal(expected, Tr.FrenchOf(name));
    }

    [Fact]
    public void The_band_elides_the_de_before_the_name_of_the_recorded_app_in_French_only()
    {
        var capturing = new AudioEngineStatus(CaptureActivity.Capturing, true, true, true, "Speakers", "Headset", true, TimeSpan.FromMinutes(5), 86_688_000);
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Capture de Discord", StatusText.From(capturing with { SourceApp = "Discord.exe" }, 300).Title);
        Assert.Equal("Capture d'OBS", StatusText.From(capturing with { SourceApp = "OBS.exe" }, 300).Title);
        Tr.Set(UiLanguage.English);
        Assert.Equal("Capturing OBS", StatusText.From(capturing with { SourceApp = "OBS.exe" }, 300).Title);
    }

    [Theory]
    [InlineData(UiLanguage.English, "87\u00A0MB")]
    [InlineData(UiLanguage.French, "87\u00A0Mo")]
    public void A_size_never_parts_its_number_from_its_unit(UiLanguage language, string expected)
    {
        using var scope = Tr.Use(language);
        Assert.Equal(expected, Tr.Size(87, "0", SizeUnit.MB));
        Assert.DoesNotContain(' ', Tr.Size(1500.5, "#,0.0", SizeUnit.MB));
    }
}
