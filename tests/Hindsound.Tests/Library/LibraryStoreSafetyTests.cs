using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>What the store promises when it is used from several threads, and what it hands back when it does not write.</summary>
public sealed class LibraryStoreSafetyTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    public LibraryStoreSafetyTests() => Directory.CreateDirectory(_folder);

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private string LibraryPath => Path.Combine(_folder, "library.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static ClipGroup Clip(string name, long size = 1000) =>
        new([new ClipFile(Path.Combine(@"D:\Clips", name), size, new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc))]);

    // A new store for each call, as the app makes them: whatever keeps two calls apart cannot live in the instance.
    private LibraryStore Store() => new(SettingsPath);

    [Fact]
    public void ReadsAndActionsOnOtherThreads_NeverSeeAFileMidWrite_AndNoActionFails()
    {
        Assert.Equal(LibraryOutcome.Done, Store().CreateCollection("pets", ClipScan.Nothing, Now).Outcome);
        var stop = false;
        var badReads = 0;
        var reads = 0;
        var failedActions = 0;

        var readers = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                if (Store().Read().Status != LibraryStatus.Loaded)
                {
                    Interlocked.Increment(ref badReads);
                }

                Interlocked.Increment(ref reads);
            }
        })).ToArray();
        var writers = Enumerable.Range(0, 2).Select(w => new Thread(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                if (Store().SetFavorite(Clip($"clip{w}-{i}.mp3"), true, ClipScan.Nothing, Now).Outcome != LibraryOutcome.Done)
                {
                    Interlocked.Increment(ref failedActions);
                }
            }
        })).ToArray();

        foreach (var thread in readers.Concat(writers))
        {
            thread.Start();
        }

        foreach (var writer in writers)
        {
            Assert.True(writer.Join(TimeSpan.FromMinutes(2)));
        }

        Volatile.Write(ref stop, true);
        foreach (var reader in readers)
        {
            Assert.True(reader.Join(TimeSpan.FromMinutes(1)));
        }

        Assert.True(reads > 0);
        Assert.Equal(0, badReads);
        Assert.Equal(0, failedActions);
        var read = Store().Read();
        Assert.Equal(LibraryStatus.Loaded, read.Status);
        Assert.Single(read.Data.Collections);
        Assert.Equal(200, read.Data.Clips.Count);
    }

    [Fact]
    public void NotWritten_ShowsTheFileAsItIs_NotTheChangeThatFailed()
    {
        var store = Store();
        store.CreateCollection("pets", ClipScan.Nothing, Now);

        LibraryChange created;
        LibraryChange starred;
        using (new FileStream(LibraryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            created = store.CreateCollection("dogs", ClipScan.Nothing, Now);
            starred = store.SetFavorite(Clip("a.mp3"), true, ClipScan.Nothing, Now);
        }

        Assert.Equal(LibraryOutcome.NotWritten, created.Outcome);
        Assert.Equal("pets", Assert.Single(created.Library.Data.Collections).Name);
        Assert.Equal(LibraryOutcome.NotWritten, starred.Outcome);
        Assert.Empty(starred.Library.Data.Clips);
    }

    [Fact]
    public void ARefusal_ShowsTheFileAsItIs_NotTheEntriesTheActionSettled()
    {
        var store = Store();
        var scan = new ClipScan([], [@"D:\Clips"]);
        store.SetFavorite(Clip("a.mp3"), true, ClipScan.Nothing, Now);
        store.CreateCollection("pets", scan, Now);

        // Past the wait, the action drops the entry in memory before it finds the name taken.
        var change = store.CreateCollection("PETS", scan, Now.AddDays(31));

        Assert.Equal(LibraryOutcome.NameTaken, change.Outcome);
        Assert.Single(change.Library.Data.Clips);
        Assert.Single(Store().Read().Data.Clips);
    }

    // END
}
