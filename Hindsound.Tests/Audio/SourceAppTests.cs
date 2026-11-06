using System.Runtime.InteropServices;
using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

internal sealed class FakeProcesses : IProcessSnapshot
{
    private int _takes;
    private volatile IReadOnlyList<ProcessEntry> _list;

    public FakeProcesses(params ProcessEntry[] list) => _list = list;

    public bool Fail { get; set; }

    public int Takes => Volatile.Read(ref _takes);

    public void Set(params ProcessEntry[] list) => _list = list;

    public IReadOnlyList<ProcessEntry> Take()
    {
        Interlocked.Increment(ref _takes);
        return Fail ? throw new COMException("snapshot failed") : _list;
    }
}

public class SourceAppFinderTests
{
    private const string App = "Discord.exe";

    [Fact]
    public void An_application_that_is_not_running_gives_nothing()
    {
        Assert.Null(SourceAppFinder.FindCaptureTarget([new ProcessEntry(10, 1, "other.exe")], App));
        Assert.Null(SourceAppFinder.FindCaptureTarget([], App));
    }

    [Fact]
    public void The_ancestor_of_the_same_name_is_chosen_whatever_the_order()
    {
        ProcessEntry[] list =
        [
            new(21, 20, App),
            new(22, 21, App),
            new(20, 5, App),
            new(5, 1, "Update.exe"),
        ];

        Assert.Equal(20, SourceAppFinder.FindCaptureTarget(list, App));
    }

    [Fact]
    public void A_parent_with_another_name_does_not_count()
    {
        ProcessEntry[] list = [new(5, 1, "launcher.exe"), new(6, 5, "helper.exe"), new(7, 6, App)];

        Assert.Equal(7, SourceAppFinder.FindCaptureTarget(list, App));
    }

    [Fact]
    public void With_two_instances_the_first_listed_is_taken_as_the_oldest()
    {
        ProcessEntry[] list = [new(30, 1, App), new(31, 30, App), new(40, 1, App), new(41, 40, App)];

        Assert.Equal(30, SourceAppFinder.FindCaptureTarget(list, App));
    }

    [Fact]
    public void The_name_is_compared_without_regard_to_case()
    {
        Assert.Equal(8, SourceAppFinder.FindCaptureTarget([new ProcessEntry(8, 1, "DISCORD.EXE")], App));
    }

    [Fact]
    public void A_parent_gone_or_a_loop_of_reused_ids_still_gives_an_answer()
    {
        Assert.Equal(8, SourceAppFinder.FindCaptureTarget([new ProcessEntry(8, 9999, App)], App));
        Assert.Equal(8, SourceAppFinder.FindCaptureTarget([new ProcessEntry(8, 8, App)], App));
        Assert.Equal(8, SourceAppFinder.FindCaptureTarget([new ProcessEntry(8, 9, App), new ProcessEntry(9, 8, App)], App));
    }
}

public class AudioAppListTests
{
    private static readonly ProcessEntry[] Processes =
    [
        new(100, 1, "Hindsound.exe"),
        new(200, 1, "Discord.exe"),
        new(201, 200, "Discord.exe"),
        new(300, 1, "game.exe"),
        new(400, 1, "player.exe"),
    ];

    [Fact]
    public void Each_application_comes_once_without_this_app_or_the_system_sounds()
    {
        var apps = AudioAppList.Build([(100, "Us"), (200, null), (201, null), (0, "System sounds"), (300, "The Game")], Processes, 100);

        Assert.Equal([new AudioAppInfo("Discord.exe", "Discord"), new AudioAppInfo("game.exe", "The Game")], apps);
    }

    [Fact]
    public void A_name_that_is_a_resource_reference_gives_way_to_the_exe_name()
    {
        var apps = AudioAppList.Build([(400, @"@%SystemRoot%\System32\player.dll,-1")], Processes, 100);

        Assert.Equal([new AudioAppInfo("player.exe", "player")], apps);
    }

