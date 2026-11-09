using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The ways to the Library page, the time of a press, the folders kept, and the clips of the probe.</summary>
public sealed class LibraryGesturesTests
{
    private const Section LastPage = Section.Export;

    private sealed class FakeScreen(Section on, List<string> calls) : IWindowScreen
    {
        public Section Section { get; private set; } = on;

        public void ShowSection(Section section)
        {
            Section = section;
            calls.Add("show " + section);
        }

        public void ShowUpdates() => calls.Add("updates");

        public void ShowUpdateCard() => calls.Add("card");

        public void SelectClip(string path) => calls.Add("clip " + path);
    }

    private static (WindowGoing Going, List<string> Calls) Going(Section? openOn)
    {
        var calls = new List<string>();
        FakeScreen? open = openOn is { } on ? new FakeScreen(on, calls) : null;
        var going = new WindowGoing(
            () => true,
            () => open,
            () => calls.Add("bring back"),
            target =>
            {
                calls.Add("create " + target);
                open = new FakeScreen(target, calls);
            },
            _ => { });
        return (going, calls);
    }

    private static ClipResult Clip(params string[] paths) => new(paths, TimeSpan.FromSeconds(30), 1000, false, [0.5f], false, false);

    private static NotificationContent Saved(params string[] paths) =>
        NotificationStates.ForClip(Clip(paths), TimeSpan.FromSeconds(30), false, false, 0xFFFFFF);

    // ---------------------------------------------------------------- the table

    [Fact]
    public void Library_in_the_menu_lands_on_the_page_closed_or_open_and_never_leaves_a_card()
    {
        Assert.Equal(new WindowRoute(WindowAction.Show, Section.Library), WindowRoutes.Of(WindowGesture.LibraryMenu, null, LastPage));
        Assert.Equal(new WindowRoute(WindowAction.Show, Section.Library), WindowRoutes.Of(WindowGesture.LibraryMenu, Section.Now, LastPage));
        Assert.Equal(new WindowRoute(WindowAction.Show, Section.FirstRun), WindowRoutes.Of(WindowGesture.LibraryMenu, Section.FirstRun, LastPage));
    }

