using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

public sealed class ClipScannerTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    public ClipScannerTests()
    {
        Directory.CreateDirectory(Clips);
    }

    private string Clips => _root.Combine("clips");

    private string NoRescue => _root.Combine("no-rescue-folder");

    public void Dispose() => _root.Dispose();

    private static string Clip(string folder, string name, DateTime? written = null, int bytes = 10)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, written ?? Noon);
        return path;
    }

    private ClipListing Scan(DateTime? clipStarted = null, Action<string>? journal = null) =>
        ClipScanner.Scan(new ClipScanRequest { OutputFolder = Clips, RescueFolder = NoRescue, ClipStartedUtc = clipStarted }, journal);

    private static string[] Names(ClipListing listing) =>
        listing.Groups.SelectMany(g => g.Files).Select(f => Path.GetFileName(f.Path)).ToArray();

    // ---------------------------------------------------------------- what counts as a clip

    [Fact]
    public void A_file_gives_its_path_size_and_date()
    {
        var path = Clip(Clips, "a.mp3", Noon, bytes: 123);

        var file = Assert.Single(Scan().Groups).Main;

        Assert.Equal(new ClipFile(path, 123, Noon), file);
        Assert.Equal(DateTimeKind.Utc, file.WrittenUtc.Kind);
    }

    [Fact]
    public void Only_mp3_and_wav_of_any_case_with_something_in_them_are_clips()
    {
        Clip(Clips, "a.mp3");
        Clip(Clips, "b.wav");
        Clip(Clips, "C.MP3");
        Clip(Clips, "D.Wav");
        Clip(Clips, "empty.mp3", bytes: 0);
        Clip(Clips, "notes.txt");
        Clip(Clips, "a.mp3.tmp");
        Clip(Clips, "mp3");
        Directory.CreateDirectory(Path.Combine(Clips, "folder.mp3"));

        Assert.Equal(["a.mp3", "b.wav", "C.MP3", "D.Wav"], Names(Scan()).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Only_the_first_level_is_read()
    {
        Clip(Clips, "top.mp3");
        Clip(Path.Combine(Clips, "deeper"), "below.mp3");

        Assert.Equal(["top.mp3"], Names(Scan()));
    }

    // ---------------------------------------------------------------- the two tracks of a clip

    [Fact]
    public void System_and_mic_are_one_line_named_after_the_system_track()
    {
        Clip(Clips, "Hindsound_App_2026-03-10_12-00-00_mic.mp3");
        var system = Clip(Clips, "Hindsound_App_2026-03-10_12-00-00_system.mp3");

        var group = Assert.Single(Scan().Groups);

        Assert.Equal(2, group.Files.Count);
        Assert.Equal(system, group.Main.Path);
    }

    [Fact]
    public void Numbered_clips_pair_within_their_number()
    {
        Clip(Clips, "clip_system.mp3");
        Clip(Clips, "clip_mic.mp3");
        Clip(Clips, "clip (2)_system.mp3", Noon.AddMinutes(1));
        Clip(Clips, "clip (2)_mic.mp3", Noon.AddMinutes(1));

        var groups = Scan().Groups;

        Assert.Equal(2, groups.Count);
        Assert.Equal(["clip (2)_system.mp3", "clip (2)_mic.mp3"], groups[0].Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(["clip_system.mp3", "clip_mic.mp3"], groups[1].Files.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void A_lone_track_is_a_line_of_its_own()
    {
        Clip(Clips, "a_mic.mp3", Noon);
        Clip(Clips, "b_system.mp3", Noon.AddMinutes(1));

        var groups = Scan().Groups;

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Files));
        Assert.EndsWith("b_system.mp3", groups[0].Main.Path);
    }

    [Fact]
    public void Tracks_pair_whatever_the_case_of_the_names()
    {
        Clip(Clips, "CLIP_SYSTEM.MP3");
        Clip(Clips, "clip_mic.mp3");

        Assert.Equal(2, Assert.Single(Scan().Groups).Files.Count);
    }

    [Fact]
    public void Only_tracks_of_one_format_and_one_folder_pair()
    {
        Clip(Clips, "a_system.mp3");
        Clip(Clips, "a_mic.wav");
        Clip(Clips, "b_system.mp3");
        Clip(_root.Combine("other"), "b_mic.mp3");

        var listing = ClipScanner.Scan(new ClipScanRequest { OutputFolder = Clips, KnownFolders = [_root.Combine("other")], RescueFolder = NoRescue });

        Assert.Equal(4, listing.Groups.Count);
    }

    [Fact]
    public void A_name_merely_holding_the_suffix_stays_alone()
    {
        Clip(Clips, "my_mic_test.mp3");
        Clip(Clips, "my_system_test.mp3");
        Clip(Clips, "my.mp3");

        Assert.Equal(3, Scan().Groups.Count);
    }

    [Fact]
    public void An_empty_mic_track_beside_a_finished_system_track_leaves_the_system_track()
    {
        Clip(Clips, "a_system.mp3");
        Clip(Clips, "a_mic.mp3", bytes: 0);

        Assert.Equal(["a_system.mp3"], Names(Scan()));
    }

    [Fact]
    public void Real_saved_tracks_make_one_line_and_a_second_save_another()
    {
        var saver = new ClipSaver(NoRescue, () => new DateTime(2026, 3, 10, 12, 0, 0));
        var options = new ExportOptions { Format = ExportFormat.Wav, Normalization = NormalizationMode.Off, TrackMode = TrackMode.MultiTrack, OutputFolder = Clips };
        var snapshot = new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), Signals.Sine(300, 0.3, 0.5, channels: 1));
        var first = saver.Save(snapshot, options, "App");
        var second = saver.Save(snapshot, options, "App");

        var groups = Scan().Groups;

        Assert.Equal(2, groups.Count);
        var files = groups.SelectMany(g => g.Files).Select(f => f.Path).Order().ToArray();
        Assert.Equal(first.Paths.Concat(second.Paths).Order(), files);
        Assert.All(groups, g => Assert.Equal(2, g.Files.Count));
    }

    // ---------------------------------------------------------------- order

    [Fact]
    public void The_newest_comes_first_and_the_path_decides_between_equal_dates()
    {
        Clip(Clips, "old.mp3", Noon.AddHours(-1));
        Clip(Clips, "b.mp3", Noon);
        Clip(Clips, "A.mp3", Noon);
        Clip(Clips, "new.mp3", Noon.AddHours(1));

        Assert.Equal(["new.mp3", "A.mp3", "b.mp3", "old.mp3"], Names(Scan()));
    }

    [Fact]
    public void A_pair_takes_the_date_of_its_later_track()
    {
        Clip(Clips, "pair_system.mp3", Noon.AddHours(-2));
        Clip(Clips, "pair_mic.mp3", Noon.AddHours(1));
        Clip(Clips, "between.mp3", Noon);
        Clip(Clips, "pair2_mic.mp3", Noon.AddHours(-2));
        Clip(Clips, "pair2_system.mp3", Noon.AddHours(2));

        var mains = Scan().Groups.Select(g => Path.GetFileName(g.Main.Path)).ToArray();

        Assert.Equal(["pair2_system.mp3", "pair_system.mp3", "between.mp3"], mains);
    }

    // ---------------------------------------------------------------- the cap

    private static List<ClipGroup> Made(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ClipGroup([new ClipFile($@"D:\clips\{i}.mp3", 1, Noon.AddMinutes(-i))]))
            .ToList();

    [Fact]
    public void Up_to_the_cap_nothing_is_left_out_and_nothing_is_asked()
    {
        var groups = Made(ClipScanner.MaxLines);

        var lines = ClipScanner.Cap(groups, _ => throw new InvalidOperationException("nothing to ask"));

        Assert.Equal(ClipScanner.MaxLines, lines.Lines.Count);
        Assert.Equal(0, lines.LeftOut);
    }

    [Fact]
    public void The_cap_keeps_the_most_recent()
    {
        Assert.Equal(2000, ClipScanner.MaxLines);
        var groups = Made(ClipScanner.MaxLines + 5);

        var lines = ClipScanner.Cap(groups, _ => false);

        Assert.Equal(groups.Take(ClipScanner.MaxLines), lines.Lines);
        Assert.Equal(5, lines.LeftOut);
    }

    [Fact]
    public void A_labeled_clip_past_the_cap_is_kept_in_its_place()
    {
        var groups = Made(ClipScanner.MaxLines + 5);
        var labeled = groups[ClipScanner.MaxLines + 2];

        var lines = ClipScanner.Cap(groups, g => g == labeled);

        Assert.Equal(ClipScanner.MaxLines + 1, lines.Lines.Count);
        Assert.Same(labeled, lines.Lines[^1]);
        Assert.Equal(4, lines.LeftOut);
    }

    [Fact]
    public void The_cap_is_only_on_lines_what_the_index_reads_stays_whole()
    {
        for (var i = 0; i < 3; i++) Clip(Clips, $"{i}.mp3");

        var listing = Scan();

        Assert.Equal(3, listing.Groups.Count);
        Assert.Equal(2, ClipScanner.Cap(listing.Groups, _ => false, max: 2).Lines.Count);
    }

    // ---------------------------------------------------------------- a clip being saved

    [Fact]
    public void Files_written_since_the_clip_started_are_left_out_while_it_runs()
    {
        var started = Noon;
        Clip(Clips, "before.mp3", started.AddSeconds(-3));
        Clip(Clips, "edge.mp3", started.AddSeconds(-1)); // within the 2 s of a FAT drive: it may be the new one
        Clip(Clips, "after.mp3", started.AddSeconds(1));

        Assert.Equal(["before.mp3"], Names(Scan(clipStarted: started)));
        Assert.Equal(["after.mp3", "edge.mp3", "before.mp3"], Names(Scan()));
    }

    [Fact]
    public void The_limit_of_the_margin_itself_is_left_out()
    {
        Clip(Clips, "exact.mp3", Noon.AddSeconds(-2));

        Assert.Empty(Scan(clipStarted: Noon).Groups);
    }

    [Fact]
    public void Reserved_then_being_written_then_finished()
    {
        var started = DateTime.UtcNow;
        var paths = FileNameTemplate.ReserveUniquePaths(Clips, "clip", ["_system", "_mic"], ".wav");
        Assert.Empty(Scan(clipStarted: started).Groups);
        Assert.Empty(Scan().Groups); // reserved means empty

        using (var writer = Encoders.OpenWav(paths[0], 2))
        {
            writer.Write(Signals.Sine(300, 0.3, 0.5));
            Assert.Empty(Scan(clipStarted: started).Groups);
        }

        Assert.Empty(Scan(clipStarted: started).Groups); // the second track is still to come

        Encoders.WriteWav(paths[1], Signals.Sine(300, 0.3, 0.5, channels: 1), 1);
        Assert.Empty(Scan(clipStarted: started).Groups);

        Assert.Equal(2, Assert.Single(Scan().Groups).Files.Count);
    }

    // ---------------------------------------------------------------- the folders

    [Fact]
    public void All_kinds_of_folders_are_read_and_nothing_is_created()
    {
        Clip(Clips, "output.mp3");
        Clip(_root.Combine("left"), "left.mp3");
        Clip(_root.Combine("rescued"), "rescued.mp3");
        Clip(_root.Combine("cited"), "cited.mp3");

        var listing = ClipScanner.Scan(new ClipScanRequest
        {
            OutputFolder = Clips,
            KnownFolders = [_root.Combine("left"), _root.Combine("gone")],
            RescueFolder = _root.Combine("rescued"),
            IndexFolders = [_root.Combine("cited"), _root.Combine("also-gone")],
        });

        Assert.Equal(["cited.mp3", "left.mp3", "output.mp3", "rescued.mp3"], Names(listing).Order());
        Assert.False(Directory.Exists(_root.Combine("gone")));
        Assert.False(Directory.Exists(_root.Combine("also-gone")));
    }

    [Fact]
    public void An_absent_folder_throws_nothing_tells_nothing_and_is_not_called_read()
    {
        var log = new List<string>();
        var absent = _root.Combine("absent");

        var listing = ClipScanner.Scan(new ClipScanRequest { OutputFolder = absent, KnownFolders = ["Z:\\no-such-drive-here\\clips"], RescueFolder = NoRescue }, log.Add);

        Assert.Empty(listing.Groups);
        Assert.Empty(listing.FoldersRead);
        Assert.False(Directory.Exists(absent));
        Assert.False(Directory.Exists(NoRescue));
        Assert.Empty(log);
    }

    [Fact]
    public void An_empty_folder_is_read()
    {
        Assert.Equal([Clips], Scan().FoldersRead);
    }

    [Fact]
    public void A_folder_named_twice_is_read_once()
    {
        Clip(Clips, "a.mp3");

        var listing = ClipScanner.Scan(new ClipScanRequest
        {
            OutputFolder = Clips,
            KnownFolders = [Clips + Path.DirectorySeparatorChar, Clips.ToUpperInvariant()],
            RescueFolder = Clips,
            IndexFolders = [Clips],
        });

        Assert.Single(listing.Groups);
        Assert.Equal([Clips], listing.FoldersRead);
    }

    [Fact]
    public void A_folder_follows_the_rule_of_the_save()
    {
        const string variable = "HINDSOUND_TEST_SCANNER_FOLDER";
        Environment.SetEnvironmentVariable(variable, _root.Path);
        try
        {
            var real = Path.GetFullPath(ExportOptions.DefaultOutputFolder());

            Assert.Equal(_root.Combine("x"), ClipFolders.Resolve($"%{variable}%\\x"));
            Assert.Equal(real, ClipFolders.Resolve(""));
            Assert.Equal(real, ClipFolders.Resolve("   "));
            Assert.Equal(real, ClipFolders.Resolve(null));
            Assert.Equal(real, ClipFolders.Resolve(ExportSettings.DefaultFolder));
            Assert.Null(ClipFolders.Resolve("relative\\folder"));
            Assert.Null(ClipFolders.Resolve("%HINDSOUND_NO_SUCH_VARIABLE%\\x"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_folder_given_with_a_variable_is_read()
    {
        const string variable = "HINDSOUND_TEST_SCANNER_READ";
        Environment.SetEnvironmentVariable(variable, _root.Path);
        try
        {
            Clip(_root.Combine("x"), "a.mp3");

            var listing = ClipScanner.Scan(new ClipScanRequest { OutputFolder = $"%{variable}%\\x", RescueFolder = NoRescue });

            Assert.Equal(["a.mp3"], Names(listing));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void An_empty_output_field_means_the_folder_a_save_would_use()
    {
        var folders = ClipScanner.FoldersToRead(new ClipScanRequest { OutputFolder = "", RescueFolder = NoRescue });

        Assert.Equal([Path.GetFullPath(ExportOptions.DefaultOutputFolder()), NoRescue], folders);
    }

    [Fact]
    public void After_the_output_folder_changes_an_unlabeled_clip_is_still_listed()
    {
        var before = _root.Combine("before");
        var after = _root.Combine("after");
        Clip(before, "old-clip.mp3", Noon);
        Clip(after, "new-clip.mp3", Noon.AddMinutes(1));

        var known = ClipFolders.AfterOutputChange(null, before, after);
        var listing = ClipScanner.Scan(new ClipScanRequest { OutputFolder = after, KnownFolders = known, RescueFolder = NoRescue });

        Assert.Equal(["new-clip.mp3", "old-clip.mp3"], Names(listing));
    }

    // ---------------------------------------------------------------- privacy

    [Fact]
    public void A_refused_folder_is_not_called_read_and_its_path_never_reaches_the_journal()
    {
        var folder = _root.Combine("Collection-Canary-7F3");
        Clip(folder, "Collection-Canary-7F3.mp3");
        Clip(Clips, "other.mp3");
        var journal = new Journal(_root.Combine("refused.log"));
        var directory = new DirectoryInfo(folder);
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var rules = directory.GetAccessControl();
        rules.AddAccessRule(denied);
        directory.SetAccessControl(rules);
        try
        {
            var listing = ClipScanner.Scan(new ClipScanRequest { OutputFolder = folder, KnownFolders = [Clips], RescueFolder = NoRescue }, journal.Write);

            Assert.Equal(["other.mp3"], Names(listing));
            Assert.Equal([Clips], listing.FoldersRead);
        }
        finally
        {
            rules.RemoveAccessRule(denied);
            directory.SetAccessControl(rules);
        }

        var text = File.ReadAllText(journal.Path);
        Assert.Contains("UnauthorizedAccessException", text);
        Assert.Contains("0x80070005", text);
        Assert.DoesNotContain("Canary", text);
        Assert.DoesNotContain(_root.Path, text);
    }

    [Fact]
    public void An_absent_folder_or_a_folder_no_clip_can_go_to_never_writes_its_name()
    {
        var journal = new Journal(_root.Combine("absent.log"));

        var listing = ClipScanner.Scan(
            new ClipScanRequest
            {
                OutputFolder = _root.Combine("Collection-Canary-7F3"),
                KnownFolders = ["Collection-Canary-7F3\\relative", "C:\\Collection-Canary-7F3\\<>|?*"],
                RescueFolder = NoRescue,
            },
            journal.Write);

        Assert.Empty(listing.Groups);
        var text = File.ReadAllText(journal.Path);
        Assert.Contains("ArgumentException", text); // whose message holds the path
        Assert.DoesNotContain("Canary", text);
        Assert.DoesNotContain("relative", text);
    }
}
