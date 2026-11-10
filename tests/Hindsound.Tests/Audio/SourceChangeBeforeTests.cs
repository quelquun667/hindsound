using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

/// <summary>Each change of source keeps what was captured just before it: the title of the wave names that past.</summary>
[Collection(nameof(TimingSensitive))]
public class SourceChangeBeforeTests
{
    private const string Discord = "Discord.exe";
    private const string Spotify = "Spotify.exe";
    private const int Rate = InternalFormat.SampleRate;

    private static FakeProcesses BothRunning() => new(new ProcessEntry(50, 1, Discord), new ProcessEntry(60, 1, Spotify));

    [Fact]
    public async Task A_change_from_the_whole_PC_has_no_app_before_it_and_the_next_one_has_the_first_app()
    {
        using var rig = new AudioTestRig(processes: BothRunning());
        await rig.Engine.StartAsync();
        rig.Run(2);

        await rig.Engine.SetSourceAppAsync(Discord);
        rig.Run(1);
        await rig.Engine.SetSourceAppAsync(Spotify);
        rig.Run(1);
        await rig.Engine.SetSourceAppAsync(null);

        Assert.Equal(
            [new SourceChange(2 * Rate, Discord, null), new SourceChange(3 * Rate, Spotify, Discord), new SourceChange(4 * Rate, null, Spotify)],
            rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task The_app_chosen_at_the_start_is_what_the_first_change_comes_after()
    {
        using var rig = new AudioTestRig(sourceApp: Discord, processes: BothRunning());
        await rig.Engine.StartAsync();
        rig.Run(2);

        await rig.Engine.SetSourceAppAsync(Spotify);

        Assert.Equal([new SourceChange(2 * Rate, Spotify, Discord)], rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task A_resize_of_the_memory_keeps_the_app_before_the_change()
    {
        using var rig = new AudioTestRig(bufferSeconds: 10, sourceApp: Discord, processes: BothRunning());
        await rig.Engine.StartAsync();
        rig.Run(4);
        await rig.Engine.SetSourceAppAsync(Spotify);
        rig.Run(1);

        await rig.Engine.ResizeBufferAsync(TimeSpan.FromSeconds(3));
        rig.Run(1);
        await rig.Engine.ResizeBufferAsync(TimeSpan.FromSeconds(30));

        Assert.Equal([new SourceChange(4 * Rate, Spotify, Discord)], rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task A_second_choice_while_paused_comes_after_what_the_first_one_came_after()
    {
        // Paused, both choices land on one frame and the second replaces the first: the past before that frame is still the whole PC.
        using var rig = new AudioTestRig(processes: BothRunning());
        await rig.Engine.StartAsync();
        rig.Run(2);
        await rig.Engine.PauseAsync();

        await rig.Engine.SetSourceAppAsync(Discord);
        await rig.Engine.SetSourceAppAsync(Spotify);

        Assert.Equal([new SourceChange(2 * Rate, Spotify, null)], rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task The_oldest_change_kept_still_knows_what_came_before_it_once_older_ones_are_dropped()
    {
        using var rig = new AudioTestRig(processes: BothRunning());
        await rig.Engine.StartAsync();
        rig.Run(1);

        string?[] order = [Discord, Spotify, null];
        for (var i = 0; i < 19; i++)
        {
            await rig.Engine.SetSourceAppAsync(order[i % 3]);
            rig.Clock.Advance(1);
        }

        var changes = rig.Engine.WaveFacts.Changes;
        Assert.Equal(8, changes.Count);

        // Change 11 of 0 to 18 is the oldest kept, the whole PC again: chosen after change 10, which was Spotify and is dropped.
        Assert.Null(changes[0].App);
        Assert.Equal(Spotify, changes[0].Before);
        for (var i = 1; i < changes.Count; i++)
        {
            Assert.Equal(changes[i - 1].App, changes[i].Before);
        }
    }
}
