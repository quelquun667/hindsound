using System.Collections.Concurrent;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The real panel on the real screen, real files in a made-up folder, and a loop the test turns by hand.</summary>
public sealed class WindowLibraryTests : IDisposable
{
    private const string Canary = "Collection-Canary-7F3";
    private static readonly DateTime LongAgo = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly FakeShell _shell = new();
    private readonly List<IReadOnlyList<string>> _copied = [];
    private readonly WindowLibrary _panel;
    private Action<Func<bool>, Action<bool>> _clipboard = (attempt, done) => done(attempt());
    private Func<ClipScanRequest>? _foldersInstead;
    private int _turned;

    public WindowLibraryTests()
    {
        Directory.CreateDirectory(Clips);
        Folders = new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") };
        _panel = new WindowLibrary(
            _rig.Screen,
            new LibraryStore(SettingsPath, _journal.Enqueue),
            () => _foldersInstead is { } instead ? instead() : Folders,
            _loop.Enqueue,
            _shell,
            File.Exists,
            paths =>
            {
                _copied.Add(paths);
                return true;
            },
            (attempt, done) => _clipboard(attempt, done),
            _journal.Enqueue);
        Screen.SetLibrary(_panel);
    }

    public void Dispose()
    {
        _panel.Close();
        _rig.Dispose();
        _temp.Dispose();
    }

