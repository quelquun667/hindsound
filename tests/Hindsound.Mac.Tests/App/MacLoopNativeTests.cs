using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Mac.App;
using Hindsound.Mac.Input;
using Hindsound.Mac.Tray;
using Hindsound.Unix.App;

namespace Hindsound.Mac.Tests.App;

/// <summary>
/// The loop, the icon and the shortcuts off the main thread, where the tests run: the bare run loop, and an honest refusal
/// of AppKit and Carbon. On the main thread, the mac job proves them with the app itself (<c>--prove-loop</c>).
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class MacLoopNativeTests
{
    [Fact]
    public void Off_the_main_thread_the_pump_waits_on_the_run_loop_and_refuses_the_events_of_NSApplication()
    {
        OnOwnThread(() =>
        {
            using var pump = new MacRunLoopPump();
            Assert.False(pump.AppEvents);
            Assert.Throws<InvalidOperationException>(() => new MacRunLoopPump(appEvents: true));
        });
    }

    [Fact]
    public void Wakes_from_many_threads_while_the_pump_is_disposed_never_touch_a_freed_source()
    {
        for (var round = 0; round < 50; round++)
        {
            OnOwnThread(() =>
            {
                var pump = new MacRunLoopPump();
                using var start = new ManualResetEventSlim();
                var wakers = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
                {
                    start.Wait();
                    for (var i = 0; i < 2000; i++)
                    {
                        pump.Wake();
                    }
                })).ToArray();
                foreach (var waker in wakers)
                {
                    waker.Start();
                }

                start.Set();
                Thread.Sleep(1);
                pump.Dispose();
                foreach (var waker in wakers)
                {
                    waker.Join();
                }

                Assert.True(pump.Disposed);
                pump.Wake();
                pump.Dispose();
            });
        }
    }

    [Fact]
    public void Disposing_the_loop_of_the_shell_frees_its_pump_after_it()
    {
        OnOwnThread(() =>
        {
            var loop = (MacAppLoop)new MacShell(_ => { }).NewLoop();
            var ran = false;
            loop.Post(() => ran = true);
            loop.Post(loop.Quit);
            loop.Run();
            Assert.True(ran);
            Assert.False(loop.Pump.Disposed);

            loop.Dispose();
            Assert.True(loop.Pump.Disposed);
        });
    }

    [Fact]
    public void Off_the_main_thread_there_is_no_icon_and_no_shortcut_and_the_page_asks_for_a_command()
    {
        OnOwnThread(() =>
        {
            var log = new List<string>();
            using var loop = new UnixLoop();
            Assert.Null(MacStatusItem.TryCreate(loop, log.Add));
            Assert.Null(CarbonHotkeys.TryOpen(log.Add));
            Assert.False(CarbonHotkeys.Ready);
            Assert.Equal(ShortcutMode.Command, MacPlatform.ShortcutsNow());
            Assert.Equal(["menu bar: no icon off the main thread", "hotkeys: Carbon runs on the main thread only"], log);
        });
    }

    private static void OnOwnThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the test thread did not end");
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
