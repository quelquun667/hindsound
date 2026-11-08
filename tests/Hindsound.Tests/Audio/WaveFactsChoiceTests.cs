using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

[Collection(nameof(TimingSensitive))]
public class WaveFactsChoiceTests
{
    private const string App = "Discord.exe";
    private const string Other = "Music.exe";
    private const int Rate = InternalFormat.SampleRate;
    private static readonly ProcessEntry Running = new(50, 1, App);
    private static readonly ProcessEntry OtherRunning = new(51, 1, Other);

    [Fact]
    public async Task A_choice_taken_back_while_paused_leaves_no_line()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(2);
        await rig.Engine.PauseAsync();

        await rig.Engine.SetSourceAppAsync(App);
        Assert.Single(rig.Engine.WaveFacts.Changes);
        await rig.Engine.SetSourceAppAsync(null);

        Assert.Empty(rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task A_choice_taken_back_while_paused_keeps_the_line_of_the_choice_before()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(2);
        await rig.Engine.SetSourceAppAsync(App);
        rig.Run(1);
        await rig.Engine.PauseAsync();

        await rig.Engine.SetSourceAppAsync(null);
        await rig.Engine.SetSourceAppAsync(App);

        Assert.Equal([new SourceChange(2 * Rate, App)], rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task Several_choices_while_paused_leave_the_last_one_alone()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running, OtherRunning));
        await rig.Engine.StartAsync();
        rig.Run(2);
        await rig.Engine.PauseAsync();

        await rig.Engine.SetSourceAppAsync(Other);
        await rig.Engine.SetSourceAppAsync(App);

        Assert.Equal([new SourceChange(2 * Rate, App)], rig.Engine.WaveFacts.Changes);
    }
}