    private sealed class FakeShell : IFolderShell
    {
        public readonly ConcurrentQueue<IReadOnlyList<string>> Shown = new();
        public int Thread;

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Thread = Environment.CurrentManagedThreadId;
            Shown.Enqueue(files);
            return 0;
        }
    }

    private string SettingsPath => _temp.Combine("settings.json");

    private string Clips => _temp.Combine("clips");

    private ClipScanRequest Folders { get; set; }

    private SettingsScreen Screen => _rig.Screen;

    private string Clip(string name, DateTime? written = null, string? folder = null)
    {
        var path = Path.Combine(folder ?? Clips, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, written ?? LongAgo);
        return path;
    }

    private void Turn(Func<bool> until)
    {
        var end = Environment.TickCount64 + 20_000;
        while (!until())
        {
            if (_loop.TryDequeue(out var work))
            {
                work();
                _turned++;
            }
            else if (Environment.TickCount64 > end)
            {
                Assert.Fail("the loop never got there");
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void Settle() => Turn(() => !_panel.IsWorking && _loop.IsEmpty);

    private void OpenPage()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.ShowSection(Section.Library);
        Settle();
    }

    private IReadOnlyList<string> Listed => (_panel.Lines?.Lines ?? []).Select(line => Path.GetFileName(line.Main.Path)).ToList();

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    private bool Enabled(string id) => ((Button)Screen.WidgetById(id)!).Enabled;

    private void Choose(string name)
    {
        Screen.SelectClip(Path.Combine(Clips, name));
        Assert.True(Enabled(SettingsScreen.ClipCopyId));
    }

    private string ChosenName()
    {
        // The list draws each name too: the foot is the line after the caption of the chosen clip.
        var drawn = Painted();
        return string.Join("|", drawn.Skip(drawn.LastIndexOf(SettingsScreen.SelectedClipCaption)));
    }

    // ---------------------------------------------------------------- when the disk is read

    [Fact]
    public void Nothing_is_read_until_the_page_is_on_screen()
    {
        Clip("a.mp3");
        var asked = 0;
        _foldersInstead = () =>
        {
            asked++;
            return Folders;
        };
        Screen.ShowSection(Section.Now);

        _panel.LookIfShown();
        Screen.ReadLibraryAgain();

        Assert.Equal(0, asked);
        Assert.False(_panel.IsWorking);
        Assert.Null(_panel.Lines);
    }

    [Fact]
    public void Opening_the_page_lists_the_clips_newest_first_and_writes_no_index()
    {
        Clip("old.mp3", LongAgo);
        Clip("new.mp3", LongAgo.AddDays(1));

        OpenPage();

        Assert.Equal(["new.mp3", "old.mp3"], Listed);
        Assert.Contains("new", Painted());
        Assert.False(File.Exists(_temp.Combine("library.json")));
        Assert.Equal(2, Directory.GetFiles(Clips).Length);
    }

    [Fact]
    public void A_clip_saved_with_the_page_open_shows_up_and_moves_neither_the_choice_nor_the_focus()
    {
        Clip("chosen.mp3", LongAgo);
        Clip("other.mp3", LongAgo.AddDays(-1));
        OpenPage();
        Choose("chosen.mp3");
        _rig.FocusOn(SettingsScreen.ClipListId);
        var requests = _rig.Host.FocusRequests;

        Clip("saved.mp3", LongAgo.AddDays(2));
        _panel.LookIfShown();
        Settle();

        Assert.Equal(["saved.mp3", "chosen.mp3", "other.mp3"], Listed);
        Assert.Contains("chosen.mp3", ChosenName(), StringComparison.Ordinal);
        Assert.Equal(SettingsScreen.ClipListId, Screen.FocusId);
        Assert.Equal(requests, _rig.Host.FocusRequests);
    }

    [Fact]
    public void A_file_written_since_the_press_stays_out_until_the_press_has_ended()
    {
        Clip("whole.mp3", LongAgo);
        OpenPage();
        var written = DateTime.UtcNow;
        Clip("half.mp3", written);

        Folders = Folders with { ClipStartedUtc = written };
        _panel.LookIfShown();
        Settle();
        Assert.Equal(["whole.mp3"], Listed);

        Folders = Folders with { ClipStartedUtc = null };
        _panel.LookIfShown();
        Settle();
        Assert.Equal(["half.mp3", "whole.mp3"], Listed);
    }

    [Fact]
    public void Coming_back_after_a_rename_in_the_folder_shows_the_new_name()
    {
        var path = Clip("before.mp3");
        OpenPage();

        File.Move(path, Path.Combine(Clips, "after.mp3"));
        _panel.LookIfShown();
        Settle();

        Assert.Equal(["after.mp3"], Listed);
    }

    // ---------------------------------------------------------------- a click on the notification

    private WindowGoing Going() => new(() => true, () => Screen, () => { }, _ => { }, _ => { });

    private static NotificationContent Saved(string path) =>
        NotificationStates.ForClip(new ClipResult([path], TimeSpan.FromSeconds(30), 1000, false, [0.5f], false, false), TimeSpan.FromSeconds(30), false, false, 0xFFFFFF);

    [Fact]
    public void A_click_on_clip_saved_lands_on_the_page_with_the_clip_chosen_once_the_reading_is_back()
    {
        var path = Clip("saved.mp3", LongAgo);
        Clip("other.mp3", LongAgo.AddDays(1));
        Screen.UpdateStatus(ScreenRig.Capturing());

        Going().Click(Saved(path));
        Settle();

        Assert.Equal(Section.Library, Screen.Section);
        Assert.Contains("saved.mp3", ChosenName(), StringComparison.Ordinal);
        Assert.True(Enabled(SettingsScreen.ClipCopyId));
    }

    [Fact]
    public void A_clip_not_listed_yet_is_chosen_by_the_reading_that_brings_it()
    {
        Clip("other.mp3", LongAgo);
        OpenPage();
        var path = Path.Combine(Clips, "late.mp3");

        Going().Click(Saved(path));
        Settle();
        Assert.False(Enabled(SettingsScreen.ClipCopyId));

        Clip("late.mp3", LongAgo.AddDays(1));
        _panel.LookIfShown();
        Settle();

        Assert.True(Enabled(SettingsScreen.ClipCopyId));
        Assert.Contains("late.mp3", ChosenName(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the output folder changes

    [Fact]
    public void A_folder_left_stays_listed()
    {
        var settings = _rig.Settings;
        settings.Export.OutputFolder = Clips;
        var next = _temp.Combine("next");
        Clip("left-behind.mp3", LongAgo);
        Clip("new.mp3", LongAgo.AddDays(1), next);
        _foldersInstead = () => new ClipScanRequest { OutputFolder = settings.Export.OutputFolder, KnownFolders = settings.Export.KnownClipFolders ?? [], RescueFolder = Folders.RescueFolder };

        Assert.True(Screen.Editor.SetFolder(next).Ok);
        OpenPage();

        Assert.Equal(["new.mp3", "left-behind.mp3"], Listed);
    }

    // ---------------------------------------------------------------- folders that can't be read

    [Fact]
    public void An_absent_folder_gives_an_empty_page_and_no_exception()
    {
        Folders = Folders with { OutputFolder = _temp.Combine("nowhere") };

        OpenPage();

        Assert.NotNull(_panel.Lines);
        Assert.Empty(_panel.Lines!.Lines);
        Assert.Contains(SettingsScreen.NoClipsTitle, Painted());
    }

    [Fact]
    public void A_refused_or_absent_folder_or_a_failed_look_never_tells_its_name_to_the_journal()
    {
        var refused = _temp.Combine(Canary);
        Clip(Canary + ".mp3", folder: refused);
        var kept = Clip("kept.mp3");
        var directory = new DirectoryInfo(refused);
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var rules = directory.GetAccessControl();
        rules.AddAccessRule(denied);
        directory.SetAccessControl(rules);
        try
        {
            Folders = Folders with { KnownFolders = [refused, _temp.Combine(Canary + "-absent")] };
            OpenPage();
            Assert.Equal(["kept.mp3"], Listed);

            // A gesture on a file gone, then a look that fails as a whole with the name in its message.
            File.Delete(kept);
            _panel.ShowInFolder(new ClipGroup([new ClipFile(Path.Combine(refused, Canary + ".mp3"), 3, LongAgo)]));
            var before = _turned;
            Turn(() => _turned > before);
            _foldersInstead = () => throw new IOException($@"C:\{Canary}\x.mp3");
            _panel.ReadAgain();
            Settle();

            Assert.Contains(_journal, text => text.StartsWith("library read: not asked (IOException", StringComparison.Ordinal));
            Assert.DoesNotContain(_journal, text => text.Contains(Canary, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            rules.RemoveAccessRule(denied);
            directory.SetAccessControl(rules);
        }
    }

    // ---------------------------------------------------------------- after the window has gone

    [Fact]
    public void A_look_that_ends_after_the_window_has_gone_is_dropped_and_says_when_it_is_over()
    {
        Clip("a.mp3");
        Screen.ShowSection(Section.Library);
        var ended = 0;

        _panel.Close(() => ended++);
        Turn(() => !_panel.IsWorking);
        Settle();

        Assert.Null(_panel.Lines);
        Assert.Equal(1, ended);

        _panel.ReadAgain();
        Assert.False(_panel.IsWorking);
    }

    [Fact]
    public void The_answer_of_a_gesture_after_the_window_has_gone_is_dropped()
    {
        var path = Clip("a.mp3");
        OpenPage();
        Choose("a.mp3");
        File.Delete(path);
        var line = _panel.Lines!.Lines[0];

        _panel.ShowInFolder(line);
        _panel.Close();
        var before = _turned;
        Turn(() => _turned > before);

        Assert.True(Enabled(SettingsScreen.ClipCopyId));
        Assert.DoesNotContain(SettingsScreen.ClipGoneMessage, Painted());
    }

    // ---------------------------------------------------------------- the gestures

    private void Answered(Action gesture)
    {
        var before = _turned;
        gesture();
        Turn(() => _turned > before);
    }

    [Fact]
    public void A_file_found_gone_says_so_on_the_screen()
    {
        var path = Clip("a.mp3");
        OpenPage();
        Choose("a.mp3");
        File.Delete(path);

        Answered(() => _panel.ShowInFolder(_panel.Lines!.Lines[0]));

        Assert.False(Enabled(SettingsScreen.ClipCopyId));
        Assert.Contains(Painted(), text => text.Contains(SettingsScreen.ClipGoneMessage, StringComparison.Ordinal));
        Assert.Empty(_shell.Shown);
    }

    [Fact]
    public void Copy_and_show_of_a_line_of_two_tracks_act_on_both()
    {
        var system = Clip("clip_system.mp3");
        var mic = Clip("clip_mic.mp3");
        OpenPage();
        var line = Assert.Single(_panel.Lines!.Lines);
        Assert.Equal(system, line.Main.Path);

        Answered(() => _panel.Copy(line));
        Answered(() => _panel.ShowInFolder(line));

        Assert.Equal([system, mic], Assert.Single(_copied));
        Assert.Equal([system, mic], Assert.Single(_shell.Shown));
        Assert.NotEqual(Environment.CurrentManagedThreadId, _shell.Thread);
    }

    [Fact]
    public void With_no_clipboard_the_copy_is_never_tried_and_the_screen_says_it_did_not_work()
    {
        var options = AppOptions.Parse(["--no-clipboard"]);
        _clipboard = (attempt, done) => SettingsMapping.CopyToClipboard(options, attempt, (_, _) => { }, done);
        Clip("a.mp3");
        OpenPage();
        Choose("a.mp3");

        Answered(() => _panel.Copy(_panel.Lines!.Lines[0]));

        Assert.Empty(_copied);
        Assert.Contains(Painted(), text => text.Contains(SettingsScreen.ClipFailedMessage, StringComparison.Ordinal));
    }
}
