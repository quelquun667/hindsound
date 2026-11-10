using Hindsound.Audio;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Audio;

/// <summary>
/// The moment right after a choice, before any new audio is held: the line sits on the last column with every bar from before it, and the
/// title already has to name the past, or it would say "Whole PC" for the first second of each change.
/// </summary>
[Collection(nameof(TimingSensitive))]
public class SourceChangeRightAfterTests
{
    private const string Discord = "Discord.exe";
    private const string Spotify = "Spotify.exe";
    private const int Columns = 100;

    [Fact]
    public void A_change_on_the_last_frame_held_puts_the_line_on_the_last_column_and_keeps_the_past()
    {
        // The change is at the very end of the memory: no bar comes after it, so every one is from before it.
        var facts = NowRig.FullUntil(null, null, 100, new SourceChange(100L * NowRig.Rate, Spotify, Discord));

        var line = WaveView.MarkerOf(facts, 60);

        Assert.True(line.Shows);
        Assert.True(line.Mixed);
        Assert.Equal(59, line.Column);
        Assert.Equal(Discord, line.Past);
        Assert.Equal("Discord, then Spotify", WaveView.SystemLaneTitle(ScreenRig.Capturing() with { SourceApp = Spotify }, line));
    }

    [Fact]
    public async Task Right_after_a_choice_with_nothing_new_yet_the_title_already_names_both()
    {
        using var rig = new AudioTestRig(sourceApp: Discord, processes: new FakeProcesses(new ProcessEntry(50, 1, Discord), new ProcessEntry(60, 1, Spotify)));
        await rig.Engine.StartAsync();
        rig.Run(2);

        await rig.Engine.SetSourceAppAsync(Spotify);

        var line = WaveView.MarkerOf(rig.Engine.WaveFacts, Columns);
        Assert.True(line.Shows);
        Assert.True(line.Mixed);
        Assert.Equal(Columns - 1, line.Column);
        Assert.Equal(Discord, line.Past);
        Assert.Equal("Discord, then Spotify", WaveView.SystemLaneTitle(rig.Engine.Status, line));
    }
}