    [Fact]
    public void A_better_name_from_a_second_process_of_the_application_is_kept()
    {
        var apps = AudioAppList.Build([(200, null), (201, "Discord Voice")], Processes, 100);

        Assert.Equal([new AudioAppInfo("Discord.exe", "Discord Voice")], apps);
    }

    [Fact]
    public void A_session_whose_process_is_gone_is_left_out()
    {
        Assert.Empty(AudioAppList.Build([(999, "Gone")], Processes, 100));
    }
}

[Collection(nameof(TimingSensitive))]
public class SourceAppCaptureTests
{
    private const string App = "Discord.exe";
    private const int Rate = InternalFormat.SampleRate;
    private static readonly ProcessEntry Running = new(50, 1, App);
    private static readonly ProcessEntry Restarted = new(60, 1, App);

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    private static int ZeroFrames(float[] stereo)
    {
        var zeros = 0;
        for (var i = 0; i < stereo.Length; i += 2)
        {
            zeros += stereo[i] == 0f ? 1 : 0;
        }

        return zeros;
    }

    // Wherever the PC side holds sound, it holds the very frame the microphone holds at that place.
    private static void AssertAligned(AudioSnapshot snapshot)
    {
        Assert.Equal(snapshot.Mic.Length * 2, snapshot.System.Length);
        for (var frame = 0; frame < snapshot.Mic.Length; frame++)
        {
            var left = snapshot.System[frame * 2];
            if (left != 0f && left != snapshot.Mic[frame])
            {
                Assert.Fail($"Sources differ at frame {frame}: {left} and {snapshot.Mic[frame]}");
            }
        }
    }

    [Fact]
    public async Task An_application_not_running_is_no_error_and_the_status_stays_capturing()
    {
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: new FakeProcesses());

        await rig.Engine.StartAsync();

        var status = rig.Engine.Status;
        Assert.Equal(CaptureActivity.Capturing, status.Activity);
        Assert.True(status.SourceAppMissing);
        Assert.Equal(App, status.SourceApp);
        Assert.False(status.SystemCapturing);
        Assert.Empty(rig.Factory.Opened);

        // Its silence is a clip like any other, of the length asked for.
        rig.Clock.Advance(4);
        var snapshot = rig.Engine.Snapshot(Seconds(3));
        Assert.Equal(3 * 2 * Rate, snapshot.System.Length);
        Assert.All(snapshot.System, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public async Task An_application_is_captured_with_its_process_tree_and_not_the_endpoint()
    {
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: new FakeProcesses(new ProcessEntry(51, 50, App), Running));
        rig.Factory.SystemDevice = null;

        await rig.Engine.StartAsync();

        var status = rig.Engine.Status;
        Assert.Equal(CaptureActivity.Capturing, status.Activity);
        Assert.False(status.SourceAppMissing);
        Assert.True(status.SystemCapturing);
        Assert.False(status.OwnSoundsExcluded);
        Assert.Equal(50, rig.SystemSource.Request.TargetProcessId);
        Assert.Equal(App, rig.SystemSource.Request.TargetName);
        Assert.False(rig.SystemSource.Request.ExcludeOwnProcess);
    }

    [Fact]
    public async Task An_application_takes_precedence_over_a_chosen_output()
    {
        using var rig = new AudioTestRig(micEnabled: false, systemDeviceId: "speakers", sourceApp: App, processes: new FakeProcesses(Running));

        await rig.Engine.StartAsync();

        Assert.Single(rig.Factory.Opened);
        Assert.Equal(50, rig.SystemSource.Request.TargetProcessId);

        await rig.Engine.SetDevicesAsync("speakers", null, false);
        Assert.Single(rig.Factory.Opened);
    }

