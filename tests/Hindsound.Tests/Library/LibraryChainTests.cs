using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>
/// The list and the collections read together, on real files, in the order the window will call them: the scanner gives the
/// clips, the index finds its entries among them, the cap cuts the lines, an action writes, and the next look reads it back.
/// </summary>
public abstract class LibraryChainSetup : IDisposable
{
    protected const string Canary = "Collection-Canary-7F3";

    protected static readonly DateTime T = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    protected static readonly DateTime Day0 = new(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

    private int _side;

    protected LibraryChainSetup()
    {
        Store = new LibraryStore(Root.Combine("app", "settings.json"), Told.Add);
        Directory.CreateDirectory(Root.Combine("app"));
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Clips);
        OutputNow = Clips;
    }

    protected TempFolder Root { get; } = new();

    protected List<string> Told { get; } = [];

    protected LibraryStore Store { get; }

    protected string Clips => Root.Combine("clips");

    protected string Other => Root.Combine("other");

    protected string Rescue => Root.Combine("rescued");

    // Out of every folder read, as the Recycle Bin is.
    protected string Bin => Root.Combine("bin");

    protected string OutputNow { get; set; }

    protected IReadOnlyList<string> KnownNow { get; set; } = [];

    protected List<LibraryClip> Entries => Store.Read().Data.Clips;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root.Path, "*", new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Root.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One look: every clip goes to the index, and only then does the cap cut the lines.</summary>
    protected Look Read(DateTime? started = null)
    {
        var listing = ClipScanner.Scan(
            new ClipScanRequest
            {
                OutputFolder = OutputNow,
                KnownFolders = KnownNow,
                IndexFolders = ClipIndexMatch.FoldersCited(Entries),
                RescueFolder = Rescue,
                ClipStartedUtc = started,
            },
            Told.Add);
        var scan = new ClipScan(listing.Groups, listing.FoldersRead);
        var match = ClipIndexMatch.Match(Entries, scan);
        return new Look(listing, scan, match, ClipScanner.Cap(listing.Groups, g => match.LabelsOf(g).Any));
    }

    protected static string Clip(string folder, string name, long size = 10, DateTime? written = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, written ?? T);
        return path;
    }

    protected static ClipGroup GroupOf(Look look, string name) =>
        look.Listing.Groups.Single(g => g.Files.Any(f => string.Equals(Path.GetFileName(f.Path), name, StringComparison.OrdinalIgnoreCase)));

    protected static bool IsListed(Look look, string name) =>
        look.Listing.Groups.Any(g => g.Files.Any(f => string.Equals(Path.GetFileName(f.Path), name, StringComparison.OrdinalIgnoreCase)));

    protected static bool IsLabeled(Look look, string name) => look.Match.LabelsOf(GroupOf(look, name)).Any;

    protected void Star(string name, DateTime now, bool on = true)
    {
        var look = Read();
        Assert.Equal(LibraryOutcome.Done, Store.SetFavorite(GroupOf(look, name), on, look.Scan, now).Outcome);
    }

    protected string Collection(string name, DateTime now)
    {
        var change = Store.CreateCollection(name, Read().Scan, now);
        Assert.Equal(LibraryOutcome.Done, change.Outcome);
        return change.CollectionId!;
    }

    protected void Tick(string name, string id, DateTime now, bool on = true)
    {
        var look = Read();
        Assert.Equal(LibraryOutcome.Done, Store.SetInCollection(GroupOf(look, name), id, on, look.Scan, now).Outcome);
    }

    /// <summary>Any other action: what settles the entries and writes their dates.</summary>
    protected void Act(DateTime now, DateTime? started = null)
    {
        Assert.Equal(LibraryOutcome.Done, Store.CreateCollection("side " + _side++, Read(started).Scan, now).Outcome);
    }

    protected string Leave(string path)
    {
        var there = Path.Combine(Bin, Path.GetFileName(path));
        File.Move(path, there);
        return there;
    }

    protected static void MoveTo(string from, string folder, string? name = null)
    {
        Directory.CreateDirectory(folder);
        File.Move(from, Path.Combine(folder, name ?? Path.GetFileName(from)));
    }

    protected static string Spell(string folder, string kind, string variable) => kind switch
    {
        "case" => folder.ToUpperInvariant(),
        "separator" => folder + "\\",
        "slashes" => folder.Replace('\\', '/'),
        "prefix" => @"\\?\" + folder,
        "variable" => "%" + variable + "%\\" + Path.GetFileName(folder),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    protected string SpellingOf(string folder, string kind)
    {
        var variable = "HINDSOUND_CHAIN_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, Path.GetDirectoryName(folder));
        return Spell(folder, kind, variable);
    }

    protected static void Refused(string folder, Action whileRefused)
    {
        var directory = new DirectoryInfo(folder);
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var rules = directory.GetAccessControl();
        rules.AddAccessRule(denied);
        directory.SetAccessControl(rules);
        try
        {
            whileRefused();
        }
        finally
        {
            rules = directory.GetAccessControl();
            rules.RemoveAccessRule(denied);
            directory.SetAccessControl(rules);
        }
    }

    protected sealed record Look(ClipListing Listing, ClipScan Scan, IndexMatch Match, ClipLines Lines);
}

