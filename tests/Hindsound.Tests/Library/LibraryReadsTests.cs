using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>No window here: a fake loop the test turns by hand, a scan that can be held, durations that can be counted, threads that can be seen.</summary>
internal sealed class ReadsRig
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<Thread> _threads = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly ConcurrentQueue<string> _lengthsAsked = new();
    private readonly ConcurrentQueue<CancellationToken> _tokens = new();
    private int _scanning;
    private int _mostScanning;
    private int _scans;

    public ReadsRig(string settingsPath, Func<string, TimeSpan?>? length = null, Func<ClipScanRequest, ClipListing>? scan = null, DateTime? now = null)
    {
        Store = new LibraryStore(settingsPath, _journal.Enqueue);
        Reads = new LibraryReads(
            Store,
            _loop.Enqueue,
            Delivered.Add,
            () => LengthsTold++,
            _journal.Enqueue,
            (request, journal, cancel) =>
            {
                _tokens.Enqueue(cancel);
                _mostScanning = Math.Max(_mostScanning, Interlocked.Increment(ref _scanning));
                Interlocked.Increment(ref _scans);
                Asked.Enqueue(request);
                ScanEntered.Set();
                Assert.True(HoldScan.Wait(Limit), "the scan was never let go");
                try
                {
                    return scan is null ? ClipScanner.Scan(request, journal, cancel) : scan(request);
                }
                finally
                {
                    Interlocked.Decrement(ref _scanning);
                }
            },
            (path, journal) =>
            {
                _lengthsAsked.Enqueue(path);
                LengthEntered.Set();
                Assert.True(HoldLength.Wait(Limit), "the length was never let go");
                return length is null ? AudioFileLength.Read(path, journal) : length(path);
            },
            work =>
            {
                var thread = new Thread(() => work()) { IsBackground = true };
                _threads.Enqueue(thread);
                thread.Start();
            },
            () => now ?? DateTime.UtcNow);
    }

    public LibraryStore Store { get; }

    public LibraryReads Reads { get; }

    public List<LibrarySnapshot> Delivered { get; } = [];

    public int LengthsTold { get; private set; }

    public ManualResetEventSlim HoldScan { get; } = new(true);

    public ManualResetEventSlim ScanEntered { get; } = new(false);

    public ManualResetEventSlim HoldLength { get; } = new(true);

    public ManualResetEventSlim LengthEntered { get; } = new(false);

    public ConcurrentQueue<ClipScanRequest> Asked { get; } = new();

    public int Scans => Volatile.Read(ref _scans);

    public int MostScanning => _mostScanning;

    public int ThreadsStarted => _threads.Count;

    public IReadOnlyList<string> Journal => [.. _journal];

    public IReadOnlyList<string> LengthsAsked => [.. _lengthsAsked];

    public IReadOnlyList<CancellationToken> Tokens => [.. _tokens];

    public LibrarySnapshot Last => Delivered[^1];

    /// <summary>Waits for the work to end, and for its thread with it, then turns the loop.</summary>
    public void Settle()
    {
        Assert.True(SpinWait.SpinUntil(() => !Reads.IsWorking, Limit), "the work never ended");
        foreach (var thread in _threads)
        {
            Assert.True(thread.Join(Limit), "a thread was left running");
        }

        Turn();
    }

    public void Turn()
    {
        while (_loop.TryDequeue(out var action))
        {
            action();
        }
    }

    public void Wait(ManualResetEventSlim signal) => Assert.True(signal.Wait(Limit), "what was waited for never came");
}