    [Fact]
    public async Task An_application_that_starts_later_lines_up_with_the_mic()
    {
        var processes = new FakeProcesses();
        using var rig = new AudioTestRig(sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        rig.Run(2, system: false);

        processes.Set(Running);
        await rig.Engine.RefreshDevicesAsync();
        Assert.False(rig.Engine.Status.SourceAppMissing);
        rig.Run(2);

        var snapshot = rig.Engine.Snapshot(Seconds(4));
        Assert.Equal(4 * Rate, snapshot.Mic.Length);
        AssertAligned(snapshot);
        Assert.DoesNotContain(0f, snapshot.Mic);
        Assert.Equal(2 * Rate, ZeroFrames(snapshot.System));
        Assert.Equal(Ramp.ThroughBuffer(2 * Rate), snapshot.System[2 * Rate * 2]);
    }

    [Fact]
    public async Task While_it_is_missing_it_is_looked_for_every_three_ticks_and_not_once_it_is_there()
    {
        var processes = new FakeProcesses();
        using var rig = new AudioTestRig(micEnabled: false, tick: TimeSpan.FromMilliseconds(100), sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();

        await Task.Delay(1000);
        Assert.InRange(processes.Takes, 2, 5);

        processes.Set(Running);
        await Eventually.True(() => rig.Engine.Status.SystemCapturing, "attached with no other command");
        Assert.False(rig.Engine.Status.SourceAppMissing);

        var takes = processes.Takes;
        await Task.Delay(500);
        Assert.Equal(takes, processes.Takes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public async Task An_application_that_closes_and_comes_back_is_attached_again_and_stays_aligned(double secondsRunning)
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(tick: TimeSpan.FromMilliseconds(20), sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        var first = rig.SystemSource;
        rig.Run(secondsRunning);

        processes.Set();
        first.Die(new COMException("process gone"));
        await Eventually.True(() => rig.Engine.Status.SourceAppMissing, "reported as missing");
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
        Assert.False(rig.Engine.Status.SystemCapturing);
        rig.Run(2, system: false);

        processes.Set(Restarted);
        await Eventually.True(() => rig.Engine.Status.SystemCapturing, "attached to the new instance");
        Assert.Equal(60, rig.SystemSource.Request.TargetProcessId);
        rig.Run(2);

        var length = secondsRunning + 4;
        var snapshot = rig.Engine.Snapshot(Seconds(length));
        Assert.Equal((int)(length * Rate), snapshot.Mic.Length);
        AssertAligned(snapshot);
        Assert.DoesNotContain(0f, snapshot.Mic);
        Assert.Equal(2 * Rate, ZeroFrames(snapshot.System));
    }

    [Fact]
    public async Task With_the_mic_off_time_goes_on_while_the_application_is_away()
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        var first = rig.SystemSource;
        rig.Run(2, mic: false);

        processes.Set();
        first.Die(new COMException("process gone"));
        await Eventually.True(() => rig.Engine.Status.SourceAppMissing, "reported as missing");
        rig.Clock.Advance(3);

        var snapshot = rig.Engine.Snapshot(Seconds(5));
        Assert.Equal(5 * 2 * Rate, snapshot.System.Length);
        Assert.Equal(3 * Rate, ZeroFrames(snapshot.System));
    }

    [Fact]
    public async Task A_second_instance_does_not_pull_the_capture_away()
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        var source = rig.SystemSource;

        processes.Set(Running, Restarted);
        await rig.Engine.RefreshDevicesAsync();

        Assert.Same(source, rig.SystemSource);
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task A_refresh_notices_the_process_has_gone_even_if_the_capture_said_nothing()
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        var source = rig.SystemSource;

        processes.Set();
        await rig.Engine.RefreshDevicesAsync();

        Assert.True(source.Disposed);
        Assert.True(rig.Engine.Status.SourceAppMissing);
    }

    [Fact]
    public async Task Changing_on_the_fly_and_going_back_to_the_whole_pc_excludes_our_sounds_again()
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(micEnabled: false, processes: processes);
        await rig.Engine.StartAsync();
        var wholePc = rig.SystemSource;
        Assert.True(wholePc.Request.ExcludeOwnProcess);

        await rig.Engine.SetSourceAppAsync(App);
        var app = rig.SystemSource;
        Assert.True(wholePc.Disposed);
        Assert.Equal(50, app.Request.TargetProcessId);
        Assert.False(rig.Engine.Status.OwnSoundsExcluded);

        await rig.Engine.SetSourceAppAsync(null);
        Assert.True(app.Disposed);
        Assert.Null(rig.SystemSource.Request.TargetProcessId);
        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);
        Assert.True(rig.Engine.Status.OwnSoundsExcluded);
        Assert.Null(rig.Engine.Status.SourceApp);
    }

