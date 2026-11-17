using Hindsound.Library;

namespace Hindsound.Tests.Library;

public class ClipNamesTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "clips");

    private static ClipGroup Line(params string[] names) =>
        new([.. names.Select(name => new ClipFile(Path.Combine(Folder, name), 1, DateTime.UnixEpoch))]);

    [Fact]
    public void A_clip_in_one_file_is_edited_without_its_extension()
    {
        var line = Line("Hindsound_Discord_2026-11-08_20-47-51.mp3");

        Assert.Equal("Hindsound_Discord_2026-11-08_20-47-51", ClipNames.EditableName(line));
        Assert.Equal([Path.Combine(Folder, "Boss fight.mp3")], ClipNames.TargetPaths(line, "Boss fight"));
    }

    [Fact]
    public void A_clip_in_one_file_keeps_a_name_that_only_looks_like_a_track()
    {
        var line = Line("voice_mic.wav");

        Assert.Equal("voice_mic", ClipNames.EditableName(line));
        Assert.Equal([Path.Combine(Folder, "new.wav")], ClipNames.TargetPaths(line, "new"));
    }

    [Fact]
    public void A_clip_in_two_files_keeps_the_suffix_of_each_track()
    {
        var line = Line("Hindsound_2026-11-08_system.mp3", "Hindsound_2026-11-08_mic.mp3");

        Assert.Equal("Hindsound_2026-11-08", ClipNames.EditableName(line));
        Assert.Equal(
            [Path.Combine(Folder, "Raid_system.mp3"), Path.Combine(Folder, "Raid_mic.mp3")],
            ClipNames.TargetPaths(line, "Raid"));
    }

    [Fact]
    public void An_empty_name_is_refused()
    {
        var line = Line("a.mp3");

        Assert.Equal(ClipRenameOutcome.Empty, ClipNames.Refusal(line, "  ", _ => false));
    }
}
