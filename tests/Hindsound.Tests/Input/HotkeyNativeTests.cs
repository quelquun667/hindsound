using Hindsound.Input;

namespace Hindsound.Tests.Input;

public class HotkeyNativeTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    /// <summary>Keeps the hotkey thread busy until the gate opens, so later requests wait in the queue.</summary>
    private static Task Block(Win32HotkeyNative native, ManualResetEventSlim gate, ManualResetEventSlim started)
    {
        var task = Task.Run(() => native.Invoke(() => { started.Set(); gate.Wait(); return true; }, undo: null, abandonable: false));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        return task;
    }

    [Fact]
    public async Task A_request_abandoned_before_it_starts_never_runs()
    {
        using var native = new Win32HotkeyNative(Short);
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var blocker = Block(native, gate, started);
        var ran = false;

        var outcome = native.Invoke(() => { ran = true; return true; }, undo: null);
        gate.Set();
        await blocker;
        native.Invoke(() => true, undo: null); // a request behind it: the thread has drained the queue

        Assert.Equal(HotkeyRegistration.TimedOut, outcome);
        Assert.False(ran);
    }

    private const int MaxTries = 20;

    // The caller gives up after Short. On a busy machine the hotkey thread can come to the request even later: the request is
    // then abandoned before it starts, the case of the test above, and never runs. Only that setup is tried again; the
    // checks run once, on the first try whose request started.
    [Fact]
    public async Task A_request_abandoned_while_it_runs_is_undone_when_it_ends()
    {
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            using var native = new Win32HotkeyNative(Short);
            using var gate = new ManualResetEventSlim();
            using var started = new ManualResetEventSlim();
            using var undone = new ManualResetEventSlim();
            var caller = Task.Run(() => native.Invoke(() => { started.Set(); gate.Wait(); return true; }, undo: undone.Set));
            var outcome = await caller;
            if (!started.Wait(TimeSpan.FromSeconds(1)))
            {
                gate.Set();
                continue;
            }

            Assert.Equal(HotkeyRegistration.TimedOut, outcome);
            Assert.False(undone.IsSet); // still running
            gate.Set();

            Assert.True(undone.Wait(TimeSpan.FromSeconds(5)));
            return;
        }

        Assert.Fail($"The hotkey thread never started a request before its caller gave up, in {MaxTries} tries.");
    }

    [Fact]
    public void A_request_that_answers_in_time_is_not_undone()
    {
        using var native = new Win32HotkeyNative(TimeSpan.FromSeconds(5));
        var undone = false;

        var outcome = native.Invoke(() => true, undo: () => undone = true);

        Assert.Equal(HotkeyRegistration.Registered, outcome);
        Assert.False(undone);
    }

    // Real RegisterHotKey with a combination nobody uses, released at the end. Excluded from the normal run.
    [Fact]
    [Trait("Category", "Device")]
    public async Task Real_a_register_that_times_out_leaves_no_ghost_and_the_next_apply_works()
    {
        var settings = new Hindsound.Settings.HotkeySettings { SaveDefault = "Ctrl+Alt+Shift+F23" };
        using var native = new Win32HotkeyNative(Short);
        using var manager = new HotkeyManager(native);
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var blocker = Block(native, gate, started);

        var during = manager.Apply(settings)[(int)HotkeyAction.SaveDefault];
        gate.Set();
        await blocker;
        var after = manager.Apply(settings)[(int)HotkeyAction.SaveDefault];

        Assert.Equal(HotkeyStatus.Unavailable, during.Status);
        Assert.Equal(HotkeyStatus.Registered, after.Status);
    }
}
