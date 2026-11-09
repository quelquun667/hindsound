using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>What the app gives the Library page, with the app built and never run, and its folders replaced by made-up ones.</summary>
public sealed class AppLibraryWiringTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly List<Section> _opened = [];
    private int _foldersAsked;

    public void Dispose()
    {
        _rig.Dispose();
        _temp.Dispose();
    }

    private string Clips => _temp.Combine("clips");

    private HindsoundApp Built(params string[] more)
    {
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen", .. more]);
        Directory.CreateDirectory(Clips);
        return new HindsoundApp(options, new Journal(_temp.Combine("settings.log")))
        {
            OpenInstead = _opened.Add,
            PostInstead = _loop.Enqueue,
            LibraryFoldersInstead = () =>
            {
                _foldersAsked++;
                return new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") };
            },
        };
    }

    private void Clip(string name)
    {
        Directory.CreateDirectory(Clips);
        File.WriteAllBytes(Path.Combine(Clips, name), [1, 2, 3]);
    }

    private void Turn(Func<bool> until)
    {
        var end = Environment.TickCount64 + 20_000;
        while (!until())
        {
            if (_loop.TryDequeue(out var work))
            {
                work();
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

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    // A row draws its name and its extension apart.
    private bool Shows(string text) => Painted().Contains(text);

    private void OpenPage(string waitedFor)
    {
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);
        Turn(() => Shows(waitedFor));
    }

    [Fact]
    public void A_new_screen_gets_the_library_and_reads_nothing_before_the_page_is_shown()
    {
        Clip("a.mp3");
        var app = Built();

        app.WireScreen(_rig.Screen);

        Assert.Equal(Section.Now, _rig.Screen.Section);
        Assert.Equal(0, _foldersAsked);

        OpenPage("a");
        Assert.True(_foldersAsked > 0);
    }

    [Fact]
    public void A_clip_ended_or_the_window_back_looks_again_only_while_the_page_is_shown()
    {
        var app = Built();
        app.WireScreen(_rig.Screen);

        app.LookAtTheClipsIfShown();
        Assert.Equal(0, _foldersAsked);

        Clip("a.mp3");
        OpenPage("a");

        Clip("b.mp3");
        app.LookAtTheClipsIfShown();
        Turn(() => Shows("b"));
    }

    [Fact]
    public void Once_the_window_has_gone_nothing_is_read_and_a_reading_on_its_way_is_dropped()
    {
        Clip("a.mp3");
        var app = Built();
        app.WireScreen(_rig.Screen);
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);

        app.LibraryWindowClosed();
        var asked = _foldersAsked;
        app.LookAtTheClipsIfShown();

        var end = Environment.TickCount64 + 500;
        Turn(() => Environment.TickCount64 > end);
        Assert.Equal(asked, _foldersAsked);
        Assert.False(Shows("a"));
    }

    [Fact]
    public void A_second_window_does_not_answer_to_the_first_one()
    {
        var app = Built();
        app.WireScreen(_rig.Screen);
        using var second = new ScreenRig();

        app.WireScreen(second.Screen);
        Clip("a.mp3");
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);
        second.Screen.UpdateStatus(ScreenRig.Capturing());
        second.Screen.ShowSection(Section.Library);
        Turn(() =>
        {
            second.Canvas.Drawn.Clear();
            second.Screen.Paint(second.Canvas);
            return second.Canvas.Drawn.Contains("a");
        });

        Assert.False(Shows("a"));
    }

    [Fact]
    public void Library_in_the_menu_of_the_icon_opens_the_page()
    {
        var app = Built();

        app.OnTrayMessage(Native.WM_CONTEXTMENU, (nuint)TrayCommand.Library);

        Assert.Equal([Section.Library], _opened);
    }

    [Fact]
    public void A_click_on_a_clip_opens_the_page()
    {
        var app = Built();
        var clip = new ClipResult([Path.Combine(Clips, "a.mp3")], TimeSpan.FromSeconds(30), 1000, false, [0.5f], false, false);

        app.NotificationClicked(NotificationStates.ForClip(clip, TimeSpan.FromSeconds(30), false, false, 0xFFFFFF));
        app.NotificationClicked(NotificationStates.LastClipGone());

        Assert.Equal([Section.Library, Section.Library], _opened);
    }

    [Fact]
    public void With_no_clipboard_the_gesture_reaches_nothing_and_the_screen_is_told()
    {
        Clip("a.mp3");
        var app = Built("--no-clipboard");
        app.WireScreen(_rig.Screen);
        OpenPage("a");
        _rig.Screen.SelectClip(Path.Combine(Clips, "a.mp3"));
        _rig.FocusOn(SettingsScreen.ClipCopyId);

        _rig.Press(Vk.Enter);

        Turn(() => Shows(SettingsScreen.ClipFailedMessage));
    }

    [Fact]
    public void A_clip_gone_with_its_folder_is_a_failure_and_not_a_clip_moved_or_deleted()
    {
        Clip("a.mp3");
        var app = Built();
        app.WireScreen(_rig.Screen);
        OpenPage("a");
        _rig.Screen.SelectClip(Path.Combine(Clips, "a.mp3"));
        _rig.FocusOn(SettingsScreen.ClipShowId);
        Directory.Delete(Clips, true);

        _rig.Press(Vk.Enter);

        Turn(() => Shows(SettingsScreen.ClipFailedMessage));
        Assert.False(Shows(SettingsScreen.ClipGoneMessage));
    }

    [Fact]
    public void Off_screen_show_in_folder_opens_nothing_and_the_log_has_no_path()
    {
        Clip("Collection-Canary-7F3.mp3");
        var app = Built();
        app.WireScreen(_rig.Screen);
        OpenPage("Collection-Canary-7F3");
        _rig.Screen.SelectClip(Path.Combine(Clips, "Collection-Canary-7F3.mp3"));
        _rig.FocusOn(SettingsScreen.ClipShowId);

        _rig.Press(Vk.Enter);

        var log = _temp.Combine("settings.log");
        Turn(() => File.Exists(log) && File.ReadAllText(log).Contains("would be selected now", StringComparison.Ordinal));
        Assert.DoesNotContain("Collection-Canary-7F3", File.ReadAllText(log), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- where it looks

    [Fact]
    public void The_folders_come_from_the_settings_and_the_clips_being_saved()
    {
        var app = Built();
        app.LibraryFoldersInstead = null;
        app.SettingsNow.Export.OutputFolder = @"X:\Made up";
        app.SettingsNow.Export.KnownClipFolders = [@"X:\Made up before"];

        var folders = app.LibraryFolders();

        Assert.Equal(@"X:\Made up", folders.OutputFolder);
        Assert.Equal([@"X:\Made up before"], folders.KnownFolders);
        Assert.Null(folders.ClipStartedUtc);

        using var held = new ManualResetEventSlim();
        var finished = new ManualResetEventSlim();
        var pipeline = new ClipPipeline(new HeldBackend(held), work => work(), (_, _) => { }, _ => { }) { Finished = _ => finished.Set() };
        app.UsePipeline(pipeline);

        pipeline.Press(new ClipRequest(TimeSpan.FromSeconds(1), new ExportOptions(), null, false));
        Assert.NotNull(app.LibraryFolders().ClipStartedUtc);

        held.Set();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(20)));
        Assert.Null(app.LibraryFolders().ClipStartedUtc);
    }

    [Fact]
    public void A_clip_pressed_after_the_request_is_held_back_from_the_look_that_starts_later()
    {
        Clip("old.mp3");
        File.SetLastWriteTimeUtc(Path.Combine(Clips, "old.mp3"), DateTime.UtcNow.AddHours(-1));
        var app = Built();
        app.WireScreen(_rig.Screen);
        using var held = new ManualResetEventSlim();
        var pipeline = new ClipPipeline(new HeldBackend(held), work => work(), (_, _) => { }, _ => { });
        app.UsePipeline(pipeline);

        // The request the page makes says nothing of a press: the press is only heard of when the look starts.
        pipeline.Press(new ClipRequest(TimeSpan.FromSeconds(1), new ExportOptions(), null, false));
        Clip("new.mp3");
        OpenPage("old");

        Assert.False(Shows("new"));
        held.Set();
    }

    private sealed class HeldBackend(ManualResetEventSlim held) : IClipBackend
    {
        public AudioSnapshot Snapshot(TimeSpan duration)
        {
            held.Wait(TimeSpan.FromSeconds(20));
            return new AudioSnapshot([], []);
        }

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName) => throw new InvalidOperationException();
    }
}
