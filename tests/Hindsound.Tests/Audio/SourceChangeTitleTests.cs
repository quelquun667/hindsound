using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Audio;

/// <summary>
/// The title of the PC side, read from a real engine: the status says the app of now, the changes say what came before. The two have
/// to tell one story, even when a choice has not happened yet.
/// </summary>
[Collection(nameof(TimingSensitive))]
public class SourceChangeTitleTests
{
    private const string Slack = "Slack.exe";
    private const string Discord = "Discord.exe";
    private const string Spotify = "Spotify.exe";
    private const int Columns = 100;

    private static FakeProcesses AllRunning() => new(new ProcessEntry(40, 1, Slack), new ProcessEntry(50, 1, Discord), new ProcessEntry(60, 1, Spotify));

    [Fact]
    public async Task A_choice_still_to_happen_does_not_name_a_past_that_never_led_to_it()
    {
        // The process list fails: the Discord capture keeps running, no change is noted, and the line under the title still says Discord.
        var processes = AllRunning();
        using var rig = new AudioTestRig(sourceApp: Slack, processes: processes);
        await rig.Engine.StartAsync();
        rig.Run(2);
        await rig.Engine.SetSourceAppAsync(Discord);
        rig.Run(1);

        processes.Fail = true;
        await rig.Engine.SetSourceAppAsync(Spotify);

        var line = WaveView.MarkerOf(rig.Engine.WaveFacts, Columns);
        Assert.True(line.Shows);
        Assert.Equal(Discord, line.App);
        Assert.Equal(Slack, line.Past);
        Assert.Equal(Spotify, rig.Engine.Status.SourceApp);
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(rig.Engine.Status, line));
    }
}