    [Fact]
    public async Task Going_back_to_the_whole_pc_while_it_is_missing_clears_the_missing_state()
    {
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: new FakeProcesses());
        await rig.Engine.StartAsync();

        await rig.Engine.SetSourceAppAsync(null);

        Assert.False(rig.Engine.Status.SourceAppMissing);
        Assert.True(rig.SystemSource.Request.ExcludeOwnProcess);
    }

    [Fact]
    public async Task A_paused_engine_does_not_say_the_application_is_missing()
    {
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: new FakeProcesses());
        await rig.Engine.StartAsync();

        await rig.Engine.PauseAsync();
        Assert.False(rig.Engine.Status.SourceAppMissing);
        Assert.Equal(CaptureActivity.Paused, rig.Engine.Status.Activity);

        await rig.Engine.ResumeAsync();
        Assert.True(rig.Engine.Status.SourceAppMissing);
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
    }

    [Fact]
    public async Task A_running_application_that_cannot_be_opened_is_a_failure_and_not_a_missing_application()
    {
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: new FakeProcesses(Running));
        rig.Factory.FailEverything = true;

        await rig.Engine.StartAsync();

        Assert.Equal(CaptureActivity.NoDevice, rig.Engine.Status.Activity);
        Assert.False(rig.Engine.Status.SourceAppMissing);
    }

    [Fact]
    public async Task A_process_list_that_fails_is_a_state_and_leaves_a_working_capture_alone()
    {
        var processes = new FakeProcesses(Running);
        using var rig = new AudioTestRig(micEnabled: false, sourceApp: App, processes: processes);
        await rig.Engine.StartAsync();
        var source = rig.SystemSource;

        processes.Fail = true;
        await rig.Engine.RefreshDevicesAsync();

        Assert.Same(source, rig.SystemSource);
        Assert.False(source.Disposed);
        Assert.Equal(CaptureActivity.Capturing, rig.Engine.Status.Activity);
    }

    [Fact]
    public async Task A_running_application_whose_capture_keeps_dying_is_not_reopened_in_a_tight_loop()
    {
        var factory = new TrackingFactory { OnStart = source => Task.Run(() => source.Die(new COMException("gone"))) };
        using var engine = new AudioEngine(
            new AudioEngineOptions(Seconds(2), MicEnabled: false, SourceApp: App),
            factory,
            new FakeWatcher(),
            processes: new FakeProcesses(Running));

        await engine.StartAsync();
        await Task.Delay(Seconds(2));

        factory.OnStart = null;
        Assert.InRange(factory.OpenCalls, 1, 3);
    }
}

public class RealProcessListTests
{
    [Fact]
    public void The_real_list_holds_this_process_and_its_name()
    {
        var processes = new ToolHelpProcessSnapshot().Take();

        var own = Assert.Single(processes, process => process.Id == Environment.ProcessId);
        Assert.EndsWith(".exe", own.ExeName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(own.Id, SourceAppFinder.FindCaptureTarget([own], own.ExeName));
    }
}

public class AppCaptureDeviceTests
{
    [Fact]
    [Trait("Category", "Device")]
    public async Task Listing_the_sessions_and_opening_an_application_already_playing_throws_nothing()
    {
        var apps = WasapiCaptureSourceFactory.ListApps();
        if (apps.Count == 0)
        {
            return;
        }

        var target = SourceAppFinder.FindCaptureTarget(new ToolHelpProcessSnapshot().Take(), apps[0].ExeName);
        Assert.NotNull(target);

        // Into memory only: nothing is played or written, and the microphone stays shut.
        using var source = await new WasapiCaptureSourceFactory().OpenAsync(
            new CaptureRequest(AudioFlow.System, "app:" + apps[0].ExeName, false, target, apps[0].ExeName));
        source.Start();
        await Task.Delay(200);
        source.Stop();
    }
}
