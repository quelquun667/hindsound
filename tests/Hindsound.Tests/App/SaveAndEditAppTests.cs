using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>"Save and edit" in the app: what opens after the clip is written, and what never gets replaced to open it.</summary>
public sealed class SaveAndEditAppTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly SaveAndEditShell _shell;

    public SaveAndEditAppTests() => _shell = new SaveAndEditShell(_rig.Screen);

    public void Dispose()
    {
        _rig.Dispose();
        _temp.Dispose();
    }

    private Exception? Failure { get; set; }

    private string Log => _temp.Combine("settings.log");

    private string Clips => _temp.Combine("clips");

    private string[] JournalLines() => File.Exists(Log) ? [.. Journal.ReadShared(Log)] : [];

    private HindsoundApp Built(bool hasWindow = true)
    {
        _shell.HasWindowNow = hasWindow;
        Directory.CreateDirectory(Clips);
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        return new HindsoundApp(options, new Journal(Log), shell: _shell)
        {
            PostInstead = _loop.Enqueue,
            LoadChoicesInstead = () => { },
            AfterInstead = (_, _) => { },
            ShowOutcomeInstead = _ => { },
            LibraryFoldersInstead = () => new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") },
        };
    }

    private ClipResult WriteClip(string name)
    {
        var path = Path.Combine(Clips, name);
        File.WriteAllBytes(path, new byte[2000]);
        return new ClipResult([path], TimeSpan.FromSeconds(30), 2000, false, [0.5f], false);
    }

    private static EditorLoad Loaded(double seconds = 30) =>
        new(EditorLoadOutcome.Loaded, TimeSpan.FromSeconds(seconds), [new float[600]], ExportFormat.Mp3);

    private sealed class SaveAndEditShell(SettingsScreen screen) : IAppShell
    {
        public bool HasWindowNow { get; set; } = true;

        public int Created { get; private set; }

        public int BroughtBack { get; set; }

        public bool HasWindow => HasWindowNow;

        public IAudioChoices AudioChoices => throw new NotSupportedException();

        public ISettingsProbe Probe => throw new NotSupportedException();

        public IAppLoop NewLoop() => throw new NotSupportedException();

        public INotificationView NewNotification(IAppLoop loop, bool offscreen) => throw new NotSupportedException();

        public ITrayIcon NewTray(IAppLoop loop) => throw new NotSupportedException();

        public ISessionEnd SessionEnd(IAppLoop loop) => throw new NotSupportedException();

        public IClipboardFiles Clipboard(IAppLoop loop) => throw new NotSupportedException();

        public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request)
        {
            Created++;
            return new FakeWindow(screen, this);
        }

        public IFolderShell NewFolderShell() => new OffscreenFolderShell(_ => { });

        public IPreviewPlayer NewPreviewPlayer() => throw new NotSupportedException();
    }

    private sealed class FakeWindow(SettingsScreen screen, SaveAndEditShell shell) : ISettingsWindow
    {
        public SettingsScreen Screen => screen;

        public Action? Activated { get; set; }

        public void Show()
        {
        }

        public void BringBack() => shell.BroughtBack++;

        public void Close()
        {
        }

        public void SetIcon(uint accent)
        {
        }

        public void ProbePicture(string path)
        {
        }

        public void Dispose()
        {
        }
    }

    // ---------------------------------------------------------------- what opens

    [Fact]
    public void With_no_window_the_editor_is_made_in_a_window_on_the_Library_page_on_the_new_clip()
    {
        var app = Built();
        var clip = WriteClip("new.mp3");

        var opening = app.OpenEditorOn(clip);

        Assert.Equal(EditorOpening.Opened, opening);
        Assert.Equal(1, _shell.Created);
        Assert.Equal(Section.Library, _rig.Screen.Section);
        Assert.Equal(clip.Paths, _rig.Screen.Editing!.Value.Clip.Files.Select(f => f.Path));
        Assert.Equal(EditorMode.Normal, _rig.Screen.Editing!.Value.Mode);
    }

    [Fact]
    public void A_window_on_another_page_with_a_clean_editor_comes_to_the_front_and_shows_the_editor_on_the_new_clip()
    {
        var app = Built();
        app.OpenEditorOn(WriteClip("first.mp3"));
        _rig.Screen.EditorLoaded(Loaded());
        _rig.Screen.ShowSection(Section.Audio);
        var second = WriteClip("second.mp3");

        var opening = app.OpenEditorOn(second);

        Assert.Equal(EditorOpening.Opened, opening);
        Assert.Equal(1, _shell.Created);
        Assert.Equal(1, _shell.BroughtBack);
        Assert.Equal(Section.Library, _rig.Screen.Section);
        Assert.Equal(second.Paths[0], _rig.Screen.Editing!.Value.Clip.Main.Path);
    }

    [Fact]
    public void An_editor_with_a_part_moved_is_kept_as_it_is_and_the_window_only_comes_to_the_front_and_the_journal_says_why()
    {
        var app = Built();
        var first = WriteClip("first.mp3");
        app.OpenEditorOn(first);
        _rig.Screen.EditorLoaded(Loaded());
        _rig.FocusOn(SettingsScreen.EditorStartId);
        _rig.Press(Vk.Right);
        var kept = _rig.Screen.EditorSelection!.StartMs;
        Assert.True(kept > 0);
        _rig.Screen.ShowSection(Section.Audio);

        var opening = app.OpenEditorOn(WriteClip("second.mp3"));

        Assert.Equal(EditorOpening.KeptWhatItShows, opening);
        Assert.Equal(1, _shell.BroughtBack);
        Assert.Equal(Section.Audio, _rig.Screen.Section);
        Assert.Equal(first.Paths[0], _rig.Screen.Editing!.Value.Clip.Main.Path);
        Assert.Equal(kept, _rig.Screen.EditorSelection!.StartMs);
        Assert.Contains(JournalLines(), line => line.Contains("editor not opened: the editor holds changes or a save in progress", StringComparison.Ordinal));
    }

    private void Click(string id)
    {
        var box = _rig.Screen.WidgetById(id)!.Bounds;
        _rig.Screen.MouseDown(box.MidX, box.MidY);
        _rig.Screen.MouseUp(box.MidX, box.MidY);
    }

    [Fact]
    public void An_editor_whose_save_is_running_is_never_replaced()
    {
        var app = Built();
        var first = WriteClip("first.mp3");
        app.OpenEditorOn(first);
        _rig.Screen.EditorLoaded(Loaded());
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Assert.Null(_rig.Screen.EditorDialog);

        var opening = app.OpenEditorOn(WriteClip("second.mp3"));

        Assert.True(_rig.Screen.EditorHoldsWork);
        Assert.Equal(EditorOpening.KeptWhatItShows, opening);
        Assert.Equal(first.Paths[0], _rig.Screen.Editing!.Value.Clip.Main.Path);
    }

    [Fact]
    public void An_editor_with_the_Save_dialog_open_is_never_replaced()
    {
        var app = Built();
        var first = WriteClip("first.mp3");
        app.OpenEditorOn(first);
        _rig.Screen.EditorLoaded(Loaded());
        Click(SettingsScreen.EditorSaveId);
        Assert.NotNull(_rig.Screen.EditorDialog);

        var opening = app.OpenEditorOn(WriteClip("second.mp3"));

        Assert.Equal(EditorOpening.KeptWhatItShows, opening);
        Assert.Equal(first.Paths[0], _rig.Screen.Editing!.Value.Clip.Main.Path);
    }

    [Fact]
    public void A_clip_whose_files_are_gone_opens_nothing_and_the_journal_says_so()
    {
        var app = Built();

        var opening = app.OpenEditorOn(new ClipResult([Path.Combine(Clips, "gone.mp3")], TimeSpan.FromSeconds(1), 1, false, [0f], false));

        Assert.Equal(EditorOpening.NotOpened, opening);
        Assert.Equal(0, _shell.Created);
        Assert.Contains(JournalLines(), line => line.Contains("editor not opened: the clip could not be read back", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- from the hotkey to the window

    private sealed class WritingBackend(string folder, bool captured) : IClipBackend
    {
        public AudioSnapshot Snapshot(TimeSpan duration) => captured ? new AudioSnapshot(new float[4800], []) : new AudioSnapshot([], []);

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName)
        {
            var path = Path.Combine(folder, "pressed.mp3");
            File.WriteAllBytes(path, new byte[2000]);
            return new ClipResult([path], TimeSpan.FromSeconds(0.1), 2000, false, [0.5f], false);
        }
    }

    // The clip path as the hotkey starts it: the press of a request that asks for the editor or not, ended by the app itself.
    private bool Press(HindsoundApp app, bool edit, bool captured, out ClipRequest? seen)
    {
        ClipRequest? request = null;
        using var done = new ManualResetEventSlim();
        var pipeline = new ClipPipeline(new WritingBackend(Clips, captured), work => work(), (_, _) => { }, _ => { })
        {
            Finished = outcome =>
            {
                request = outcome.Request;
                try
                {
                    app.OnClipFinished(outcome);
                }
                catch (Exception ex)
                {
                    Failure = ex;
                }

                done.Set();
            },
        };
        app.UsePipeline(pipeline);

        pipeline.Press(new ClipRequest(TimeSpan.FromSeconds(app.SettingsNow.ClipSeconds), new ExportOptions(), null, false) { EditAfter = edit });

        var finished = done.Wait(TimeSpan.FromSeconds(20));
        Assert.True(Failure is null, Failure?.ToString());
        seen = request;
        return finished;
    }

    [Fact]
    public void The_hotkey_saves_the_clip_with_the_length_of_Save_clip_then_opens_the_editor_on_it()
    {
        var app = Built();
        app.SettingsNow.ClipSeconds = 45;

        Assert.True(Press(app, edit: true, captured: true, out var request));

        Assert.True(request!.EditAfter);
        Assert.Equal(TimeSpan.FromSeconds(45), request.Requested);
        Assert.Equal(1, _shell.Created);
        Assert.Equal(Path.Combine(Clips, "pressed.mp3"), _rig.Screen.Editing!.Value.Clip.Main.Path);
        Assert.Contains(JournalLines(), line => line.Contains("save and edit: Opened", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_clip_never_opens_the_editor()
    {
        var app = Built();

        Assert.True(Press(app, edit: false, captured: true, out var request));

        Assert.False(request!.EditAfter);
        Assert.Equal(0, _shell.Created);
        Assert.False(_rig.Screen.EditorOpen);
    }

    [Fact]
    public void When_nothing_is_saved_no_window_opens()
    {
        var app = Built();

        Assert.True(Press(app, edit: true, captured: false, out var request));

        Assert.True(request!.EditAfter);
        Assert.Equal(0, _shell.Created);
        Assert.DoesNotContain(JournalLines(), line => line.Contains("save and edit", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- where there is no editor

    [Fact]
    public void Without_a_window_the_shortcut_is_left_out_of_what_is_registered_but_stays_in_the_settings()
    {
        var app = Built(hasWindow: false);
        app.SettingsNow.Hotkeys.SaveAndEdit = "Ctrl+Alt+E";
        app.SettingsNow.Hotkeys.CopyLast = "Ctrl+Alt+C";

        var registered = app.ToRegister(app.SettingsNow.Hotkeys);

        Assert.False(app.EditorAvailable);
        Assert.Null(registered.SaveAndEdit);
        Assert.Equal("Ctrl+Alt+C", registered.CopyLast);
        Assert.Equal("Ctrl+Alt+E", app.SettingsNow.Hotkeys.SaveAndEdit);
    }

    [Fact]
    public void With_a_window_the_shortcut_is_registered_with_the_others()
    {
        var app = Built();
        app.SettingsNow.Hotkeys.SaveAndEdit = "Ctrl+Alt+E";

        Assert.True(app.EditorAvailable);
        Assert.Equal("Ctrl+Alt+E", app.ToRegister(app.SettingsNow.Hotkeys).SaveAndEdit);
    }

    [Fact]
    public void Without_a_window_pressing_it_saves_nothing()
    {
        var app = Built(hasWindow: false);
        var backend = new CountingBackend();
        app.UsePipeline(new ClipPipeline(backend, work => work(), (_, _) => { }, _ => { }));

        app.OnHotkey(HotkeyAction.SaveAndEdit);

        Assert.Equal(0, backend.Snapshots);
    }

    private sealed class CountingBackend : IClipBackend
    {
        public int Snapshots { get; private set; }

        public AudioSnapshot Snapshot(TimeSpan duration)
        {
            Snapshots++;
            return new AudioSnapshot([], []);
        }

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName) => throw new InvalidOperationException();
    }
}