public sealed class LibraryChainTests : LibraryChainSetup
{
    [Fact]
    public void A_clip_renamed_moved_deleted_and_restored_keeps_its_labels_until_thirty_days_after_it_left()
    {
        KnownNow = [Other];
        Directory.CreateDirectory(Other);
        var id = Collection("pets", Day0);
        Clip(Clips, "a.mp3");
        Star("a.mp3", Day0);
        Tick("a.mp3", id, Day0);

        // Renamed. A look finds it and writes nothing.
        File.Move(Path.Combine(Clips, "a.mp3"), Path.Combine(Clips, "b.mp3"));
        var file = File.ReadAllText(Store.Path);
        var look = Read();
        Assert.True(look.Match.LabelsOf(GroupOf(look, "b.mp3")).Favorite);
        Assert.Equal([id], look.Match.LabelsOf(GroupOf(look, "b.mp3")).CollectionIds);
        Assert.Equal(file, File.ReadAllText(Store.Path));
        Act(Day0.AddDays(1));
        Assert.Equal(Path.Combine(Clips, "b.mp3"), Assert.Single(Assert.Single(Entries).Files).Path);

        // Moved to another known folder.
        MoveTo(Path.Combine(Clips, "b.mp3"), Other);
        Assert.True(IsLabeled(Read(), "b.mp3"));
        Act(Day0.AddDays(1));
        Assert.Equal(Path.Combine(Other, "b.mp3"), Assert.Single(Assert.Single(Entries).Files).Path);

        // Deleted: the count starts with the first action after, and is written.
        var inBin = Leave(Path.Combine(Other, "b.mp3"));
        Assert.Empty(Read().Lines.Lines);
        Act(Day0.AddDays(2));
        Assert.Equal(Day0.AddDays(2), Assert.Single(Entries).MissingSince);

        // Not moved by later actions, and not over yet one day before the end.
        Act(Day0.AddDays(31));
        Assert.Equal(Day0.AddDays(2), Assert.Single(Entries).MissingSince);

        // Restored: labels at the first look, the date gone at the next action.
        MoveTo(inBin, Other);
        Assert.True(IsLabeled(Read(), "b.mp3"));
        Act(Day0.AddDays(33));
        Assert.Null(Assert.Single(Entries).MissingSince);

        // Deleted past thirty days: the entry goes, the collection stays, a late return finds nothing.
        inBin = Leave(Path.Combine(Other, "b.mp3"));
        Act(Day0.AddDays(40));
        Act(Day0.AddDays(70));
        Assert.Empty(Entries);
        Assert.Contains(Store.Read().Data.Collections, c => c.Id == id);
        MoveTo(inBin, Other);
        Assert.False(IsLabeled(Read(), "b.mp3"));
    }

