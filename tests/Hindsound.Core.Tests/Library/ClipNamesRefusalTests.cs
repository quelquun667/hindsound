using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The one rule for the name of a clip, rule by rule, in the order the dialog tells them. No file is made: the disk is a set of paths.</summary>
public class ClipNamesRefusalTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "clips");

    private static ClipGroup Line(params string[] names) => LineIn(Folder, names);

    private static ClipGroup LineIn(string folder, params string[] names) =>
        new([.. names.Select(name => new ClipFile(Path.Combine(folder, name), 1, DateTime.UnixEpoch))]);

    /// <summary>The disk as the system sees it: names compared by the rule of paths of this machine.</summary>
    private static Func<string, bool> Disk(params string[] names)
    {
        var paths = new HashSet<string>(names.Select(name => Path.Combine(Folder, name)), PathRules.Comparer);
        return paths.Contains;
    }

    private static readonly Func<string, bool> Nothing = _ => false;

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_name_of_nothing_or_of_spaces_is_empty(string typed) =>
        Assert.Equal(ClipRenameOutcome.Empty, ClipNames.Refusal(Line("a.mp3"), typed, Nothing));

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\tb")]
    [InlineData("a\u0001b")]
    [InlineData("a\u001Fb")]
    public void A_character_a_file_name_can_not_hold_is_refused(string typed) =>
        Assert.Equal(ClipRenameOutcome.BadCharacters, ClipNames.Refusal(Line("a.mp3"), typed, Nothing));

    [Theory]
    [InlineData("  Boss fight")]
    [InlineData("Boss fight")]
    [InlineData("Boss.fight")]
    [InlineData("Raid - 2 (final) #3")]
    [InlineData("Été 中文")]
    public void A_good_name_passes_and_spaces_at_its_start_are_kept(string typed) =>
        Assert.Null(ClipNames.Refusal(Line("a.mp3"), typed, Nothing));

    [Theory]
    [InlineData("a.")]
    [InlineData("a ")]
    [InlineData("a..")]
    [InlineData("a. ")]
    public void A_name_that_ends_with_a_dot_or_a_space_is_refused(string typed) =>
        Assert.Equal(ClipRenameOutcome.TrailingDotOrSpace, ClipNames.Refusal(Line("a.mp3"), typed, Nothing));

    [Theory]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("Aux")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    [InlineData("COM¹")]
    [InlineData("COM²")]
    [InlineData("lpt³")]
    [InlineData("nul.txt")]
    [InlineData("COM1.mp3")]
    [InlineData("CON .mp3")]
    [InlineData("aux.final.wav")]
    public void A_name_Windows_keeps_for_a_device_is_refused_in_any_case_and_with_an_extension(string typed) =>
        Assert.Equal(ClipRenameOutcome.Reserved, ClipNames.Refusal(Line("a.mp3"), typed, Nothing));

    [Theory]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT")]
    [InlineData("CONSOLE")]
    [InlineData("nul_clip")]
    [InlineData("my.con")]
    [InlineData("COM⁴")]
    public void A_name_that_only_starts_like_a_device_passes(string typed) =>
        Assert.Null(ClipNames.Refusal(Line("a.mp3"), typed, Nothing));

    [Fact]
    public void The_rules_are_told_in_their_order()
    {
        var line = Line("a.mp3");

        Assert.Equal(ClipRenameOutcome.BadCharacters, ClipNames.Refusal(line, "a:.", Nothing));
        Assert.Equal(ClipRenameOutcome.TrailingDotOrSpace, ClipNames.Refusal(line, "CON.", Nothing));
        Assert.Equal(ClipRenameOutcome.Reserved, ClipNames.Refusal(line, "nul", _ => true));
        Assert.Equal(ClipRenameOutcome.TooLong, ClipNames.Refusal(line, new string('x', 300), _ => true));
    }

    [Fact]
    public void Each_full_path_stays_under_260_characters()
    {
        var line = Line("a.mp3");

        // The name that makes the full path 259 characters long passes; one more character, and it is too long.
        var longest = 259 - (Folder.Length + 1) - ".mp3".Length;
        Assert.Null(ClipNames.Refusal(line, new string('x', longest), Nothing));
        Assert.Equal(ClipRenameOutcome.TooLong, ClipNames.Refusal(line, new string('x', longest + 1), Nothing));
    }

    [Fact]
    public void Every_file_of_a_clip_in_two_files_counts_for_the_length()
    {
        var line = Line("old_system.mp3", "old_mic.mp3");

        // "_system" is the longer suffix: the name that fits the mic file can still be too long for the system file.
        var longest = 259 - (Folder.Length + 1) - "_system.mp3".Length;
        Assert.Null(ClipNames.Refusal(line, new string('x', longest), Nothing));
        Assert.Equal(ClipRenameOutcome.TooLong, ClipNames.Refusal(line, new string('x', longest + 1), Nothing));
    }

    [Fact]
    public void Each_file_name_stays_under_256_characters()
    {
        // At the root of the drive the path is short enough: only the name of the file is too long.
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var line = LineIn(root, "a.mp3");

        Assert.Null(ClipNames.Refusal(line, new string('x', 251), Nothing));
        Assert.Equal(ClipRenameOutcome.TooLong, ClipNames.Refusal(line, new string('x', 252), Nothing));
    }

    [Fact]
    public void A_name_another_file_has_is_taken()
    {
        Assert.Equal(ClipRenameOutcome.Taken, ClipNames.Refusal(Line("a.mp3"), "Boss", Disk("a.mp3", "Boss.mp3")));
        Assert.Null(ClipNames.Refusal(Line("a.mp3"), "Boss", Disk("a.mp3", "Boss.wav")));
    }

    [Fact]
    public void A_name_another_file_has_in_another_case_is_taken_where_the_disk_ignores_case()
    {
        var refusal = ClipNames.Refusal(Line("a.mp3"), "boss", Disk("a.mp3", "Boss.mp3"));

        Assert.Equal(PathRules.IgnoresCase ? ClipRenameOutcome.Taken : null, refusal);
    }

    [Fact]
    public void Changing_the_case_alone_is_allowed()
    {
        // Where the disk ignores case, the new name is "there": it is the clip itself.
        Assert.Null(ClipNames.Refusal(Line("boss.mp3"), "Boss", Disk("boss.mp3")));
        Assert.Null(ClipNames.Refusal(Line("raid_system.mp3", "raid_mic.mp3"), "RAID", Disk("raid_system.mp3", "raid_mic.mp3")));
    }

    [Fact]
    public void Keeping_the_same_name_is_allowed()
    {
        Assert.Null(ClipNames.Refusal(Line("boss.mp3"), "boss", Disk("boss.mp3")));
    }

    [Fact]
    public void The_mic_file_alone_of_a_clip_in_two_files_makes_the_name_taken()
    {
        var line = Line("old_system.mp3", "old_mic.mp3");

        Assert.Equal(ClipRenameOutcome.Taken, ClipNames.Refusal(line, "Raid", Disk("old_system.mp3", "old_mic.mp3", "Raid_mic.mp3")));
        Assert.Null(ClipNames.Refusal(line, "Raid", Disk("old_system.mp3", "old_mic.mp3", "Raid.mp3")));
    }

    [Fact]
    public void A_file_of_the_line_is_never_asked_about()
    {
        var asked = new List<string>();
        var line = Line("old_system.mp3", "old_mic.mp3");

        Assert.Null(ClipNames.Refusal(line, "old", path =>
        {
            asked.Add(path);
            return true;
        }));
        Assert.Empty(asked);
    }
}