    [Fact]
    public void A_click_on_a_clip_never_leaves_a_card()
    {
        foreach (var gesture in new[] { WindowGesture.ClipSavedClick, WindowGesture.OtherClipClick })
        {
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.FirstRun), WindowRoutes.Of(gesture, Section.FirstRun, LastPage));
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.UpdateCard), WindowRoutes.Of(gesture, Section.UpdateCard, LastPage));
            Assert.Equal(new WindowRoute(WindowAction.Show, Section.Library), WindowRoutes.Of(gesture, null, LastPage));
        }
    }

    // ---------------------------------------------------------------- the clip of a click

    [Fact]
    public void A_click_on_clip_saved_makes_the_window_then_chooses_the_clip()
    {
        var (going, calls) = Going(null);

        going.Click(Saved(@"C:\Clips\a.mp3"));

        Assert.Equal(["create Library", @"clip C:\Clips\a.mp3"], calls);
    }

    [Fact]
    public void A_click_on_clip_saved_moves_an_open_window_then_chooses_the_clip()
    {
        var (going, calls) = Going(Section.Audio);

        going.Click(Saved(@"C:\Clips\a.mp3"));

        Assert.Equal(["bring back", "show Library", @"clip C:\Clips\a.mp3"], calls);
    }

    [Fact]
    public void A_card_stays_and_no_clip_is_chosen_behind_it()
    {
        var (going, calls) = Going(Section.UpdateCard);

        going.Click(Saved(@"C:\Clips\a.mp3"));

        Assert.Equal(["bring back", "show UpdateCard"], calls);
    }

    [Fact]
    public void A_word_about_no_clip_in_particular_chooses_nothing()
    {
        var (going, calls) = Going(null);

        going.Click(NotificationStates.LastClipGone());
        going.Click(null);

        Assert.DoesNotContain(calls, call => call.StartsWith("clip ", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_word_about_a_clip_names_its_first_file_which_is_the_main_one_of_two_tracks()
    {
        ClipResult[] clips =
        [
            Clip(@"C:\Clips\a_system.mp3", @"C:\Clips\a_mic.mp3"),
            new([@"C:\Clips\a_system.mp3"], TimeSpan.FromSeconds(30), 1000, IsSilent: true, [0.5f], false, false),
            new([@"C:\Clips\a_system.mp3"], TimeSpan.FromSeconds(30), 1000, false, [0.5f], Rescued: true, false),
            new([@"C:\Clips\a_system.mp3"], TimeSpan.FromSeconds(30), 1000, false, [0.5f], false, FellBackToWav: true),
        ];

        foreach (var clip in clips)
        {
            Assert.Equal(@"C:\Clips\a_system.mp3", NotificationStates.ForClip(clip, TimeSpan.FromSeconds(30), true, false, 0xFFFFFF).ClipPath);
            Assert.Equal(@"C:\Clips\a_system.mp3", NotificationStates.ForClip(clip, TimeSpan.FromSeconds(30), false, false, 0xFFFFFF).ClipPath);
        }
    }

    [Fact]
    public void A_clip_with_no_file_names_nothing()
    {
        Assert.Null(Saved().ClipPath);
    }

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void Library_comes_right_after_Open_Hindsound_and_the_numbers_of_the_others_do_not_move()
    {
        var items = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: false);

        Assert.Equal(new TrayMenuItem(TrayCommand.Library, "Library"), items[1]);
        Assert.Equal(11u, (uint)TrayCommand.OpenHindsound);
        Assert.Equal(12u, (uint)TrayCommand.Library);
        Assert.DoesNotContain(TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: true), item => item.Command == TrayCommand.Library);
        Assert.Equal(TrayCommand.OpenHindsound, TrayMenu.DefaultOf(items));
    }

    // ---------------------------------------------------------------- the time of a press

    private sealed class HeldBackend : IClipBackend
    {
        public readonly ManualResetEventSlim Save1 = new();

        public AudioSnapshot Snapshot(TimeSpan duration) =>
            duration == TimeSpan.FromSeconds(2) ? new AudioSnapshot([], []) : new AudioSnapshot(new float[2], []);

        ClipResult IClipBackend.Save(AudioSnapshot snapshot, ExportOptions options, string? appName)
        {
            Save1.Wait(TimeSpan.FromSeconds(20));
            return new ClipResult(["clip"], snapshot.Duration, 1, false, [], false);
        }
    }

    private static ClipRequest Request(int seconds) => new(TimeSpan.FromSeconds(seconds), new ExportOptions(), null, false);

    [Fact]
    public void No_press_no_time()
    {
        var pipeline = new ClipPipeline(new HeldBackend(), work => work(), (_, _) => { }, _ => { });

        Assert.Null(pipeline.OldestPressUtc);
    }

    [Fact]
    public void The_oldest_press_stays_until_it_ends_even_if_a_later_one_ends_first()
    {
        var backend = new HeldBackend();
        using var ended = new SemaphoreSlim(0);
        DateTime? seenAtTheEnd = DateTime.MinValue;
        ClipPipeline pipeline = null!;
        pipeline = new ClipPipeline(backend, work => work(), (_, _) => { }, _ => { })
        {
            Finished = outcome =>
            {
                if (outcome.Request.Requested == TimeSpan.FromSeconds(1))
                {
                    seenAtTheEnd = pipeline.OldestPressUtc;
                }

                ended.Release();
            },
        };
        var before = DateTime.UtcNow;

        pipeline.Press(Request(1));
        var oldest = pipeline.OldestPressUtc;
        Assert.NotNull(oldest);
        Assert.InRange(oldest.Value, before, DateTime.UtcNow);

        // The second has nothing to save, so it ends while the first is still being written.
        pipeline.Press(Request(2));
        Assert.True(ended.Wait(TimeSpan.FromSeconds(20)));
        Assert.Equal(oldest, pipeline.OldestPressUtc);

        backend.Save1.Set();
        Assert.True(ended.Wait(TimeSpan.FromSeconds(20)));

        // Whoever hears of the last clip reads a time that no longer counts it.
        Assert.Null(seenAtTheEnd);
        Assert.Null(pipeline.OldestPressUtc);
    }

    // ---------------------------------------------------------------- the folders kept

    private static SettingsEditor Editor(string? folder = @"D:\Old", params string[] known)
    {
        var settings = new AppSettings().Normalize();
        settings.Export.OutputFolder = folder ?? settings.Export.OutputFolder;
        settings.Export.KnownClipFolders = known.Length > 0 ? [.. known] : null;
        return new SettingsEditor(settings, _ => { });
    }

    [Fact]
    public void Leaving_a_folder_keeps_it_as_stored_most_recent_first()
    {
        var editor = Editor(@"%USERPROFILE%\Old", @"D:\Older");

        Assert.True(editor.SetFolder(@"D:\New").Ok);

        Assert.Equal(@"D:\New", editor.Settings.Export.OutputFolder);
        Assert.Equal([@"%USERPROFILE%\Old", @"D:\Older"], editor.Settings.Export.KnownClipFolders);
    }

    [Fact]
    public void Eight_folders_at_most_and_the_oldest_goes()
    {
        var known = Enumerable.Range(1, 8).Select(i => $@"D:\Known{i}").ToArray();
        var editor = Editor(@"D:\Old", known);

        editor.SetFolder(@"D:\New");

        var kept = editor.Settings.Export.KnownClipFolders!;
        Assert.Equal(8, kept.Count);
        Assert.Equal(@"D:\Old", kept[0]);
        Assert.DoesNotContain(@"D:\Known8", kept);
    }

    [Fact]
    public void Going_back_to_a_folder_left_takes_it_off_the_list()
    {
        var editor = Editor(@"D:\Old", @"D:\New");

        editor.SetFolder(@"D:\New");

        Assert.Equal([@"D:\Old"], editor.Settings.Export.KnownClipFolders);
    }

    [Theory]
    [InlineData(@"D:\Old")]
    [InlineData("")]
    [InlineData("Not full")]
    public void A_folder_that_does_not_change_or_is_refused_keeps_nothing(string path)
    {
        var editor = Editor(@"D:\Old");

        editor.SetFolder(path);

        Assert.Null(editor.Settings.Export.KnownClipFolders);
        Assert.Equal(@"D:\Old", editor.Settings.Export.OutputFolder);
    }

    [Fact]
    public void The_folders_kept_survive_the_file()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        var editor = Editor(@"D:\Old");
        editor.SetFolder(@"D:\New");

        var store = new SettingsStore(path);
        store.Save(editor.Settings);

        Assert.Equal([@"D:\Old"], new SettingsStore(path).Read().Settings!.Export.KnownClipFolders);
    }

    // ---------------------------------------------------------------- the clips of the probe

    [Fact]
    public void The_probe_has_a_folder_of_its_own_or_none()
    {
        using var temp = new TempFolder();
        var settings = temp.Combine("run.json");

        Assert.Null(LibraryProbeFiles.FolderFor(null));
        Assert.Null(LibraryProbeFiles.FolderFor(SettingsStore.DefaultPath));
        Assert.Equal(temp.Combine("run.library-probe"), LibraryProbeFiles.FolderFor(settings));

        Directory.CreateDirectory(temp.Combine("run.library-probe"));
        Assert.Null(LibraryProbeFiles.FolderFor(settings));
    }

    [Fact]
    public void The_clips_of_the_probe_are_listed_with_a_length_and_leave_nothing_behind()
    {
        using var temp = new TempFolder();
        var folder = temp.Combine("run.library-probe");

        LibraryProbeFiles.Create(folder, 5);
        var listing = ClipScanner.Scan(new ClipScanRequest { OutputFolder = folder, RescueFolder = folder });

        Assert.Equal(5, listing.Groups.Count);
        Assert.Equal(LibraryProbeFiles.PathOf(folder, 4), listing.Groups[0].Main.Path);
        Assert.Equal(TimeSpan.FromSeconds(0.1), AudioFileLength.Read(listing.Groups[0].Main.Path));

        LibraryProbeFiles.Remove(folder);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Removing_the_clips_of_the_probe_touches_nothing_else()
    {
        using var temp = new TempFolder();
        var folder = temp.Combine("run.library-probe");
        LibraryProbeFiles.Create(folder, 2);
        var other = Path.Combine(folder, "mine.mp3");
        File.WriteAllBytes(other, [1]);

        LibraryProbeFiles.Remove(folder);

        Assert.True(File.Exists(other));
        Assert.Single(Directory.GetFiles(folder));
    }
}
