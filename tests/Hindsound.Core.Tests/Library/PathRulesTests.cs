using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>What the system decides about a path: case, separators and the key that stands for a folder.</summary>
public sealed class PathRulesTests
{
    private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ClipFile File(string path) => new(path, 1000, T);

    [Fact]
    public void Where_case_is_ignored_two_spellings_are_one_name()
    {
        var comparer = PathRules.ComparerFor(true);

        Assert.True(comparer.Equals("Clips/Game.mp3", "clips/game.MP3"));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, PathRules.ComparisonFor(true));
    }

    [Fact]
    public void Where_case_counts_two_spellings_are_two_names()
    {
        var comparer = PathRules.ComparerFor(false);

        Assert.False(comparer.Equals("Clips/Game.mp3", "clips/game.mp3"));
        Assert.True(comparer.Equals("Clips/Game.mp3", "Clips/Game.mp3"));
        Assert.Equal(StringComparison.Ordinal, PathRules.ComparisonFor(false));
        Assert.Equal(2, new HashSet<string>(["a/Game.mp3", "a/game.mp3"], comparer).Count);
    }

    [Fact]
    public void The_comparer_of_this_system_follows_its_file_system()
    {
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(expected, PathRules.IgnoresCase);
        Assert.Equal(expected, PathRules.Comparer.Equals("A", "a"));
        Assert.Equal(expected, string.Equals("A", "a", PathRules.Comparison));
    }

    [Fact]
    public void The_portable_key_leaves_out_the_separator_at_the_end_only()
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Clips"));

        Assert.Equal(folder, PathRules.PortableKey(folder + Path.DirectorySeparatorChar));
        Assert.Equal(folder, PathRules.PortableKey(folder));
        Assert.Equal(Path.Combine(folder, "a.mp3"), PathRules.PortableKey(Path.Combine(folder, "a.mp3")));
        Assert.Equal(string.Empty, PathRules.PortableKey(string.Empty));
    }

    [Fact]
    public void The_key_of_a_folder_with_and_without_its_last_separator_is_one()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Clips");

        Assert.Equal(ClipFolders.Key(folder), ClipFolders.Key(folder + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void On_Windows_the_key_is_the_Windows_rule()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(ClipFolders.WindowsKey(@"\\?\D:\Clips\"), ClipFolders.Key(@"\\?\D:\Clips\"));
        Assert.Equal(@"D:\Clips", ClipFolders.Key("D:/Clips/"));
    }

    [Fact]
    public void Off_Windows_the_key_of_a_path_is_the_path_itself()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The preview opens the file with this text: a backslash made of the slashes would name a file that is not there.
        Assert.Equal("/home/u/Music/Hindsound/a.mp3", ClipFolders.Key("/home/u/Music/Hindsound/a.mp3"));
        Assert.Equal("/home/u/Clips", ClipFolders.Key("/home/u/Clips/"));
        Assert.Equal(ClipFolders.Key("/home/u/Clips"), ClipFolders.Key("/home/u/Clips/"));
        Assert.NotEqual(ClipFolders.Key("/home/u/Clips"), ClipFolders.Key("/home/u/clips"));
    }

    [Fact]
    public void Off_Windows_two_clips_that_differ_only_by_case_stay_two_clips()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var upper = new ClipGroup([File("/home/u/Clips/Hindsound_Game_2026-11-08_12-00-00.mp3")]);
        var lower = new ClipGroup([File("/home/u/Clips/Hindsound_game_2026-11-08_12-00-00.mp3")]);
        var scan = new ClipScan([upper, lower], ["/home/u/Clips"]);
        var entry = new LibraryClip
        {
            Files = [new LibraryFile { Path = upper.Main.Path, Size = 1000, Written = T }],
            Collections = ["c1"],
        };

        var match = ClipIndexMatch.Match([entry], scan);

        Assert.Equal(["c1"], match.LabelsOf(upper).CollectionIds);
        Assert.Empty(match.LabelsOf(lower).CollectionIds);
    }
}