    [Fact]
    public void A_clip_of_two_tracks_lives_while_one_track_is_there()
    {
        var system = Clip(Clips, "x_system.mp3", written: T);
        var mic = Clip(Clips, "x_mic.mp3", written: T.AddSeconds(1));
        Star("x_mic.mp3", Day0);
        Assert.Equal(system, GroupOf(Read(), "x_mic.mp3").Main.Path);
        Assert.Equal(2, Assert.Single(Entries).Files.Count);

        // One leaves: the line goes on under the other, for good, and the entry remembers both.
        var inBin = Leave(system);
        var look = Read();
        Assert.Equal(mic, GroupOf(look, "x_mic.mp3").Main.Path);
        Assert.True(IsLabeled(look, "x_mic.mp3"));
        Act(Day0.AddDays(1));
        Act(Day0.AddDays(50));
        Assert.Null(Assert.Single(Entries).MissingSince);
        Assert.Equal(2, Assert.Single(Entries).Files.Count);

        // It comes back: one line again, one entry.
        MoveTo(inBin, Clips);
        look = Read();
        Assert.Equal(system, GroupOf(look, "x_mic.mp3").Main.Path);
        Assert.True(IsLabeled(look, "x_system.mp3"));
        Assert.Single(look.Listing.Groups);

        // Both leave and one comes back: the count starts, and ends.
        inBin = Leave(system);
        var micInBin = Leave(mic);
        Act(Day0.AddDays(60));
        Assert.Equal(Day0.AddDays(60), Assert.Single(Entries).MissingSince);
        MoveTo(micInBin, Clips);
        Assert.True(IsLabeled(Read(), "x_mic.mp3"));
        Act(Day0.AddDays(61));
        Assert.Null(Assert.Single(Entries).MissingSince);
        MoveTo(inBin, Clips);

        // Both renamed: the entry follows each.
        File.Move(system, Path.Combine(Clips, "y_system.mp3"));
        File.Move(mic, Path.Combine(Clips, "y_mic.mp3"));
        Assert.True(IsLabeled(Read(), "y_mic.mp3"));
        Act(Day0.AddDays(62));
        Assert.Equal(
            [Path.Combine(Clips, "y_mic.mp3"), Path.Combine(Clips, "y_system.mp3")],
            Assert.Single(Entries).Files.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_order_of_the_tracks_of_an_entry_does_not_have_to_be_the_order_of_the_line()
    {
        var id = Collection("pets", Day0);
        var mic = Clip(Clips, "x_mic.mp3", written: T);
        Star("x_mic.mp3", Day0);

        // The system track arrives after the labeling, and joins the entry behind the mic, while the line names it first.
        var system = Clip(Clips, "x_system.mp3", written: T.AddSeconds(1));
        Assert.True(IsLabeled(Read(), "x_system.mp3"));
        Tick("x_system.mp3", id, Day0);
        var entry = Assert.Single(Entries);
        Assert.Equal([mic, system], entry.Files.Select(f => f.Path));
        Assert.Equal(system, GroupOf(Read(), "x_mic.mp3").Main.Path);

        // So the survivor may be either of the two.
        var inBin = Leave(mic);
        Assert.True(IsLabeled(Read(), "x_system.mp3"));
        MoveTo(inBin, Clips);
        Leave(system);
        Assert.True(IsLabeled(Read(), "x_mic.mp3"));
    }

    [Fact]
    public void Two_tracks_labeled_apart_act_as_one_line()
    {
        var id = Collection("pets", Day0);
        var mic = Clip(Clips, "x_mic.mp3", written: T);
        Star("x_mic.mp3", Day0);
        var micInBin = Leave(mic);
        var system = Clip(Clips, "x_system.mp3", written: T.AddSeconds(1));
        Tick("x_system.mp3", id, Day0);
        MoveTo(micInBin, Clips);
        Assert.Equal(2, Entries.Count);

        var look = Read();
        var labels = look.Match.LabelsOf(Assert.Single(look.Listing.Groups));
        Assert.True(labels.Favorite);
        Assert.Equal([id], labels.CollectionIds);

        Star("x_system.mp3", Day0, on: false);
        Tick("x_mic.mp3", id, Day0, on: false);
        Assert.Empty(Entries);
        Assert.True(File.Exists(system));
    }

    [Fact]
    public void Among_identical_copies_a_doubt_is_no_link_until_only_one_remains()
    {
        var original = Clip(Clips, "original.mp3");
        var first = Clip(Clips, "copy 1.mp3");
        var second = Clip(Clips, "copy 2.mp3");
        Star("original.mp3", Day0);

        // The name decides while the file is there.
        var look = Read();
        Assert.True(IsLabeled(look, "original.mp3"));
        Assert.False(IsLabeled(look, "copy 1.mp3"));
        Assert.False(IsLabeled(look, "copy 2.mp3"));

        // Gone with two candidates: neither gets the labels, and the count runs.
        Leave(original);
        look = Read();
        Assert.False(IsLabeled(look, "copy 1.mp3"));
        Assert.False(IsLabeled(look, "copy 2.mp3"));
        Act(Day0.AddDays(1));
        Assert.Equal(Day0.AddDays(1), Assert.Single(Entries).MissingSince);

        // One candidate left: that one is the clip.
        Leave(second);
        Assert.True(IsLabeled(Read(), "copy 1.mp3"));
        Act(Day0.AddDays(2));
        var entry = Assert.Single(Entries);
        Assert.Null(entry.MissingSince);
        Assert.Equal(first, Assert.Single(entry.Files).Path);
    }

    [Fact]
    public void A_copy_of_a_labeled_clip_does_not_take_the_labels_of_a_dead_one()
    {
        var kept = Clip(Clips, "kept.mp3");
        var dead = Clip(Clips, "dead.mp3");
        Star("kept.mp3", Day0);
        Star("dead.mp3", Day0);
        Leave(dead);

        // The only other file of that size and date is named by an entry: it is that entry's.
        Assert.True(IsLabeled(Read(), "kept.mp3"));
        Act(Day0.AddDays(1));
        Assert.Equal(2, Entries.Count);
        Assert.Null(Entries.Single(e => e.Files[0].Path == kept).MissingSince);
        Assert.NotNull(Entries.Single(e => e.Files[0].Path == dead).MissingSince);
    }

    [Fact]
    public void A_change_of_output_folder_hides_no_clip_and_a_labeled_clip_outlives_the_cap_of_known_folders()
    {
        var first = Root.Combine("first");
        Clip(first, "plain.mp3", size: 11);
        Clip(first, "labeled.mp3", size: 12);
        OutputNow = first;
        Star("labeled.mp3", Day0);

        var output = first;
        IReadOnlyList<string> known = [];
        for (var i = 0; i < ExportSettingsLimit + 1; i++)
        {
            var next = Root.Combine("folder" + i);
            Directory.CreateDirectory(next);
            known = ClipFolders.AfterOutputChange(known, output, next);
            output = next;
            OutputNow = next;
            KnownNow = known;
            if (i < ExportSettingsLimit)
            {
                Assert.True(IsListed(Read(), "plain.mp3"));
            }
        }

        // The first folder has left the list of earlier folders, and only the index still names it.
        Assert.Equal(ExportSettingsLimit, known.Count);
        Assert.DoesNotContain(first, known, StringComparer.OrdinalIgnoreCase);
        var look = Read();
        Assert.True(IsLabeled(look, "labeled.mp3"));
        Assert.Contains(first, look.Listing.FoldersRead, StringComparer.OrdinalIgnoreCase);

        // Read through the index, it is whole: the plain clip beside the labeled one has a line too.
        Assert.True(IsListed(look, "plain.mp3"));
        Act(Day0.AddDays(40));
        Assert.Null(Assert.Single(Entries).MissingSince);

        // Back to an earlier folder under another spelling: it leaves the list, and is read once.
        var earlier = known[2];
        known = ClipFolders.AfterOutputChange(known, output, earlier + "\\");
        Assert.DoesNotContain(known, k => string.Equals(k.TrimEnd('\\'), earlier, StringComparison.OrdinalIgnoreCase));
        OutputNow = earlier + "\\";
        KnownNow = known;
        look = Read();
        Assert.Single(look.Listing.FoldersRead, f => string.Equals(f.TrimEnd('\\'), earlier, StringComparison.OrdinalIgnoreCase));
        Assert.True(IsLabeled(look, "labeled.mp3"));
    }

    private const int ExportSettingsLimit = Hindsound.Settings.ExportSettings.MaxKnownClipFolders;

    [Fact]
    public void The_rescue_folder_is_read_like_any_other_and_its_absence_counts_nothing()
    {
        var rescued = Clip(Rescue, "saved.mp3");
        Assert.True(IsListed(Read(), "saved.mp3"));
        Star("saved.mp3", Day0);
        Assert.Contains(Rescue, ClipIndexMatch.FoldersCited(Entries), StringComparer.OrdinalIgnoreCase);

        // The whole folder away: not read, so not gone.
        var away = Root.Combine("rescue-away");
        Directory.Move(Rescue, away);
        var look = Read();
        Assert.DoesNotContain(Rescue, look.Listing.FoldersRead, StringComparer.OrdinalIgnoreCase);
        Act(Day0.AddDays(1));
        Act(Day0.AddDays(50));
        Assert.Null(Assert.Single(Entries).MissingSince);
        Directory.Move(away, Rescue);
        Assert.True(IsLabeled(Read(), "saved.mp3"));

        // Moved by the user to the clips: the labels follow.
        MoveTo(rescued, Clips);
        Assert.True(IsLabeled(Read(), "saved.mp3"));
        Act(Day0.AddDays(51));
        Assert.Equal(Path.Combine(Clips, "saved.mp3"), Assert.Single(Assert.Single(Entries).Files).Path);
    }

    [Fact]
    public void An_absent_folder_counts_nothing_and_a_folder_moved_whole_is_followed()
    {
        Clip(Clips, "a.mp3");
        Star("a.mp3", Day0);
        var away = Root.Combine("away");

        Directory.Move(Clips, away);
        var look = Read();
        Assert.Empty(look.Listing.Groups);
        Assert.False(look.Scan.WasRead(Clips));
        Act(Day0.AddDays(1));
        Act(Day0.AddDays(50));
        Assert.Null(Assert.Single(Entries).MissingSince);
        Assert.Empty(Told);

        // The new place is known, the old one absent.
        KnownNow = [away];
        Assert.True(IsLabeled(Read(), "a.mp3"));
        Act(Day0.AddDays(51));
        Assert.Equal(Path.Combine(away, "a.mp3"), Assert.Single(Assert.Single(Entries).Files).Path);
    }

    [Fact]
    public void A_refused_folder_counts_nothing_and_is_told_without_its_path()
    {
        var file = Clip(Other, "a.mp3");
        Clip(Clips, "b.mp3", size: 11);
        KnownNow = [Other];
        Star("a.mp3", Day0);

        Refused(Other, () =>
        {
            var look = Read();
            Assert.False(look.Scan.WasRead(Other));
            Assert.True(look.Scan.WasRead(Clips));
            Assert.False(IsListed(look, "a.mp3"));
            Act(Day0.AddDays(1));
            Act(Day0.AddDays(50));
            Assert.Null(Assert.Single(Entries).MissingSince);
        });

        Assert.True(IsLabeled(Read(), "a.mp3"));
        Assert.True(File.Exists(file));
        Assert.Contains(Told, line => line.Contains("UnauthorizedAccessException", StringComparison.Ordinal));
        Assert.DoesNotContain(Told, line => line.Contains(Root.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void One_track_in_a_folder_not_read_stops_the_count_of_a_clip_whose_other_track_is_gone()
    {
        var system = Clip(Clips, "x_system.mp3", written: T);
        var mic = Clip(Clips, "x_mic.mp3", written: T.AddSeconds(1));
        Star("x_mic.mp3", Day0);
        MoveTo(mic, Other);

        // The mic track is known where it went, in a folder then taken away; the other track is deleted.
        KnownNow = [Other];
        Act(Day0.AddDays(1));
        KnownNow = [];
        var away = Root.Combine("away");
        Directory.Move(Other, away);
        Leave(system);
        Act(Day0.AddDays(2));
        Act(Day0.AddDays(60));
        Assert.Null(Assert.Single(Entries).MissingSince);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("separator")]
    [InlineData("slashes")]
    [InlineData("prefix")]
    [InlineData("variable")]
    public void A_folder_under_another_spelling_is_read_once_and_its_clips_keep_their_labels(string kind)
    {
        Clip(Clips, "a.mp3");
        Star("a.mp3", Day0);

        OutputNow = SpellingOf(Clips, kind);
        KnownNow = [Clips, SpellingOf(Clips, "separator"), SpellingOf(Clips, "case"), SpellingOf(Clips, "prefix"), SpellingOf(Clips, "variable")];
        var look = Read();
        Assert.Single(look.Listing.FoldersRead);
        Assert.Single(look.Listing.Groups);
        Assert.True(IsLabeled(look, "a.mp3"));

        Act(Day0.AddDays(1));
        Act(Day0.AddDays(50));
        Assert.Null(Assert.Single(Entries).MissingSince);
        Assert.True(IsLabeled(Read(), "a.mp3"));
    }

    [Theory]
    [InlineData("case")]
    [InlineData("separator")]
    [InlineData("slashes")]
    [InlineData("prefix")]
    [InlineData("variable")]
    public void An_absent_folder_under_another_spelling_counts_nothing(string kind)
    {
        Clip(Clips, "a.mp3");
        Star("a.mp3", Day0);
        var away = Root.Combine("away");
        Directory.Move(Clips, away);

        OutputNow = SpellingOf(Clips, kind);
        Act(Day0.AddDays(1));
        Act(Day0.AddDays(50));
        Assert.Null(Assert.Single(Entries).MissingSince);

        Directory.Move(away, Clips);
        Assert.True(IsLabeled(Read(), "a.mp3"));
    }

    [Theory]
    [InlineData("case")]
    [InlineData("separator")]
    [InlineData("slashes")]
    [InlineData("variable")]
    public void Under_another_spelling_the_clip_gone_is_counted_and_the_one_there_is_known_by_its_name(string kind)
    {
        var gone = Clip(Clips, "gone.mp3", size: 11);
        Clip(Clips, "there.mp3", size: 12);
        Clip(Clips, "copy of there.mp3", size: 12);
        Star("gone.mp3", Day0);
        Star("there.mp3", Day0);
        Leave(gone);

        OutputNow = SpellingOf(Clips, kind);
        var look = Read();
        Assert.True(IsLabeled(look, "there.mp3"));
        Assert.False(IsLabeled(look, "copy of there.mp3"));
        Act(Day0.AddDays(1));
        Assert.Equal(2, Entries.Count);
        Act(Day0.AddDays(31));
        Assert.Single(Entries);
    }

    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public void A_hidden_or_system_file_counts_as_gone_and_only_its_labels_are_at_stake(FileAttributes attribute)
    {
        var file = Clip(Clips, "a.mp3");
        Star("a.mp3", Day0);

        // Taken for gone, and kept for a while in case it comes back.
        File.SetAttributes(file, attribute);
        Assert.False(IsListed(Read(), "a.mp3"));
        Act(Day0.AddDays(1));
        Assert.Equal(Day0.AddDays(1), Assert.Single(Entries).MissingSince);
        File.SetAttributes(file, FileAttributes.Normal);
        Assert.True(IsLabeled(Read(), "a.mp3"));
        Act(Day0.AddDays(2));
        Assert.Null(Assert.Single(Entries).MissingSince);

        // Hidden past the thirty days: the labels go, the file is never touched.
        File.SetAttributes(file, attribute);
        Act(Day0.AddDays(3));
        Act(Day0.AddDays(33));
        Assert.Empty(Entries);
        Assert.True(File.Exists(file));
        File.SetAttributes(file, FileAttributes.Normal);
        Assert.False(IsLabeled(Read(), "a.mp3"));
    }

    [Fact]
    public void A_hidden_copy_is_no_candidate_for_a_lost_clip()
    {
        var original = Clip(Clips, "original.mp3");
        var hidden = Clip(Clips, "hidden.mp3");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Star("original.mp3", Day0);
        Leave(original);

        Assert.Empty(Read().Listing.Groups);
        Act(Day0.AddDays(1));
        Assert.Equal(original, Assert.Single(Assert.Single(Entries).Files).Path);
    }

    [Fact]
    public void Past_the_cap_a_labeled_clip_keeps_its_line_and_no_clip_cut_counts_as_gone()
    {
        const int total = ClipScanner.MaxLines + 100;
        for (var i = 0; i < total; i++)
        {
            Clip(Clips, $"clip {i:D4}.mp3", written: T.AddMinutes(i));
        }

        // The oldest of all, then a pair of old tracks.
        Clip(Clips, "old_system.mp3", written: T.AddDays(-2));
        Clip(Clips, "old_mic.mp3", written: T.AddDays(-2).AddSeconds(1));
        Clip(Clips, "plain old.mp3", written: T.AddDays(-3));
        Star("clip 0000.mp3", Day0);
        Star("old_mic.mp3", Day0);
        var id = Collection("pets", Day0);
        Tick("clip 0001.mp3", id, Day0);

        var look = Read();
        Assert.Equal(total + 2, look.Listing.Groups.Count);
        Assert.Equal(ClipScanner.MaxLines + 3, look.Lines.Lines.Count);
        Assert.Equal(total + 2 - ClipScanner.MaxLines - 3, look.Lines.LeftOut);
        Assert.Equal($"clip {total - 1:D4}.mp3", Path.GetFileName(look.Lines.Lines[0].Main.Path));
        Assert.Equal($"clip {total - ClipScanner.MaxLines:D4}.mp3", Path.GetFileName(look.Lines.Lines[ClipScanner.MaxLines - 1].Main.Path));
        Assert.Equal(["clip 0001.mp3", "clip 0000.mp3", "old_system.mp3"], look.Lines.Lines.Skip(ClipScanner.MaxLines).Select(g => Path.GetFileName(g.Main.Path)));
        Assert.DoesNotContain(look.Lines.Lines, g => g.Main.Path.EndsWith("plain old.mp3", StringComparison.OrdinalIgnoreCase));

        // Nothing cut counts as gone, however far the date: the index sees every clip.
        Act(Day0.AddDays(90));
        Assert.Equal(3, Entries.Count);
        Assert.All(Entries, entry => Assert.Null(entry.MissingSince));

        // A labeled clip renamed while past the cap is followed past the cap.
        File.Move(Path.Combine(Clips, "clip 0000.mp3"), Path.Combine(Clips, "renamed.mp3"));
        look = Read();
        Assert.Contains(look.Lines.Lines, g => g.Main.Path.EndsWith("renamed.mp3", StringComparison.OrdinalIgnoreCase));

        // A clip cut from the lines gets its line as soon as it is labeled.
        Star("plain old.mp3", Day0.AddDays(91));
        look = Read();
        Assert.Equal(ClipScanner.MaxLines + 4, look.Lines.Lines.Count);
        Assert.Equal("plain old.mp3", Path.GetFileName(look.Lines.Lines[^1].Main.Path));
    }

    [Fact]
    public void A_clip_being_saved_has_no_line_until_the_save_is_over()
    {
        Clip(Clips, "old.mp3", written: T);
        var started = T.AddHours(1);
        Clip(Clips, "new_system.mp3", written: started.AddSeconds(1));
        Clip(Clips, "new_mic.mp3", written: started.AddSeconds(2));

        var during = Read(started);
        Assert.True(IsListed(during, "old.mp3"));
        Assert.False(IsListed(during, "new_system.mp3"));
        Assert.False(IsListed(during, "new_mic.mp3"));

        var after = Read();
        Assert.Equal(2, after.Listing.Groups.Count);
        Assert.Equal("new_system.mp3", Path.GetFileName(after.Listing.Groups[0].Main.Path));
    }

    [Fact]
    public void Neither_journal_holds_a_name_the_user_chose()
    {
        var folder = Root.Combine(Canary);
        var refused = Root.Combine(Canary + "-refused");
        Directory.CreateDirectory(refused);
        var appFolder = Root.Combine(Canary + "-app");
        Directory.CreateDirectory(appFolder);
        var store = new LibraryStore(Path.Combine(appFolder, "settings.json"), Told.Add);
        var library = Path.Combine(appFolder, "library.json");
        var audio = Clip(folder, Canary + ".mp3");
        var sound = File.ReadAllBytes(audio);

        Look LookAt()
        {
            var data = store.Read().Data;
            var listing = ClipScanner.Scan(
                new ClipScanRequest
                {
                    OutputFolder = folder,
                    KnownFolders = [refused, Canary + "\\relative", @"C:\" + Canary + @"\<>|?*", Root.Combine(Canary + ".txt")],
                    IndexFolders = ClipIndexMatch.FoldersCited(data.Clips),
                    RescueFolder = Root.Combine(Canary + "-absent"),
                },
                Told.Add);
            var scan = new ClipScan(listing.Groups, listing.FoldersRead);
            var match = ClipIndexMatch.Match(data.Clips, scan);
            return new Look(listing, scan, match, ClipScanner.Cap(listing.Groups, g => match.LabelsOf(g).Any));
        }

        File.WriteAllText(Root.Combine(Canary + ".txt"), "a file where a folder should be");
        Refused(refused, () =>
        {
            var look = LookAt();
            var group = GroupOf(look, Canary + ".mp3");
            var id = store.CreateCollection(Canary, look.Scan, Day0).CollectionId!;
            Assert.Equal(LibraryOutcome.Done, store.SetInCollection(group, id, true, look.Scan, Day0).Outcome);
            Assert.Equal(LibraryOutcome.NameTaken, store.CreateCollection(Canary, look.Scan, Day0).Outcome);
            Assert.Equal(LibraryOutcome.UnknownCollection, store.RenameCollection(Canary, "x", look.Scan, Day0).Outcome);

            // Locked for reading, then for writing only.
            using (new FileStream(library, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(LibraryStatus.Unreadable, store.Read().Status);
                Assert.Equal(LibraryOutcome.ReadOnly, store.CreateCollection(Canary + "-2", look.Scan, Day0).Outcome);
            }

            using (new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(LibraryOutcome.NotWritten, store.CreateCollection(Canary + "-3", look.Scan, Day0).Outcome);
            }

            // The clip gone, then back, under a look at each step.
            var inBin = Leave(audio);
            look = LookAt();
            Assert.Equal(LibraryOutcome.Done, store.RenameCollection(id, Canary + "-4", look.Scan, Day0).Outcome);
            MoveTo(inBin, folder);

            // A name deleted is in no file of the folder.
            look = LookAt();
            Assert.Equal(LibraryOutcome.Done, store.DeleteCollection(id, look.Scan, Day0).Outcome);
            Assert.All(Directory.GetFiles(appFolder), file => Assert.DoesNotContain(Canary, File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));

            // A later version, a broken file that quotes the name, and a broken file whose copy can't be made.
            File.WriteAllText(library, $$"""{ "version": 2, "collections": [{ "id": "c1", "name": "{{Canary}}" }] }""");
            Assert.Equal(LibraryStatus.TooNew, store.Read().Status);
            Assert.Equal(LibraryOutcome.ReadOnly, store.CreateCollection(Canary + "-5", look.Scan, Day0).Outcome);
            File.WriteAllText(library, $$"""{ "version": 1, "collections": [{ "id": "{{Canary}}", "name": {{Canary}} }] }""");
            Assert.Equal(LibraryStatus.Corrupt, store.Read().Status);
            Directory.CreateDirectory(library + ".bak");
            Assert.Equal(LibraryOutcome.NotWritten, store.StartOver().Outcome);

            // A hand edit that cites a path no listing can open.
            Directory.Delete(library + ".bak");
            Assert.Equal(LibraryOutcome.Done, store.StartOver().Outcome);
            var bad = @"C:\\" + Canary + @"\\<>|?*\\" + Canary + ".mp3";
            File.WriteAllText(library, $$"""{ "version": 1, "collections": [], "clips": [{ "files": [{ "path": "{{bad}}", "size": 1, "written": "2026-01-01T00:00:00Z" }], "favorite": true }] }""");
            LookAt();
        });

        Assert.Contains(Told, line => line.StartsWith("clip scan:", StringComparison.Ordinal));
        Assert.Contains(Told, line => line.StartsWith("library:", StringComparison.Ordinal));
        Assert.DoesNotContain(Told, line => line.Contains("Canary", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Told, line => line.Contains(Root.Path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(sound, File.ReadAllBytes(audio));
    }
}

/// <summary>What the two sides did not agree on when they first met. Each of these fell the day the chain was first run.</summary>
public sealed class LibraryChainDefects : LibraryChainSetup
{
    [Fact]
    public void A_file_held_back_by_a_save_is_not_counted_as_gone()
    {
        Clip(Clips, "a.mp3", written: T);
        Star("a.mp3", Day0);

        // A save asked for just after this clip was written: the scanner leaves it out, the folder was read all the same.
        var started = T.AddSeconds(1);
        Assert.False(IsListed(Read(started), "a.mp3"));
        Act(Day0.AddDays(1), started);

        Assert.Null(Assert.Single(Entries).MissingSince);
    }

    [Fact]
    public void A_file_held_back_by_a_save_is_not_purged()
    {
        var file = Clip(Clips, "a.mp3", written: T);
        Star("a.mp3", Day0);
        var inBin = Leave(file);
        Act(Day0.AddDays(1));
        MoveTo(inBin, Clips);

        // Back, with no action since: the old date is still in the file. A file written just before a save is left out of the look.
        Act(Day0.AddDays(40), started: T.AddSeconds(1));

        Assert.Single(Entries);
    }

    [Fact]
    public void An_entry_under_the_long_form_of_its_folder_is_known_by_its_name()
    {
        Clip(Clips, "a.mp3");
        Clip(Clips, "copy of a.mp3");
        Star("a.mp3", Day0);

        OutputNow = SpellingOf(Clips, "prefix");
        var look = Read();

        Assert.True(IsLabeled(look, "a.mp3"));
        Assert.False(IsLabeled(look, "copy of a.mp3"));
    }

    [Fact]
    public void An_entry_saved_under_the_long_form_is_known_by_its_name_once_the_output_folder_is_plain()
    {
        OutputNow = SpellingOf(Clips, "prefix");
        Clip(Clips, "a.mp3");
        Clip(Clips, "copy of a.mp3");
        Star("a.mp3", Day0);

        OutputNow = Clips;
        var look = Read();

        Assert.True(IsLabeled(look, "a.mp3"));
        Assert.False(IsLabeled(look, "copy of a.mp3"));
    }

    [Fact]
    public void A_clip_gone_is_counted_and_purged_whichever_way_its_folder_was_spelled()
    {
        var gone = Clip(Clips, "gone.mp3");
        Star("gone.mp3", Day0);
        Leave(gone);

        OutputNow = SpellingOf(Clips, "prefix");
        Assert.True(Read().Scan.WasRead(Clips));
        Act(Day0.AddDays(1));
        Assert.Equal(Day0.AddDays(1), Assert.Single(Entries).MissingSince);
        Act(Day0.AddDays(31));

        Assert.Empty(Entries);
    }

    [Fact]
    public void A_hand_edit_that_turns_the_separators_of_an_entry_loses_nothing()
    {
        var file = Clip(Clips, "a.mp3");
        Star("a.mp3", Day0);
        File.WriteAllText(Store.Path, File.ReadAllText(Store.Path).Replace(@"\\", "/", StringComparison.Ordinal));
        Assert.Contains("/", Assert.Single(Assert.Single(Entries).Files).Path, StringComparison.Ordinal);

        // Edited in a sound editor: only the name can tell it now.
        File.WriteAllBytes(file, new byte[99]);
        Assert.True(IsLabeled(Read(), "a.mp3"));
        Act(Day0.AddDays(1));
        Assert.Null(Assert.Single(Entries).MissingSince);
        Act(Day0.AddDays(50));
        Assert.Single(Entries);
    }

    [Theory]
    [InlineData(12_345_670L, 20_000_000L)] // FAT: rounded up to the next even second
    [InlineData(12_345_670L, 12_300_000L)] // exFAT: cut to 10 ms
    public void A_clip_moved_to_a_drive_with_coarser_dates_keeps_its_labels(long ticksBefore, long ticksOnTheDrive)
    {
        var file = Clip(Clips, "a.mp3", written: T.AddTicks(ticksBefore));
        Star("a.mp3", Day0);
        KnownNow = [Other];

        // What the copy to such a drive does to the date: the scanner already allows for it, in the other direction.
        MoveTo(file, Other);
        File.SetLastWriteTimeUtc(Path.Combine(Other, "a.mp3"), T.AddTicks(ticksOnTheDrive));

        Assert.True(IsLabeled(Read(), "a.mp3"));
    }

    [Fact]
    public void Two_tracks_of_one_size_written_in_the_same_tick_keep_their_labels_when_renamed()
    {
        var system = Clip(Clips, "x_system.mp3");
        var mic = Clip(Clips, "x_mic.mp3");
        Star("x_mic.mp3", Day0);

        File.Move(system, Path.Combine(Clips, "y_system.mp3"));
        File.Move(mic, Path.Combine(Clips, "y_mic.mp3"));

        Assert.True(IsLabeled(Read(), "y_mic.mp3"));
    }
}
