using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// What the Windows adapters hand from the window of the loop to the app: the messages of the icon, Explorer starting
/// again, the end of the session and a second launch. Real messages to a hidden window of this test process, as in
/// UiLoopTests: nothing shows and nothing takes the focus.
/// </summary>
[Collection("UiLoop")]
public class WindowsShellForwardsTests
{
    private const nuint CloseApp = 1;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    // A window belongs to the thread that made it, and a loop that never ends must not hold the test.
    private static bool LoopEnds(Action<UiLoop> before)
    {
        using var ended = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var loop = new UiLoop();
            before(loop);
            loop.After(300, loop.Quit);
            loop.Run();
            ended.Set();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return ended.Wait(Bound);
    }

    [Fact]
    public void The_messages_of_the_icon_reach_it_with_their_code_and_their_wParam()
    {
        var heard = new List<(uint Code, nuint WParam)>();

        var ended = LoopEnds(loop =>
        {
            var tray = (TrayIcon)new WindowsShell().NewTray(loop);
            tray.Message = (code, wParam) => heard.Add((code, wParam));
            Native.PostMessageW(loop.Hwnd, UiLoop.TrayCallbackMessage, 0x00050007, 0x7B);
        });

        Assert.True(ended);
        Assert.Equal([(0x7Bu, (nuint)0x00050007)], heard);
    }

    [Fact]
    public void Explorer_starting_again_reaches_the_icon_as_lost()
    {
        var lost = 0;

        var ended = LoopEnds(loop =>
        {
            var tray = (TrayIcon)new WindowsShell().NewTray(loop);
            tray.Lost = () => lost++;
            Native.PostMessageW(loop.Hwnd, Native.RegisterWindowMessageW("TaskbarCreated"), 0, 0);
        });

        Assert.True(ended);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void The_questions_of_the_session_reach_the_app_on_the_thread_of_the_loop()
    {
        var asked = 0;
        var kept = 0;
        var ending = 0;
        var threads = new List<int>();
        var loopThread = 0;

        var ended = LoopEnds(loop =>
        {
            loopThread = Environment.CurrentManagedThreadId;
            var session = new WindowsShell().SessionEnd(loop);
            session.Asked = () => { asked++; threads.Add(Environment.CurrentManagedThreadId); };
            session.Kept = () => { kept++; threads.Add(Environment.CurrentManagedThreadId); };
            session.Ending = () => ending++;
            Native.PostMessageW(loop.Hwnd, Native.WM_QUERYENDSESSION, 0, (nint)CloseApp);
            Native.PostMessageW(loop.Hwnd, Native.WM_ENDSESSION, 0, (nint)CloseApp);
        });

        Assert.True(ended);
        Assert.Equal(1, asked);
        Assert.Equal(1, kept);
        Assert.Equal(0, ending);
        Assert.All(threads, thread => Assert.Equal(loopThread, thread));
    }

    [Fact]
    public void The_end_of_the_session_reaches_the_app_and_ends_the_loop()
    {
        var ending = 0;

        var ended = LoopEnds(loop =>
        {
            new WindowsShell().SessionEnd(loop).Ending = () => ending++;
            Native.PostMessageW(loop.Hwnd, Native.WM_ENDSESSION, 1, (nint)CloseApp);
        });

        Assert.True(ended);
        Assert.Equal(1, ending);
    }

    [Fact]
    public void A_reason_to_wait_is_given_to_Windows_and_taken_back()
    {
        bool blocked = false, unblocked = false;

        var ended = LoopEnds(loop =>
        {
            var session = new WindowsShell().SessionEnd(loop);
            blocked = session.Block("Writing a clip");
            unblocked = session.Unblock();
        });

        Assert.True(ended);
        Assert.True(blocked, "Windows refused the reason given on the window of the loop");
        Assert.True(unblocked);
    }

    [Fact]
    public void The_window_of_the_loop_is_not_in_front_while_hidden()
    {
        var inFront = true;

        var ended = LoopEnds(loop => inFront = loop.InFront());

        Assert.True(ended);
        Assert.False(inFront);
    }

    [Fact]
    public void The_message_of_a_second_launch_reaches_the_app_through_the_channel()
    {
        var heard = new List<InstanceMessage>();
        using var mutex = new Mutex(true, @"Local\Hindsound.SingleInstance.Test.Forwards." + Guid.NewGuid().ToString("N"), out var first);
        var channel = new WindowsInstanceChannel(mutex, first);

        var ended = LoopEnds(loop =>
        {
            channel.Listen(loop, heard.Add);
            Native.PostMessageW(loop.Hwnd, UiLoop.ShowMessage, 0, 0);
        });

        Assert.True(first);
        Assert.True(ended);
        Assert.Equal([InstanceMessage.Show], heard);
    }
}