public sealed class LibraryReadsTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    private string Clips => _root.Combine("clips");

    private string Settings => _root.Combine("settings.json");

    private string LibraryFile => _root.Combine("library.json");

    private ClipScanRequest Folders => new() { OutputFolder = Clips, RescueFolder = _root.Combine("no-rescue-folder") };

    public LibraryReadsTests()
    {
        Directory.CreateDirectory(Clips);
    }

    public void Dispose() => _root.Dispose();

    private ReadsRig Rig(Func<string, TimeSpan?>? length = null, Func<ClipScanRequest, ClipListing>? scan = null, DateTime? now = null) =>
        new(Settings, length ?? (_ => null), scan, now);

    private string Clip(string name, DateTime? written = null, int bytes = 10, string? folder = null)
    {
        folder ??= Clips;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, written ?? Noon);
        return path;
    }

    private static string[] Names(LibrarySnapshot snapshot) =>
        snapshot.Lines.Lines.Select(line => Path.GetFileName(line.Main.Path)).ToArray();

    private LibrarySnapshot Look(ReadsRig rig, ClipScanRequest? folders = null)
    {
        rig.Reads.Request(folders ?? Folders);
        rig.Settle();
        return rig.Last;
    }

    private LibrarySnapshot Act(ReadsRig rig, LibraryAction action)
    {
        rig.Reads.Act(action, Folders);
        rig.Settle();
        return rig.Last;
    }

    private static ClipGroup LineOf(string path, long size = 10, DateTime? written = null) =>
        new([new ClipFile(path, size, written ?? Noon)]);

    /// <summary>A favorite on this clip, written through the store by a first look.</summary>
    private void Star(string path)
    {
        var rig = Rig();
        rig.Reads.Act(LibraryAction.SetFavorite(LineOf(path), true), Folders with { KnownFolders = [Path.GetDirectoryName(path)!] });
        rig.Settle();
        Assert.Equal(LibraryOutcome.Done, rig.Last.Acted!.Outcome);
    }

    // ---------------------------------------------------------------- on request, and off the thread of the window

    [Fact]
    public void Nothing_is_read_without_a_request()
    {
        Clip("a.mp3");
        var rig = Rig();

        Thread.Sleep(50);
        rig.Turn();

        Assert.Equal(0, rig.Scans);
        Assert.Equal(0, rig.ThreadsStarted);
        Assert.Empty(rig.Delivered);
        Assert.Empty(rig.LengthsAsked);
        Assert.False(rig.Reads.IsWorking);
        Assert.False(File.Exists(LibraryFile));
    }

    [Fact]
    public void A_look_runs_off_the_calling_thread_and_answers_on_the_loop()
    {
        Clip("a.mp3");
        int? scanThread = null;
        var rig = Rig(scan: request =>
        {
            scanThread = Environment.CurrentManagedThreadId;
            return ClipScanner.Scan(request);
        });

        rig.Reads.Request(Folders);
        Assert.Empty(rig.Delivered);
        rig.Settle();

        Assert.NotNull(scanThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, scanThread);
        Assert.Equal(["a.mp3"], Names(rig.Last));
        Assert.Equal([Clips], rig.Last.FoldersRead);
        Assert.Equal(LibraryStatus.Missing, rig.Last.Library.Status);
        Assert.Null(rig.Last.Acted);
    }

    [Fact]
    public void A_look_writes_nothing()
    {
        var path = Clip("a.mp3");
        Star(path);
        File.Delete(path);
        var before = File.ReadAllBytes(LibraryFile);
        var date = File.GetLastWriteTimeUtc(LibraryFile);

        Look(Rig());

        Assert.Equal(before, File.ReadAllBytes(LibraryFile));
        Assert.Equal(date, File.GetLastWriteTimeUtc(LibraryFile));
    }

    [Fact]
    public void The_folders_the_index_names_are_read_whatever_the_request_says()
    {
        var elsewhere = _root.Combine("elsewhere");
        var path = Clip("old.mp3", folder: elsewhere);
        Star(path);
        var rig = Rig();

        var snapshot = Look(rig, Folders with { IndexFolders = [_root.Combine("not-this-one")] });

        Assert.Equal(["old.mp3"], Names(snapshot));
        Assert.True(snapshot.Labels.LabelsOf(snapshot.Lines.Lines[0]).Favorite);
        Assert.Equal([elsewhere], Assert.Single(rig.Asked).IndexFolders);
    }

    // ---------------------------------------------------------------- one at a time

    [Fact]
    public void Requests_during_a_look_give_one_more_look_with_the_newest_folders()
    {
        Clip("a.mp3");
        var rig = Rig();
        rig.HoldScan.Reset();

        rig.Reads.Request(Folders);
        rig.Wait(rig.ScanEntered);
        for (var i = 0; i < 5; i++)
        {
            rig.Reads.Request(Folders with { OutputFolder = _root.Combine("asked-" + i) });
        }

        rig.HoldScan.Set();
        rig.Settle();

        Assert.Equal(2, rig.Scans);
        Assert.Equal(1, rig.MostScanning);
        Assert.Equal(1, rig.ThreadsStarted);
        Assert.Equal(_root.Combine("asked-4"), rig.Asked.Last().OutputFolder);
        Assert.Equal(2, rig.Delivered.Count);
    }

    [Fact]
    public void A_request_after_the_end_starts_again_and_ends_again()
    {
        Clip("a.mp3");
        var rig = Rig();

        Look(rig);
        Assert.False(rig.Reads.IsWorking);
        Assert.Equal(1, rig.ThreadsStarted);

        Clip("b.mp3", Noon.AddHours(1));
        var second = Look(rig);

        Assert.Equal(["b.mp3", "a.mp3"], Names(second));
        Assert.Equal(2, rig.ThreadsStarted);
        Assert.False(rig.Reads.IsWorking);
    }

    [Fact]
    public void A_failed_look_leaves_the_screen_and_the_next_look_works()
    {
        Clip("a.mp3");
        var fail = true;
        var rig = Rig(scan: request => fail ? throw new InvalidOperationException("boom") : ClipScanner.Scan(request));

        rig.Reads.Request(Folders);
        rig.Settle();
        Assert.Empty(rig.Delivered);
        Assert.False(rig.Reads.IsWorking);

        fail = false;
        Assert.Equal(["a.mp3"], Names(Look(rig)));
    }

    [Fact]
    public void The_real_thread_pool_gives_the_same_answer()
    {
        var path = _root.Combine("clips", "a.wav");
        File.WriteAllBytes(path, Wav(1000, 3000));
        var loop = new BlockingCollection<Action>();
        var delivered = new List<LibrarySnapshot>();
        var known = 0;
        var reads = new LibraryReads(new LibraryStore(Settings), loop.Add, delivered.Add, () => known++);

        reads.Request(Folders);

        var until = DateTime.UtcNow.AddSeconds(20);
        while (known == 0 && DateTime.UtcNow < until)
        {
            if (loop.TryTake(out var action, 50))
            {
                action();
            }
        }

        Assert.Equal(["a.wav"], Names(Assert.Single(delivered)));
        Assert.Equal(3.0, reads.LengthOf(delivered[0].Lines.Lines[0])!.Value.TotalSeconds, 3);
        Assert.True(SpinWait.SpinUntil(() => !reads.IsWorking, TimeSpan.FromSeconds(20)));
    }

    // ---------------------------------------------------------------- the view closes

    [Fact]
    public void A_look_under_way_when_the_view_closes_delivers_nothing_and_reads_no_duration()
    {
        Clip("a.mp3");
        var rig = Rig();
        rig.HoldScan.Reset();

        rig.Reads.Request(Folders);
        rig.Wait(rig.ScanEntered);
        rig.Reads.Request(Folders);
        rig.Reads.Close();
        Assert.True(rig.Tokens[0].IsCancellationRequested);
        rig.HoldScan.Set();
        rig.Settle();

        Assert.Equal(1, rig.Scans);
        Assert.Empty(rig.Delivered);
        Assert.Empty(rig.LengthsAsked);
    }

    [Fact]
    public void An_answer_already_on_its_way_when_the_view_closes_is_dropped()
    {
        Clip("a.mp3");
        var rig = Rig(length: _ => TimeSpan.FromSeconds(1));

        rig.Reads.Request(Folders);
        Assert.True(SpinWait.SpinUntil(() => !rig.Reads.IsWorking, TimeSpan.FromSeconds(20)));
        rig.Reads.Close();
        rig.Turn();

        Assert.Empty(rig.Delivered);
        Assert.Equal(0, rig.LengthsTold);
    }

    [Fact]
    public void Closing_during_the_durations_stops_them_after_the_file_in_hand()
    {
        Clip("a.mp3", Noon.AddHours(2));
        Clip("b.mp3", Noon.AddHours(1));
        Clip("c.mp3", Noon);
        var rig = Rig(length: _ => TimeSpan.FromSeconds(1));
        rig.HoldLength.Reset();

        rig.Reads.Request(Folders);
        rig.Wait(rig.LengthEntered);
        rig.Turn();
        Assert.Single(rig.Delivered);
        rig.Reads.Close();
        rig.HoldLength.Set();
        rig.Settle();

        Assert.Single(rig.LengthsAsked);
        Assert.Equal(0, rig.LengthsTold);
    }

    [Fact]
    public void After_the_close_nothing_starts()
    {
        Clip("a.mp3");
        var rig = Rig();

        rig.Reads.Close();
        rig.Reads.Request(Folders);
        rig.Reads.Act(LibraryAction.CreateCollection("Fake"), Folders);
        rig.Settle();

        Assert.Equal(0, rig.ThreadsStarted);
        Assert.Equal(0, rig.Scans);
        Assert.False(File.Exists(LibraryFile));
    }

    [Fact]
    public void An_action_asked_for_before_the_close_is_written_and_then_the_work_ends()
    {
        var path = Clip("a.mp3");
        var rig = Rig();
        rig.HoldScan.Reset();

        rig.Reads.Act(LibraryAction.SetFavorite(LineOf(path), true), Folders);
        rig.Wait(rig.ScanEntered);
        rig.Reads.Close();
        Assert.False(rig.Tokens[0].IsCancellationRequested);
        rig.HoldScan.Set();
        rig.Settle();

        Assert.Empty(rig.Delivered);
        Assert.Empty(rig.LengthsAsked);
        var entry = Assert.Single(new LibraryStore(Settings).Read().Data.Clips);
        Assert.True(entry.Favorite);
        Assert.Equal(path, Assert.Single(entry.Files).Path);
    }

    [Fact]
    public void A_cancelled_scan_lists_no_folder_at_all()
    {
        Clip("a.mp3");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var listing = ClipScanner.Scan(Folders, cancel: cancel.Token);

        Assert.Empty(listing.Groups);
        Assert.Empty(listing.FoldersRead);
    }

    [Fact]
    public void A_scan_cancelled_between_two_folders_stops_there()
    {
        var known = _root.Combine("known");
        Clip("b.mp3", folder: known);
        using var cancel = new CancellationTokenSource();

        // A file where a folder should be can't be listed, and telling the journal so is the one moment to cut in here.
        var folders = Folders with { OutputFolder = Clip("not-a-folder.txt"), KnownFolders = [known] };
        var listing = ClipScanner.Scan(folders, _ => cancel.Cancel(), cancel.Token);

        Assert.True(cancel.IsCancellationRequested);
        Assert.Empty(listing.Groups);
        Assert.Empty(listing.FoldersRead);
    }

    // ---------------------------------------------------------------- the folders

    [Fact]
    public void An_absent_folder_is_an_empty_list_and_is_not_created()
    {
        var absent = _root.Combine("absent");
        var rig = Rig();

        var snapshot = Look(rig, Folders with { OutputFolder = absent });

        Assert.Empty(snapshot.Lines.Lines);
        Assert.Empty(snapshot.FoldersRead);
        Assert.Empty(rig.Journal);
        Assert.False(Directory.Exists(absent));
    }

    [Fact]
    public void A_refused_folder_is_left_out_and_keeps_its_labels_and_the_journal_never_sees_its_name()
    {
        var folder = _root.Combine("Collection-Canary-7F3");
        var inside = Clip("Collection-Canary-7F3.mp3", folder: folder);
        Clip("other.mp3");
        Star(inside);
        var directory = new DirectoryInfo(folder);
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var rules = directory.GetAccessControl();
        rules.AddAccessRule(denied);
        directory.SetAccessControl(rules);
        var rig = Rig();
        try
        {
            var snapshot = Look(rig);

            Assert.Equal(["other.mp3"], Names(snapshot));
            Assert.Equal([Clips], snapshot.FoldersRead);

            // An action writes the entries with this look: a folder not read does not start the count of days.
            Assert.Equal(LibraryOutcome.Done, Act(rig, LibraryAction.CreateCollection("Fake")).Acted!.Outcome);
        }
        finally
        {
            rules.RemoveAccessRule(denied);
            directory.SetAccessControl(rules);
        }

        var entry = Assert.Single(new LibraryStore(Settings).Read().Data.Clips);
        Assert.True(entry.Favorite);
        Assert.Null(entry.MissingSince);
        Assert.Contains(rig.Journal, line => line.Contains("UnauthorizedAccessException", StringComparison.Ordinal));
        AssertNoCanary(rig.Journal);
    }

    [Fact]
    public void A_file_written_since_the_clip_was_asked_for_is_not_listed_until_the_clip_ends()
    {
        var started = DateTime.UtcNow;
        Clip("whole.mp3", started.AddHours(-1));
        Clip("being-written.mp3", started.AddMilliseconds(100));
        var rig = Rig(length: _ => TimeSpan.FromSeconds(1));

        var during = Look(rig, Folders with { ClipStartedUtc = started });

        Assert.Equal(["whole.mp3"], Names(during));
        Assert.Empty(during.FoldersRead);
        Assert.DoesNotContain(rig.LengthsAsked, path => path.EndsWith("being-written.mp3", StringComparison.Ordinal));

        var after = Look(rig);

        Assert.Equal(["being-written.mp3", "whole.mp3"], Names(after));
        Assert.Equal([Clips], after.FoldersRead);
    }

    [Fact]
    public void An_action_during_a_save_does_not_start_the_count_on_the_clip_being_written()
    {
        var started = DateTime.UtcNow;
        var written = Clip("being-written.mp3", started.AddHours(-1));
        Star(written);
        File.SetLastWriteTimeUtc(written, started.AddMilliseconds(100));
        var rig = Rig();

        rig.Reads.Act(LibraryAction.CreateCollection("Fake"), Folders with { ClipStartedUtc = started });
        rig.Settle();

        Assert.Equal(LibraryOutcome.Done, rig.Last.Acted!.Outcome);
        Assert.Null(Assert.Single(new LibraryStore(Settings).Read().Data.Clips).MissingSince);
    }

    // ---------------------------------------------------------------- the cap

    private static ClipListing Many(int count)
    {
        var groups = new List<ClipGroup>(count);
        for (var i = 0; i < count; i++)
        {
            groups.Add(new ClipGroup([new ClipFile(@"C:\fake-clips\clip-" + i + ".mp3", 10 + i, Noon.AddSeconds(-i))]));
        }

        return new ClipListing(groups, [@"C:\fake-clips"]);
    }

    [Fact]
    public void Past_the_cap_only_the_labeled_clips_stay_and_the_index_is_given_them_all()
    {
        var listing = Many(ClipScanner.MaxLines + 100);
        var old = listing.Groups[ClipScanner.MaxLines + 50];
        var rig = Rig(scan: _ => listing, now: Noon);
        Assert.Equal(LibraryOutcome.Done, Act(rig, LibraryAction.SetFavorite(old, true)).Acted!.Outcome);

        var snapshot = rig.Last;

        Assert.Equal(ClipScanner.MaxLines + 1, snapshot.Lines.Lines.Count);
        Assert.Equal(99, snapshot.Lines.LeftOut);
        Assert.Same(old, snapshot.Lines.Lines[^1]);
        Assert.True(snapshot.Labels.LabelsOf(old).Favorite);
    }

    [Fact]
    public void An_action_on_another_line_does_not_count_a_clip_past_the_cap_as_gone()
    {
        var listing = Many(ClipScanner.MaxLines + 100);
        var rig = Rig(scan: _ => listing, now: Noon);
        Act(rig, LibraryAction.SetFavorite(listing.Groups[ClipScanner.MaxLines + 50], true));

        // Written with the look, this action would count the first clip as gone if the index were only given the lines.
        Act(rig, LibraryAction.SetFavorite(listing.Groups[1], true));

        var read = new LibraryStore(Settings).Read();
        Assert.Equal(2, read.Data.Clips.Count);
        Assert.All(read.Data.Clips, clip => Assert.Null(clip.MissingSince));
    }

    [Fact]
    public void An_action_finds_its_line_past_the_cap()
    {
        var listing = Many(ClipScanner.MaxLines + 100);
        var rig = Rig(scan: _ => listing, now: Noon);
        var old = listing.Groups[^1];

        Assert.Equal(LibraryOutcome.Done, Act(rig, LibraryAction.SetFavorite(old, true)).Acted!.Outcome);
    }

    // ---------------------------------------------------------------- actions, from a fresh look

    [Fact]
    public void A_star_on_a_line_is_written_and_comes_back_in_the_answer()
    {
        var path = Clip("a.mp3");
        var rig = Rig();
        var line = Look(rig).Lines.Lines[0];

        var snapshot = Act(rig, LibraryAction.SetFavorite(line, true));

        Assert.Equal(LibraryOutcome.Done, snapshot.Acted!.Outcome);
        Assert.True(snapshot.Labels.LabelsOf(snapshot.Lines.Lines[0]).Favorite);
        Assert.Equal(LibraryStatus.Loaded, snapshot.Library.Status);
        Assert.Equal(path, Assert.Single(new LibraryStore(Settings).Read().Data.Clips).Files[0].Path);
    }

    [Fact]
    public void An_action_on_a_clip_renamed_since_the_list_writes_nothing_and_gives_the_fresh_look()
    {
        var old = Clip("a.mp3");
        var rig = Rig();
        var line = Look(rig).Lines.Lines[0];
        File.Move(old, _root.Combine("clips", "b.mp3"));

        var snapshot = Act(rig, LibraryAction.SetFavorite(line, true));

        Assert.Equal(LibraryOutcome.ClipGone, snapshot.Acted!.Outcome);
        Assert.Equal(["b.mp3"], Names(snapshot));
        Assert.False(File.Exists(LibraryFile));
        Assert.False(snapshot.Labels.LabelsOf(snapshot.Lines.Lines[0]).Any);
    }

    [Fact]
    public void Taking_the_star_off_a_clip_renamed_since_the_list_leaves_it_and_its_line_shows_it()
    {
        var old = Clip("a.mp3");
        Star(old);
        var rig = Rig();
        var line = Look(rig).Lines.Lines[0];
        var renamed = _root.Combine("clips", "b.mp3");
        File.Move(old, renamed);
        var before = File.ReadAllBytes(LibraryFile);

        var snapshot = Act(rig, LibraryAction.SetFavorite(line, false));

        Assert.Equal(LibraryOutcome.ClipGone, snapshot.Acted!.Outcome);
        Assert.Equal(before, File.ReadAllBytes(LibraryFile));
        Assert.Equal(["b.mp3"], Names(snapshot));
        Assert.True(snapshot.Labels.LabelsOf(snapshot.Lines.Lines[0]).Favorite);
    }

    [Fact]
    public void After_that_an_action_on_the_new_line_moves_the_entry_with_it()
    {
        var old = Clip("a.mp3");
        Star(old);
        var rig = Rig();
        var renamed = _root.Combine("clips", "b.mp3");
        File.Move(old, renamed);
        var line = Look(rig).Lines.Lines[0];

        var snapshot = Act(rig, LibraryAction.SetFavorite(line, false));

        Assert.Equal(LibraryOutcome.Done, snapshot.Acted!.Outcome);
        Assert.False(snapshot.Labels.LabelsOf(snapshot.Lines.Lines[0]).Any);
        Assert.Empty(new LibraryStore(Settings).Read().Data.Clips);
    }

    [Fact]
    public void A_clip_deleted_since_the_list_writes_nothing()
    {
        var path = Clip("a.mp3");
        var rig = Rig();
        var line = Look(rig).Lines.Lines[0];
        File.Delete(path);

        var snapshot = Act(rig, LibraryAction.SetInCollection(line, "c1", true));

        Assert.Equal(LibraryOutcome.ClipGone, snapshot.Acted!.Outcome);
        Assert.Empty(snapshot.Lines.Lines);
        Assert.False(File.Exists(LibraryFile));
    }

    [Fact]
    public void The_action_labels_the_tracks_of_the_fresh_look_not_those_of_the_old_line()
    {
        var system = Clip("clip_system.mp3");
        var rig = Rig();
        var line = Look(rig).Lines.Lines[0];
        Assert.Single(line.Files);
        var mic = Clip("clip_mic.mp3");

        var snapshot = Act(rig, LibraryAction.SetFavorite(line, true));

        Assert.Equal(LibraryOutcome.Done, snapshot.Acted!.Outcome);
        var entry = Assert.Single(new LibraryStore(Settings).Read().Data.Clips);
        Assert.Equal(new[] { system, mic }, entry.Files.Select(file => file.Path).Order(StringComparer.Ordinal).Reverse());
    }

    [Fact]
    public void The_size_and_date_written_are_those_of_the_fresh_look()
    {
        var path = Clip("a.mp3", bytes: 10);
        var rig = Rig();
        var line = Look(rig).Lines.Lines[0];
        Clip("a.mp3", Noon.AddHours(1), bytes: 25);

        Act(rig, LibraryAction.SetFavorite(line, true));

        var file = Assert.Single(Assert.Single(new LibraryStore(Settings).Read().Data.Clips).Files);
        Assert.Equal(path, file.Path);
        Assert.Equal(25, file.Size);
        Assert.Equal(Noon.AddHours(1), file.Written);
    }

    [Fact]
    public void A_line_is_found_under_another_spelling_of_its_path()
    {
        var path = Clip("a.mp3");
        var rig = Rig();

        var snapshot = Act(rig, LibraryAction.SetFavorite(LineOf(path.ToUpperInvariant().Replace('\\', '/')), true));

        Assert.Equal(LibraryOutcome.Done, snapshot.Acted!.Outcome);
    }

    [Fact]
    public void A_line_with_no_file_is_gone_and_nothing_throws()
    {
        Clip("a.mp3");
        var rig = Rig();

        var snapshot = Act(rig, LibraryAction.SetFavorite(new ClipGroup([]), true));

        Assert.Equal(LibraryOutcome.ClipGone, snapshot.Acted!.Outcome);
        Assert.False(File.Exists(LibraryFile));
    }

    [Fact]
    public void Each_action_looks_first_and_they_keep_their_order()
    {
        var path = Clip("a.mp3");
        var rig = Rig();
        rig.HoldScan.Reset();
        var first = LibraryAction.CreateCollection("Fake one");
        var second = LibraryAction.CreateCollection("Fake one");
        var third = LibraryAction.SetInCollection(LineOf(path), "c1", true);

        rig.Reads.Act(first, Folders);
        rig.Wait(rig.ScanEntered);
        rig.Reads.Act(second, Folders);
        rig.Reads.Act(third, Folders);
        rig.HoldScan.Set();
        rig.Settle();

        Assert.Equal(3, rig.Scans);
        Assert.Equal(1, rig.MostScanning);
        Assert.Equal([first, second, third], rig.Delivered.Select(snapshot => snapshot.Acted!.Action));
        Assert.Equal(
            [LibraryOutcome.Done, LibraryOutcome.NameTaken, LibraryOutcome.Done],
            rig.Delivered.Select(snapshot => snapshot.Acted!.Outcome));
        Assert.Equal("c1", rig.Delivered[0].Acted!.CollectionId);
        Assert.Equal(["c1"], rig.Last.Labels.LabelsOf(rig.Last.Lines.Lines[0]).CollectionIds);
    }

    [Fact]
    public void Renaming_and_deleting_a_collection_go_the_same_way()
    {
        var path = Clip("a.mp3");
        var rig = Rig();
        Act(rig, LibraryAction.CreateCollection("Fake one"));
        Act(rig, LibraryAction.SetInCollection(LineOf(path), "c1", true));

        var renamed = Act(rig, LibraryAction.RenameCollection("c1", "Fake two"));
        Assert.Equal("Fake two", Assert.Single(renamed.Library.Data.Collections).Name);

        var deleted = Act(rig, LibraryAction.DeleteCollection("c1"));
        Assert.Empty(deleted.Library.Data.Collections);
        Assert.False(deleted.Labels.LabelsOf(deleted.Lines.Lines[0]).Any);
        Assert.Equal(10, new FileInfo(path).Length);
    }

    [Fact]
    public void A_broken_index_refuses_the_action_and_the_clips_stay_visible()
    {
        Clip("a.mp3");
        File.WriteAllText(LibraryFile, "{ not json");
        var before = File.ReadAllBytes(LibraryFile);
        var rig = Rig();

        var snapshot = Act(rig, LibraryAction.CreateCollection("Fake"));

        Assert.Equal(LibraryOutcome.ReadOnly, snapshot.Acted!.Outcome);
        Assert.Equal(LibraryStatus.Corrupt, snapshot.Library.Status);
        Assert.Equal(["a.mp3"], Names(snapshot));
        Assert.Equal(before, File.ReadAllBytes(LibraryFile));
    }

    [Fact]
    public void A_refused_action_answers_with_the_file_as_it_is()
    {
        Clip("a.mp3");
        var rig = Rig();

        var snapshot = Act(rig, LibraryAction.DeleteCollection("no-such-id"));

        Assert.Equal(LibraryOutcome.UnknownCollection, snapshot.Acted!.Outcome);
        Assert.Equal(["a.mp3"], Names(snapshot));
        Assert.False(File.Exists(LibraryFile));
    }

    // ---------------------------------------------------------------- the durations

    [Fact]
    public void The_name_comes_first_and_the_duration_after()
    {
        Clip("a.mp3");
        var rig = Rig(length: _ => TimeSpan.FromSeconds(7));
        rig.HoldLength.Reset();

        rig.Reads.Request(Folders);
        rig.Wait(rig.LengthEntered);
        rig.Turn();

        var line = Assert.Single(rig.Delivered).Lines.Lines[0];
        Assert.Null(rig.Reads.LengthOf(line));
        Assert.Equal(0, rig.LengthsTold);

        rig.HoldLength.Set();
        rig.Settle();

        Assert.Equal(1, rig.LengthsTold);
        Assert.Equal(TimeSpan.FromSeconds(7), rig.Reads.LengthOf(line));
    }

    [Fact]
    public void A_duration_read_is_not_read_again_until_the_path_the_size_or_the_date_change()
    {
        var path = Clip("a.mp3");
        var rig = Rig(length: _ => TimeSpan.FromSeconds(7));

        Look(rig);
        Look(rig);
        Assert.Single(rig.LengthsAsked);

        Clip("a.mp3", bytes: 11);
        Assert.Null(rig.Reads.LengthOf(LineOf(path, 11)));
        Look(rig);
        Assert.Equal(2, rig.LengthsAsked.Count);

        Clip("a.mp3", Noon.AddSeconds(1), bytes: 11);
        Look(rig);
        Assert.Equal(3, rig.LengthsAsked.Count);

        File.Move(path, _root.Combine("clips", "b.mp3"));
        var renamed = Look(rig);
        Assert.Equal(4, rig.LengthsAsked.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), rig.Reads.LengthOf(renamed.Lines.Lines[0]));
    }

    [Fact]
    public void Nothing_new_tells_nothing()
    {
        Clip("a.mp3");
        var rig = Rig(length: _ => TimeSpan.FromSeconds(7));

        Look(rig);
        Look(rig);

        Assert.Equal(1, rig.LengthsTold);
    }

    [Fact]
    public void A_duration_of_a_file_changed_or_gone_is_no_longer_given()
    {
        var path = Clip("a.mp3");
        var rig = Rig(length: _ => TimeSpan.FromSeconds(7));
        var line = Look(rig).Lines.Lines[0];
        Assert.NotNull(rig.Reads.LengthOf(line));

        File.Delete(path);
        Look(rig);

        Assert.Null(rig.Reads.LengthOf(line));
    }

    [Fact]
    public void Without_a_known_duration_the_line_has_none_and_the_next_look_tries_again()
    {
        var path = Clip("a.mp3");
        var answer = (TimeSpan?)null;
        var rig = Rig(length: _ => answer);

        var line = Look(rig).Lines.Lines[0];
        Assert.Null(rig.Reads.LengthOf(line));
        Assert.Equal(0, rig.LengthsTold);
        Assert.Equal([path], rig.LengthsAsked);

        answer = TimeSpan.FromSeconds(7);
        Look(rig);

        Assert.Equal(TimeSpan.FromSeconds(7), rig.Reads.LengthOf(line));
        Assert.Equal(1, rig.LengthsTold);
    }

    [Fact]
    public void The_other_track_gives_the_duration_when_the_first_has_none()
    {
        var system = Clip("clip_system.mp3");
        var mic = Clip("clip_mic.mp3");
        var rig = Rig(length: path => path == mic ? TimeSpan.FromSeconds(7) : null);

        var line = Assert.Single(Look(rig).Lines.Lines);

        Assert.Equal(system, line.Main.Path);
        Assert.Equal([system, mic], rig.LengthsAsked);
        Assert.Equal(TimeSpan.FromSeconds(7), rig.Reads.LengthOf(line));
    }

    [Fact]
    public void A_line_of_two_tracks_is_read_once_for_a_duration()
    {
        var system = Clip("clip_system.mp3");
        Clip("clip_mic.mp3");
        var rig = Rig(length: _ => TimeSpan.FromSeconds(7));

        Look(rig);

        Assert.Equal([system], rig.LengthsAsked);
    }

    [Fact]
    public void A_request_during_the_durations_ends_them_and_what_was_read_is_kept()
    {
        Clip("a.mp3", Noon.AddHours(2));
        Clip("b.mp3", Noon.AddHours(1));
        Clip("c.mp3", Noon);
        var looksAtEachAsk = new ConcurrentQueue<int>();
        ReadsRig rig = null!;
        rig = Rig(length: _ =>
        {
            looksAtEachAsk.Enqueue(rig.Scans);
            return TimeSpan.FromSeconds(7);
        });
        rig.HoldLength.Reset();

        rig.Reads.Request(Folders);
        rig.Wait(rig.LengthEntered);
        rig.Reads.Request(Folders);
        rig.HoldLength.Set();
        rig.Settle();

        Assert.Equal(2, rig.Scans);
        Assert.Equal(3, rig.LengthsAsked.Distinct().Count());
        Assert.Equal([1, 2, 2], looksAtEachAsk);
    }

    [Fact]
    public void An_action_during_the_durations_ends_them_too()
    {
        var newest = Clip("a.mp3", Noon.AddHours(2));
        Clip("b.mp3", Noon.AddHours(1));
        var looksAtEachAsk = new ConcurrentQueue<int>();
        ReadsRig rig = null!;
        rig = Rig(length: _ =>
        {
            looksAtEachAsk.Enqueue(rig.Scans);
            return TimeSpan.FromSeconds(7);
        });
        rig.HoldLength.Reset();

        rig.Reads.Request(Folders);
        rig.Wait(rig.LengthEntered);
        rig.Reads.Act(LibraryAction.SetFavorite(LineOf(newest, written: Noon.AddHours(2)), true), Folders);
        rig.HoldLength.Set();
        rig.Settle();

        Assert.Equal(LibraryOutcome.Done, rig.Last.Acted!.Outcome);
        Assert.Equal([1, 2], looksAtEachAsk);
    }

    [Fact]
    public void The_durations_are_newest_first()
    {
        Clip("old.mp3", Noon);
        Clip("new.mp3", Noon.AddHours(1));
        var rig = Rig(length: _ => TimeSpan.FromSeconds(7));

        Look(rig);

        Assert.Equal(["new.mp3", "old.mp3"], rig.LengthsAsked.Select(path => Path.GetFileName(path)));
    }

    [Fact]
    public void Another_view_reads_again()
    {
        Clip("a.mp3");
        var first = Rig(length: _ => TimeSpan.FromSeconds(7));
        Look(first);
        var second = Rig(length: _ => TimeSpan.FromSeconds(7));
        var line = Look(second).Lines.Lines[0];

        Assert.Single(second.LengthsAsked);
        Assert.Equal(TimeSpan.FromSeconds(7), second.Reads.LengthOf(line));
    }

    [Fact]
    public void A_real_file_gives_its_real_duration()
    {
        File.WriteAllBytes(_root.Combine("clips", "a.wav"), Wav(1000, 3000));
        File.WriteAllBytes(_root.Combine("clips", "false.wav"), [1, 2, 3]);
        var rig = new ReadsRig(Settings);

        var snapshot = Look(rig);
        var lengths = snapshot.Lines.Lines.ToDictionary(line => Path.GetFileName(line.Main.Path), rig.Reads.LengthOf);

        Assert.Equal(3.0, lengths["a.wav"]!.Value.TotalSeconds, 3);
        Assert.Null(lengths["false.wav"]);
    }

    // ---------------------------------------------------------------- privacy

    private static void AssertNoCanary(IEnumerable<string> journal)
    {
        foreach (var line in journal)
        {
            Assert.DoesNotContain("Canary", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("7F3", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Locked_files_tell_their_type_once_in_a_look_and_never_their_name()
    {
        var one = _root.Combine("clips", "Collection-Canary-7F3-one.wav");
        var two = _root.Combine("clips", "Collection-Canary-7F3-two.wav");
        File.WriteAllBytes(one, Wav(1000, 3000));
        File.WriteAllBytes(two, Wav(1000, 3000));
        var rig = new ReadsRig(Settings);

        using (new FileStream(one, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (new FileStream(two, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var snapshot = Look(rig);
            Assert.All(snapshot.Lines.Lines, shown => Assert.Null(rig.Reads.LengthOf(shown)));
        }

        var line = Assert.Single(rig.Journal);
        Assert.Contains("IOException", line, StringComparison.Ordinal);
        Assert.Contains("0x80070020", line, StringComparison.Ordinal);
        AssertNoCanary(rig.Journal);
    }

    [Fact]
    public void A_failed_look_tells_only_the_type_of_the_failure()
    {
        var rig = Rig(scan: _ => throw new IOException(@"C:\Collection-Canary-7F3\clip.mp3 is being used"));

        rig.Reads.Request(Folders);
        rig.Settle();

        var line = Assert.Single(rig.Journal);
        Assert.Contains("IOException", line, StringComparison.Ordinal);
        AssertNoCanary(rig.Journal);
    }

    [Fact]
    public void An_answer_that_fails_on_the_loop_tells_only_the_type_of_the_failure()
    {
        Clip("a.mp3");
        var loop = new ConcurrentQueue<Action>();
        var told = new ConcurrentQueue<string>();
        var reads = new LibraryReads(
            new LibraryStore(Settings),
            loop.Enqueue,
            _ => throw new IOException(@"C:\Collection-Canary-7F3\clip.mp3 is being used"),
            () => { },
            told.Enqueue);

        reads.Request(Folders);
        Assert.True(SpinWait.SpinUntil(() => !reads.IsWorking, TimeSpan.FromSeconds(20)));
        while (loop.TryDequeue(out var action))
        {
            action();
        }

        Assert.Contains("IOException", Assert.Single(told), StringComparison.Ordinal);
        AssertNoCanary(told);
    }

    [Fact]
    public void A_collection_named_like_the_canary_never_reaches_the_journal()
    {
        var path = Clip("Collection-Canary-7F3.mp3");
        var rig = new ReadsRig(Settings, _ => null);

        Act(rig, LibraryAction.CreateCollection("Collection-Canary-7F3"));
        Act(rig, LibraryAction.CreateCollection("Collection-Canary-7F3"));
        Act(rig, LibraryAction.SetInCollection(LineOf(path), "c1", true));
        Act(rig, LibraryAction.SetFavorite(LineOf(_root.Combine("clips", "Collection-Canary-7F3-gone.mp3")), true));
        File.WriteAllText(LibraryFile, "{ \"Collection-Canary-7F3\": ");
        Act(rig, LibraryAction.CreateCollection("Collection-Canary-7F3"));

        Assert.NotEmpty(rig.Journal);
        AssertNoCanary(rig.Journal);
    }

    // ---------------------------------------------------------------- building files

    private static byte[] Wav(uint bytesPerSecond, int dataBytes)
    {
        var wav = new byte[12 + 8 + 16 + 8 + dataBytes];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(wav.Length - 8));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24), bytesPerSecond);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), bytesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), 8);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), (uint)dataBytes);
        return wav;
    }
}
