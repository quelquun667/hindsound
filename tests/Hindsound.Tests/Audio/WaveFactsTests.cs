using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Audio;

/// <summary>A list of processes that takes its time: the clock moves on while the command is under way.</summary>
internal sealed class SlowProcesses(double seconds, params ProcessEntry[] list) : IProcessSnapshot
{
    public FakeClock? Clock { get; set; }

    public IReadOnlyList<ProcessEntry> Take()
    {
        Clock?.Advance(seconds);
        return list;
    }
}

[Collection(nameof(TimingSensitive))]
public class WaveFactsTests
{
    private const string App = "Discord.exe";
    private const int Rate = InternalFormat.SampleRate;
    private static readonly ProcessEntry Running = new(50, 1, App);

    [Fact]
    public async Task It_is_the_window_a_snapshot_would_take()
    {
        using var rig = new AudioTestRig(bufferSeconds: 5);
        await rig.Engine.StartAsync();
        rig.Run(3);

        var facts = rig.Engine.WaveFacts;

        Assert.Equal(3 * Rate, facts.End - facts.Start);
        Assert.Equal(5 * Rate, facts.BufferFrames);
        Assert.Equal(rig.Engine.Snapshot(TimeSpan.FromHours(1)).Mic.Length, facts.End - facts.Start);
        Assert.NotNull(facts.System);
        Assert.NotNull(facts.Mic);
        Assert.Empty(facts.Changes);

        // Full, it stays at the memory set, though a track holds a little more.
        rig.Run(4);
        facts = rig.Engine.WaveFacts;
        Assert.Equal(5 * Rate, facts.End - facts.Start);
        Assert.True(facts.Mic!.OldestFrame < facts.Start);
    }

    [Fact]
    public async Task A_new_memory_length_shows_at_once()
    {
        using var rig = new AudioTestRig(bufferSeconds: 5);
        await rig.Engine.StartAsync();
        rig.Run(3);

        await rig.Engine.ResizeBufferAsync(TimeSpan.FromSeconds(2));

        var facts = rig.Engine.WaveFacts;
        Assert.Equal(2 * Rate, facts.BufferFrames);
        Assert.Equal(2 * Rate, facts.End - facts.Start);
    }

    [Fact]
    public async Task Reading_it_allocates_nothing()
    {
        using var rig = new AudioTestRig(bufferSeconds: 5);
        await rig.Engine.StartAsync();
        rig.Run(1);
        await rig.Engine.SetSourceAppAsync(null);
        _ = rig.Engine.WaveFacts;

        var engine = rig.Engine;
        Action reads = () =>
        {
            for (var i = 0; i < 1000; i++)
            {
                _ = engine.WaveFacts;
            }
        };

        Assert.Equal(0, WaveRig.LeastAllocated(reads));
    }

    [Fact]
    public void Before_the_start_there_is_nothing_and_nothing_breaks()
    {
        using var rig = new AudioTestRig();

        var facts = rig.Engine.WaveFacts;

        Assert.Null(facts.System);
        Assert.Null(facts.Mic);
        Assert.Equal(facts.Start, facts.End);
        Assert.Empty(facts.Changes);
    }

    [Fact]
    public async Task A_change_of_source_is_noted_where_the_new_capture_starts()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(2);

        await rig.Engine.SetSourceAppAsync(App);
        var there = rig.Engine.WaveFacts.Changes;
        Assert.Equal([new SourceChange(2 * Rate, App)], there);

        rig.Run(1);
        await rig.Engine.SetSourceAppAsync(null);

        Assert.Equal([new SourceChange(2 * Rate, App), new SourceChange(3 * Rate, null, App)], rig.Engine.WaveFacts.Changes);

