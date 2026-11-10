using System.IO;
using System.Runtime.InteropServices;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Ui;

namespace Hindsound.Tests.Library;

/// <summary>
/// The real calls, as far as they go without a window or a sound: the shell parses but is never asked to open, and MCI opens,
/// answers and closes a silent file but is never asked to play.
/// </summary>
[Trait("Category", "Device")]
public sealed class ShellAndMciDeviceTests : IDisposable
{
    private const string Canary = "Collection-Canary-7F3";

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private static void OnStaThread(Action work)
    {
        Exception? failure = null;
        var thread = OffLoop.Start(work, ex => failure = ex);
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        Assert.Null(failure);
    }

    private string SilentWav(string name)
    {
        const int SampleRate = 8000;
        const int DataBytes = SampleRate;
        var path = _root.Combine(name);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + DataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write("data"u8);
        writer.Write(DataBytes);
        for (var i = 0; i < DataBytes; i++)
        {
            writer.Write((byte)128);
        }

        return path;
    }

    [Fact]
    public void The_shell_call_that_opens_is_there_without_being_called()
    {
        var library = NativeLibrary.Load("shell32.dll");
        try
        {
            Assert.NotEqual(0, NativeLibrary.GetExport(library, "SHOpenFolderAndSelectItems"));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void Two_real_files_turn_into_items_of_one_folder()
    {
        var folder = _root.Combine("clips");
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "a_system.mp3");
        var second = Path.Combine(folder, "a_mic.mp3");
        File.WriteAllBytes(first, new byte[10]);
        File.WriteAllBytes(second, new byte[10]);

        OnStaThread(() =>
        {
            var used = false;
            var result = ExplorerFolderShell.WithItems(folder, [first, second], (folderItem, children) =>
            {
                used = true;
                Assert.NotEqual(0, folderItem);
                Assert.Equal(2, children.Length);
                Assert.All(children, child => Assert.NotEqual(0, child));
                Assert.NotEqual(children[0], children[1]);
                return 0;
            });

            Assert.Equal(0, result);
            Assert.True(used);
        });
    }

    [Fact]
    public void A_folder_that_is_not_there_is_refused_before_anything_can_open()
    {
        OnStaThread(() =>
        {
            var missing = _root.Combine(Canary);
            var result = ExplorerFolderShell.WithItems(missing, [Path.Combine(missing, Canary + ".mp3")], (_, _) => throw new InvalidOperationException("reached the open"));

            Assert.True(result < 0);
        });
    }

    [Fact]
    public void A_file_that_is_not_there_is_refused_too_and_only_a_code_goes_to_the_log()
    {
        OnStaThread(() =>
        {
            var journal = new List<string>();
            var missing = _root.Combine(Canary, Canary + ".mp3");

            var outcome = ShellFiles.ShowInFolder(
                new ClipGroup([new ClipFile(missing, 10, DateTime.UtcNow)]),
                new OnlyParsingShell(),
                _ => true,
                journal.Add);

            Assert.Equal(ShellOutcome.Failed, outcome);
            Assert.Contains("0x", Assert.Single(journal));
            Assert.DoesNotContain("Canary", journal[0]);
        });
    }

    [Fact]
    public void MCI_opens_a_silent_file_answers_its_mode_as_a_number_and_closes_it()
    {
        var path = SilentWav("a silent clip.wav");
        OnStaThread(() =>
        {
            var player = new MciPreviewPlayer();
            const string Alias = "hindsound_preview_device_test";

            Assert.Equal(0, player.Send($"open \"{path}\" type mpegvideo alias {Alias}"));
            try
            {
                Assert.Equal(0, player.ModeOf(Alias, out var mode));
                Assert.Equal(MciPreviewPlayer.ModeStopped, mode);
                Assert.Equal(0, player.StateOf(Alias, out var state));
                Assert.Equal(PreviewTrackState.Stopped, state);
            }
            finally
            {
                Assert.Equal(0, player.Send($"close {Alias}"));
            }

            Assert.NotEqual(0, player.ModeOf(Alias, out _));
        });
    }

    [Fact]
    public void MCI_refuses_a_file_that_is_not_there_without_an_exception()
    {
        var missing = _root.Combine(Canary, Canary + ".mp3");
        OnStaThread(() =>
        {
            Assert.NotEqual(0, new MciPreviewPlayer().Send($"open \"{missing}\" type mpegvideo alias hindsound_preview_device_missing"));
        });
    }

    [Fact]
    public void The_wait_ends_at_its_delay_or_at_once_when_woken_and_answers_a_posted_message()
    {
        OnStaThread(() =>
        {
            var player = new MciPreviewPlayer();
            using var wake = new AutoResetEvent(false);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            player.Wait(wake, 50);
            Assert.True(watch.ElapsedMilliseconds >= 40, $"took {watch.ElapsedMilliseconds} ms");

            wake.Set();
            watch.Restart();
            player.Wait(wake, 30_000);
            Assert.True(watch.ElapsedMilliseconds < 15_000, $"took {watch.ElapsedMilliseconds} ms");

            // WM_NULL, to this thread: the wait returns for it, and takes it off the queue.
            Assert.NotEqual(0, PostThreadMessageW(GetCurrentThreadId(), 0, 0, 0));
            watch.Restart();
            player.Wait(wake, 30_000);
            Assert.True(watch.ElapsedMilliseconds < 15_000, $"took {watch.ElapsedMilliseconds} ms");
            Assert.Equal(0u, GetQueueStatus(QS_POSTMESSAGE) >> 16);
        });
    }

    private sealed class OnlyParsingShell : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) =>
            ExplorerFolderShell.WithItems(folder, files, (_, _) => throw new InvalidOperationException("reached the open"));
    }

    private const uint QS_POSTMESSAGE = 0x0008;

    [DllImport("user32.dll")]
    private static extern int PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetQueueStatus(uint flags);
}
