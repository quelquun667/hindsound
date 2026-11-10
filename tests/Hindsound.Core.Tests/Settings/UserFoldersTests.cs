using System.IO;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Settings;
using Hindsound.Updates;

namespace Hindsound.Tests.Settings;

public sealed class UserFoldersTests
{
    private static readonly string Existing = Path.Combine(Path.GetTempPath(), "hindsound-user-folders", "Music");

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Music")]
    [InlineData("relative/Music")]
    public void A_folder_the_system_does_not_know_is_none_never_a_relative_path(string? given)
    {
        Assert.Null(UserFolders.Find(Environment.SpecialFolder.MyMusic, _ => given));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Music")]
    public void The_files_under_a_missing_folder_go_to_a_full_path_of_their_own(string? given)
    {
        var clips = UserFolders.Under(Environment.SpecialFolder.MyMusic, _ => given, "Hindsound");

        Assert.True(Path.IsPathFullyQualified(clips), clips);
        Assert.Equal(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Hindsound", "MyMusic", "Hindsound"), clips);
    }

    [Fact]
    public void Each_missing_folder_has_its_own_stand_in()
    {
        var settings = UserFolders.Under(Environment.SpecialFolder.ApplicationData, _ => "", "Hindsound", "settings.json");
        var log = UserFolders.Under(Environment.SpecialFolder.LocalApplicationData, _ => "", "Hindsound", "hindsound.log");
        var clips = UserFolders.Under(Environment.SpecialFolder.MyMusic, _ => "", "Hindsound");

        Assert.Equal(3, new[] { Path.GetDirectoryName(settings), Path.GetDirectoryName(log), clips }.Distinct().Count());
        Assert.All([settings, log, clips], path => Assert.True(Path.IsPathFullyQualified(path), path));
    }

    [Fact]
    public void A_known_folder_is_kept_as_the_system_gives_it_whether_it_exists_or_not()
    {
        Assert.False(Directory.Exists(Existing));

        Assert.Equal(Existing, UserFolders.Find(Environment.SpecialFolder.MyMusic, _ => Existing));
        Assert.Equal(Path.Combine(Existing, "Hindsound"), UserFolders.Under(Environment.SpecialFolder.MyMusic, _ => Existing, "Hindsound"));
    }

    [Fact]
    public void Every_folder_the_system_names_is_kept_even_when_it_does_not_exist()
    {
        // A folder absent on the machine (OEM Links, a Music folder never made) is where a check of existence would show:
        // without DoNotVerify, GetFolderPath gives an empty string for it.
        foreach (var folder in Enum.GetValues<Environment.SpecialFolder>())
        {
            var named = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
            Assert.Equal(named.Length > 0 && Path.IsPathFullyQualified(named) ? named : null, UserFolders.Find(folder));
        }
    }

    [Fact]
    public void The_folder_asked_for_is_the_one_resolved()
    {
        var asked = new List<Environment.SpecialFolder>();
        UserFolders.Under(Environment.SpecialFolder.LocalApplicationData, folder => { asked.Add(folder); return Existing; }, "x");

        Assert.Equal([Environment.SpecialFolder.LocalApplicationData], asked);
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.ApplicationData)]
    [InlineData(Environment.SpecialFolder.LocalApplicationData)]
    [InlineData(Environment.SpecialFolder.MyMusic)]
    [InlineData(Environment.SpecialFolder.UserProfile)]
    public void A_folder_that_exists_is_the_same_as_before(Environment.SpecialFolder folder)
    {
        // Before, the app asked without DoNotVerify: the same path whenever the folder exists.
        var before = Environment.GetFolderPath(folder);
        if (before.Length > 0)
        {
            Assert.Equal(before, UserFolders.Find(folder));
        }
    }

    [Fact]
    public void The_files_of_the_app_are_where_they_were_when_the_folders_exist()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        if (roaming.Length > 0)
        {
            Assert.Equal(Path.Combine(roaming, "Hindsound", "settings.json"), SettingsStore.DefaultPath);
        }

        if (local.Length > 0)
        {
            Assert.Equal(Path.Combine(local, "Hindsound", "hindsound.log"), Journal.PathFor(null));
            Assert.Equal(Path.Combine(local, "Hindsound", "Rescued"), ClipSaver.DefaultRescueFolder);
            Assert.Equal(Path.Combine(local, "Hindsound", "updates"), UpdateStateStore.DefaultFolder);
        }

        if (music.Length > 0)
        {
            Assert.Equal(Path.Combine(music, "Hindsound"), ExportOptions.DefaultOutputFolder());
        }
    }

    [Fact]
    public void The_files_of_the_app_are_never_relative()
    {
        Assert.All(
            [SettingsStore.DefaultPath, Journal.PathFor(null), ClipSaver.DefaultRescueFolder, UpdateStateStore.DefaultFolder, ExportOptions.DefaultOutputFolder()],
            path => Assert.True(Path.IsPathFullyQualified(path), path));
    }
}
