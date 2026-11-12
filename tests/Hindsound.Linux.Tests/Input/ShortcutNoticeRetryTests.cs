using Hindsound.Linux.DBus;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The notice of a desktop with no global shortcut is remembered only once it went out: one that could not be shown at a
/// launch is shown at the next, and one shown never again. Pure logic over a folder of its own: runs everywhere.
/// </summary>
public sealed class ShortcutNoticeRetryTests
{
    private static readonly BusAnswer<bool> Up = new(true, true, null);
    private static readonly BusAnswer<bool> NoServer = BusAnswer<bool>.Failed("org.freedesktop.DBus.Error.ServiceUnknown");

    private sealed class Server(params BusAnswer<bool>[] answers)
    {
        private int _asked;

        public int Asked => _asked;

        public Task<BusAnswer<bool>> Send() => Task.FromResult(answers[Math.Min(_asked++, answers.Length - 1)]);
    }

    private static void InFolder(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "hindsound-notice-retry-" + Guid.NewGuid().ToString("N"));
        try
        {
            test(Path.Combine(folder, "Hindsound", HotkeyChoice.NoticeMarkerName));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void A_notice_that_did_not_go_out_is_shown_at_the_next_launch_then_never_again()
    {
        InFolder(marker =>
        {
            var server = new Server(NoServer, Up);
            var log = new List<string>();

            Assert.False(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());
            Assert.False(File.Exists(marker));
            Assert.Equal(["hotkeys: the notice could not be shown: org.freedesktop.DBus.Error.ServiceUnknown"], log);

            Assert.True(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());
            Assert.True(File.Exists(marker));

            Assert.False(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());
            Assert.Equal(2, server.Asked);
            Assert.Single(log);
        });
    }

    [Fact]
    public void A_notice_that_went_out_at_the_first_launch_is_never_sent_again()
    {
        InFolder(marker =>
        {
            var server = new Server(Up);
            var log = new List<string>();

            Assert.True(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());
            Assert.False(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());
            Assert.False(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());

            Assert.Equal(1, server.Asked);
            Assert.True(File.Exists(marker));
            Assert.Empty(log);
        });
    }

    [Fact]
    public void Without_a_home_the_notice_goes_out_at_every_launch()
    {
        var server = new Server(Up, NoServer, Up);
        var log = new List<string>();

        Assert.True(HotkeyChoice.ShowNoticeOnceAsync(null, server.Send, log.Add).GetAwaiter().GetResult());
        Assert.False(HotkeyChoice.ShowNoticeOnceAsync(null, server.Send, log.Add).GetAwaiter().GetResult());
        Assert.True(HotkeyChoice.ShowNoticeOnceAsync(null, server.Send, log.Add).GetAwaiter().GetResult());

        Assert.Equal(3, server.Asked);
        Assert.Equal(["hotkeys: the notice could not be shown: org.freedesktop.DBus.Error.ServiceUnknown"], log);
    }

    [Fact]
    public void A_marker_that_cannot_be_written_after_the_notice_went_out_is_logged()
    {
        var file = Path.GetTempFileName();
        try
        {
            // A file where the folder should be.
            var marker = Path.Combine(file, HotkeyChoice.NoticeMarkerName);
            var server = new Server(Up);
            var log = new List<string>();

            Assert.True(HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, log.Add).GetAwaiter().GetResult());

            Assert.Equal(1, server.Asked);
            Assert.StartsWith("hotkeys: the notice marker could not be written: ", Assert.Single(log));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_notice_that_did_not_go_out_leaves_no_marker_when_its_folder_is_missing()
    {
        InFolder(marker =>
        {
            var server = new Server(NoServer);

            HotkeyChoice.ShowNoticeOnceAsync(marker, server.Send, _ => { }).GetAwaiter().GetResult();

            Assert.False(Directory.Exists(Path.GetDirectoryName(marker)));
        });
    }
}
