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

    [Fact]
    public void The_reserved_paths_exist_so_nobody_else_can_take_them()
    {
        using var folder = new TempFolder();

        var first = FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", ["_system", "_mic"], ".wav");
        var second = FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", ["_system", "_mic"], ".wav");

        Assert.All(first, p => Assert.True(File.Exists(p)));
        Assert.Equal([folder.Combine("clip (2)_system.wav"), folder.Combine("clip (2)_mic.wav")], second);
        Assert.Equal(4, Directory.GetFiles(folder.Path).Length);
    }

    [Fact]
    public void A_taken_second_file_releases_the_first_one_it_had_created()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.Combine("clip_mic.wav"), "x");

        FileNameTemplate.ReserveUniquePaths(folder.Path, "clip", ["_system", "_mic"], ".wav");

        Assert.False(File.Exists(folder.Combine("clip_system.wav"))); // attempt 1 was undone
        Assert.True(File.Exists(folder.Combine("clip (2)_system.wav")));
    }

    private const string Emoji = "\U0001F3AE"; // two chars

    [Theory]
    [InlineData(39)] // the app's own limit of 40 falls in the middle of the emoji
    [InlineData(40)]
    public void A_long_app_name_is_never_cut_inside_a_character(int before)
    {
        var name = FileNameTemplate.Render("{app}", new string('a', before) + Emoji + "bbb", Moment);

        AssertWholeCharacters(name);
    }

    [Fact]
    public void A_long_template_is_never_cut_inside_a_character()
    {
        var name = FileNameTemplate.Render(new string('a', 149) + Emoji, null, Moment);

        Assert.Equal(new string('a', 149), name);
    }

    [Fact]
    public void A_character_that_fits_stays_whole()
    {
        Assert.Equal("a" + Emoji + "b", FileNameTemplate.Render("{app}", "a" + Emoji + "b", Moment));
        Assert.Equal(new string('a', 38) + Emoji, FileNameTemplate.Render("{app}", new string('a', 38) + Emoji, Moment));
    }

    [Fact]
    public void Half_a_character_in_a_title_is_removed()
    {
        // Not as test data: an attribute cannot hold half a character.
        Assert.Equal("ab", FileNameTemplate.Render("{app}", "a\uD83Cb", Moment)); // first half alone
        Assert.Equal("ab", FileNameTemplate.Render("{app}", "a\uDFAEb", Moment)); // second half alone
        Assert.Equal("ab", FileNameTemplate.Render("{app}", "a\uDFAE\uD83Cb", Moment)); // the two halves in the wrong order
        Assert.Equal("a", FileNameTemplate.Render("{app}", "a\uD83C", Moment)); // at the very end
    }

    private static void AssertWholeCharacters(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsHighSurrogate(name[i]))
            {
                Assert.True(i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]), "half a character at " + i);
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(name[i]), "half a character at " + i);
            }
        }
    }
}
