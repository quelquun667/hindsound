using Hindsound.Export;

namespace Hindsound.Tests.Export;

public class FileNameTemplateTests
{
    private static readonly DateTime Moment = new(2026, 11, 6, 14, 30, 5);
    private const string Template = "Hindsound_{app}_{date}_{time}";

    [Fact]
    public void The_default_template_gives_app_date_and_time()
    {
        Assert.Equal("Hindsound_Valorant_2026-11-06_14-30-05", FileNameTemplate.Render(Template, "Valorant", Moment));
    }

    [Fact]
    public void Characters_windows_forbids_are_removed_from_the_app()
    {
        // < > : " / \ | ? * and a control character
        Assert.Equal("Hindsound_abcdefghijk_2026-11-06_14-30-05",
            FileNameTemplate.Render(Template, "a<b>c:d\"e/f\\g|h?i*j\u0007k", Moment));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<>:|?*")]
    [InlineData("...")]
    public void An_empty_app_falls_back_to_unknown(string? app)
    {
        Assert.Equal("Hindsound_Unknown_2026-11-06_14-30-05", FileNameTemplate.Render(Template, app, Moment));
    }

    [Fact]
    public void Spaces_inside_the_app_are_kept_and_trailing_dots_are_dropped()
    {
        Assert.Equal("Hindsound_Rocket League_2026-11-06_14-30-05", FileNameTemplate.Render(Template, " Rocket League.. ", Moment));
    }

    [Fact]
    public void A_very_long_app_is_cut()
    {
        var name = FileNameTemplate.Render(Template, new string('x', 500), Moment);

        Assert.Equal("Hindsound_" + new string('x', 40) + "_2026-11-06_14-30-05", name);
    }

    [Fact]
    public void The_template_cannot_escape_the_folder()
    {
        var name = FileNameTemplate.Render(@"..\..\evil/{app}", "x", Moment);

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.Equal("....evilx", name);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    public void A_reserved_windows_name_is_prefixed(string template)
    {
        Assert.Equal("_" + template, FileNameTemplate.Render(template, "x", Moment));
    }

    [Fact]
    public void An_empty_template_uses_the_default()
    {
        Assert.Equal("Hindsound_Game_2026-11-06_14-30-05", FileNameTemplate.Render("", "Game", Moment));
    }

    [Fact]
    public void A_free_template_works()
    {
        Assert.Equal("Game 2026-11-06", FileNameTemplate.Render("{app} {date}", "Game", Moment));
    }

    [Fact]
    public void A_name_that_is_free_is_kept()
    {
        using var folder = new TempFolder();

        var paths = FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", [""], ".mp3");

        Assert.Equal([folder.Combine("clip.mp3")], paths);
    }

    [Fact]
    public void A_name_that_exists_gets_a_number_and_the_existing_file_is_left_alone()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.Combine("clip.mp3"), "first");

        var paths = FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", [""], ".mp3");
        File.WriteAllText(paths[0], "second");
        var third = FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", [""], ".mp3");

        Assert.Equal(folder.Combine("clip (2).mp3"), paths[0]);
        Assert.Equal(folder.Combine("clip (3).mp3"), third[0]);
        Assert.Equal("first", File.ReadAllText(folder.Combine("clip.mp3")));
    }

    [Fact]
    public void Two_tracks_of_a_clip_share_the_same_number_even_if_only_one_exists()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.Combine("clip_mic.wav"), "x");

        var paths = FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", ["_system", "_mic"], ".wav");

        Assert.Equal([folder.Combine("clip (2)_system.wav"), folder.Combine("clip (2)_mic.wav")], paths);
    }
}