        // What was read before does not change under the reader.
        Assert.Single(there);
    }

    [Fact]
    public async Task The_note_comes_after_the_new_capture_is_attached_and_not_when_it_was_asked_for()
    {
        var processes = new SlowProcesses(0.5, Running);
        using var rig = new AudioTestRig(processes: processes);
        await rig.Engine.StartAsync();
        rig.Run(2);
        processes.Clock = rig.Clock;

        // The real list of processes takes a moment, and the old capture is still running meanwhile.
        await rig.Engine.SetSourceAppAsync(App);

        Assert.Equal(2 * Rate + Rate / 2, rig.Engine.WaveFacts.Changes[0].Frame);
    }

    [Fact]
    public async Task The_line_is_never_before_the_last_packet_of_the_old_source()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(2);

        // A packet stamped a little ahead of the clock: the track reaches further than the time on the timeline.
        var ahead = new float[4800 * 2];
        rig.SystemSource.Deliver(ahead, rig.Clock.Ticks);
        var lastOldFrame = rig.Engine.WaveFacts.System!.EndFrame;
        Assert.True(lastOldFrame > 2 * Rate);

        await rig.Engine.SetSourceAppAsync(App);

        Assert.True(rig.Engine.WaveFacts.Changes[0].Frame >= lastOldFrame);
    }

    [Fact]
    public async Task A_source_silent_at_the_change_is_marked_at_the_time_of_the_change()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(2);
        rig.Clock.Advance(3);

        await rig.Engine.SetSourceAppAsync(App);

        Assert.Equal(5 * Rate, rig.Engine.WaveFacts.Changes[0].Frame);
    }

    [Fact]
    public async Task While_the_look_up_fails_the_old_capture_runs_on_and_the_change_waits()
    {
        var processes = new FakeProcesses(Running) { Fail = true };
        using var rig = new AudioTestRig(processes: processes);
        await rig.Engine.StartAsync();
        rig.Run(1);
        var oldSource = rig.SystemSource;

        await rig.Engine.SetSourceAppAsync(App);

        Assert.Same(oldSource, rig.SystemSource);
        Assert.Empty(rig.Engine.WaveFacts.Changes);

        rig.Run(1);
        processes.Fail = false;
        await rig.Engine.RefreshDevicesAsync();

        Assert.NotSame(oldSource, rig.SystemSource);
        Assert.Equal([new SourceChange(2 * Rate, App)], rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task The_same_choice_again_notes_nothing()
    {
        using var rig = new AudioTestRig(sourceApp: App, processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(1);

        await rig.Engine.SetSourceAppAsync(App);
        await rig.Engine.SetSourceAppAsync("DISCORD.EXE");
        await rig.Engine.RefreshDevicesAsync();

        Assert.Empty(rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task An_application_that_restarts_is_the_same_choice()
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        rig.Run(1);

        processes.Set();
        await rig.Engine.RefreshDevicesAsync();
        processes.Set(new ProcessEntry(60, 1, App));
        await rig.Engine.RefreshDevicesAsync();

        Assert.Empty(rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task An_application_not_running_is_a_change_too()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses());
        await rig.Engine.StartAsync();
        rig.Run(1);

        await rig.Engine.SetSourceAppAsync(App);

        Assert.True(rig.Engine.Status.SourceAppMissing);
        Assert.Equal([new SourceChange(Rate, App)], rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task A_change_while_paused_stands_where_time_stopped()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(2);
        await rig.Engine.PauseAsync();
        rig.Clock.Advance(5);

        await rig.Engine.SetSourceAppAsync(App);

        Assert.Equal([new SourceChange(2 * Rate, App)], rig.Engine.WaveFacts.Changes);

        await rig.Engine.ResumeAsync();
        Assert.Single(rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task Stopped_nothing_is_noted_and_a_start_forgets_the_old_notes()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));

        await rig.Engine.SetSourceAppAsync(App);
        Assert.Empty(rig.Engine.WaveFacts.Changes);

        await rig.Engine.StartAsync();
        rig.Run(1);
        await rig.Engine.SetSourceAppAsync(null);
        Assert.Single(rig.Engine.WaveFacts.Changes);

        await rig.Engine.StopAsync();
        await rig.Engine.SetSourceAppAsync(App);
        Assert.Single(rig.Engine.WaveFacts.Changes);

        // The frames of the old notes mean nothing on the new timeline.
        await rig.Engine.StartAsync();
        Assert.Empty(rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task Only_the_last_changes_are_kept_the_most_recent_last()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(1);

        for (var i = 0; i < 21; i++)
        {
            await rig.Engine.SetSourceAppAsync(i % 2 == 0 ? App : null);
            rig.Clock.Advance(1);
        }

        var changes = rig.Engine.WaveFacts.Changes;
        Assert.Equal(8, changes.Count);
        Assert.Equal(21 * Rate, changes[7].Frame);
        Assert.Equal(App, changes[7].App);
        Assert.Equal(14 * Rate, changes[0].Frame);
    }

    [Fact]
    public async Task The_changes_are_not_touched_by_a_mic_going_off_and_a_device_change()
    {
        using var rig = new AudioTestRig(processes: new FakeProcesses(Running));
        await rig.Engine.StartAsync();
        rig.Run(1);

        await rig.Engine.SetDevicesAsync(null, null, false);
        await rig.Engine.SetDevicesAsync("speakers", null, true);
        await rig.Engine.RefreshDevicesAsync();

        Assert.Empty(rig.Engine.WaveFacts.Changes);
    }

    [Fact]
    public async Task An_output_with_no_capture_yet_has_no_past_to_mark()
    {
        using var rig = new AudioTestRig(micEnabled: false, processes: new FakeProcesses(Running));
        rig.Factory.SystemDevice = null;
        await rig.Engine.StartAsync();
        Assert.Null(rig.Engine.WaveFacts.System);

        await rig.Engine.SetSourceAppAsync(App);

        Assert.NotNull(rig.Engine.WaveFacts.System);
        Assert.False(WaveView.MarkerOf(rig.Engine.WaveFacts, 60).Shows);
    }

    [Fact]
    public async Task The_mic_turned_off_leaves_no_track_and_turned_on_a_new_one()
    {
        using var rig = new AudioTestRig();
        await rig.Engine.StartAsync();
        rig.Run(1);
        var first = rig.Engine.WaveFacts.Mic;
        Assert.NotNull(first);

        await rig.Engine.SetDevicesAsync(null, null, false);
        Assert.Null(rig.Engine.WaveFacts.Mic);

        await rig.Engine.SetDevicesAsync(null, null, true);
        Assert.NotNull(rig.Engine.WaveFacts.Mic);
        Assert.NotSame(first, rig.Engine.WaveFacts.Mic);
    }
}
