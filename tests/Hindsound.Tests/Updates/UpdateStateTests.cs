using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateStateTests
{
    private static readonly UpdateVersion Next = new(0, 2, 0);

    [Fact]
    public void AMissingFileIsAnEmptyState()
    {
        using var folder = new TempFolder();

        var state = new UpdateStateStore(folder.File("state.json")).Load();

        Assert.Equal(new UpdateStateData(), state);
    }

    [Fact]
    public void AMissingFolderIsCreatedOnSave()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "updates", "state.json");

        Assert.True(new UpdateStateStore(path).Save(new UpdateStateData { HighestSeen = "1.0.0" }));
        Assert.Equal("1.0.0", new UpdateStateStore(path).Load().HighestSeen);
    }

    [Fact]
    public void AllTheFieldsComeBack()
    {
        using var folder = new TempFolder();
        var store = new UpdateStateStore(folder.File("state.json"));
        var when = new DateTimeOffset(2026, 11, 6, 14, 2, 0, TimeSpan.FromHours(1));

        Assert.True(store.Save(new UpdateStateData
        {
            LastCheck = when,
            HighestSeen = "0.3.0",
            AttemptsByVersion = new Dictionary<string, int> { ["0.2.0"] = 2 },
            ExpectedVersion = "0.2.0",
            AnnouncedVersion = "0.3.0",
            Pending = new PendingSetup("0.3.0", new string('a', 64), 8_200_000),
        }));

        var loaded = store.Load();
        Assert.Equal(when, loaded.LastCheck);
        Assert.Equal("0.3.0", loaded.HighestSeen);
        Assert.Equal(2, loaded.AttemptsByVersion!["0.2.0"]);
        Assert.Equal("0.2.0", loaded.ExpectedVersion);
        Assert.Equal("0.3.0", loaded.AnnouncedVersion);
        Assert.Equal(new PendingSetup("0.3.0", new string('a', 64), 8_200_000), loaded.Pending);
    }

    [Fact]
    public void TheFileUsesTheExpectedNames()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");

        new UpdateStateStore(path).Save(new UpdateStateData
        {
            LastCheck = DateTimeOffset.UnixEpoch,
            HighestSeen = "1.0.0",
            AttemptsByVersion = new Dictionary<string, int>(),
            ExpectedVersion = "1.0.0",
        });

        var text = File.ReadAllText(path);
        Assert.Contains("\"lastCheck\"", text);
        Assert.Contains("\"highestSeen\"", text);
        Assert.Contains("\"attemptsByVersion\"", text);
        Assert.Contains("\"expectedVersion\"", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"highestSeen\":")]
    [InlineData("{\"lastCheck\":\"yesterday\"}")]
    [InlineData("{\"attemptsByVersion\":[1,2]}")]
    [InlineData("{\"attemptsByVersion\":{\"0.2.0\":\"many\"}}")]
    public void ABrokenFileIsAnEmptyState(string content)
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        File.WriteAllText(path, content);

        Assert.Equal(new UpdateStateData(), new UpdateStateStore(path).Load());
    }

    [Fact]
    public void ThePlainJsonNullIsAnEmptyState()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        File.WriteAllText(path, "null");

        Assert.Equal(new UpdateStateData(), new UpdateStateStore(path).Load());
    }

    [Fact]
    public void BytesThatAreNotTextAreAnEmptyState()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x00, 0x80, 0xC3, 0x28]);

        Assert.Equal(new UpdateStateData(), new UpdateStateStore(path).Load());
    }

    [Fact]
    public void AnEnormousFileIsAnEmptyStateAndIsNotRead()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        File.WriteAllText(path, "{\"highestSeen\":\"1.0.0\"" + new string(' ', 65 * 1024) + "}");

        Assert.Equal(new UpdateStateData(), new UpdateStateStore(path).Load());
    }

    [Fact]
    public void ADirectoryInThePlaceOfTheFileIsAnEmptyStateAndAFailedSave()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        Directory.CreateDirectory(path);
        var store = new UpdateStateStore(path);

        Assert.Equal(new UpdateStateData(), store.Load());
        Assert.False(store.Save(new UpdateStateData()));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ALockedFileDoesNotThrow()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        var store = new UpdateStateStore(path);
        store.Save(new UpdateStateData { HighestSeen = "1.0.0" });

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(new UpdateStateData(), store.Load());
            Assert.False(store.Save(new UpdateStateData { HighestSeen = "2.0.0" }));
            Assert.False(File.Exists(path + ".tmp"));
        }

        Assert.Equal("1.0.0", store.Load().HighestSeen);
    }

    [Fact]
    public void AFailedSaveLeavesTheOldFileWhole()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        var store = new UpdateStateStore(path);
        store.Save(new UpdateStateData { HighestSeen = "1.0.0" });
        var before = File.ReadAllBytes(path);

        // The temporary file cannot be opened: the failure happens before the old file is touched.
        using (new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.False(store.Save(new UpdateStateData { HighestSeen = "2.0.0" }));
        }

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ASavedFileLeavesNoTemporaryBehind()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");

        new UpdateStateStore(path).Save(new UpdateStateData());

        Assert.Equal(["state.json"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void ANegativeCountGivesNoExtraAttempt()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("{\"attemptsByVersion\":{\"0.2.0\":-5,\"0.3.0\":2}}"));

        var loaded = new UpdateStateStore(path).Load();

        Assert.Equal(0, loaded.AttemptsFor(Next));
        Assert.Equal(2, loaded.AttemptsFor(new UpdateVersion(0, 3, 0)));
    }

    [Fact]
    public void AnAttemptIsCountedAndTheVersionExpected()
    {
        var once = new UpdateStateData().WithAttempt(Next);
        var twice = once.WithAttempt(Next);

        Assert.Equal(1, once.AttemptsFor(Next));
        Assert.Equal(2, twice.AttemptsFor(Next));
        Assert.Equal("0.2.0", twice.ExpectedVersion);
        Assert.Equal(0, new UpdateStateData().AttemptsFor(Next));
    }

    [Fact]
    public void TheHighestSeenNeverGoesDown()
    {
        var state = new UpdateStateData().WithSeen(Next);

        Assert.Equal("0.2.0", state.HighestSeen);
        Assert.Equal("0.3.0", state.WithSeen(new UpdateVersion(0, 3, 0)).HighestSeen);
        Assert.Equal("0.2.0", state.WithSeen(new UpdateVersion(0, 1, 0)).HighestSeen);
    }

    [Fact]
    public void TheDownloadedVersionNeedsTheFileAtItsSize()
    {
        using var folder = new TempFolder();
        var state = new UpdateStateData { Pending = new PendingSetup("0.2.0", new string('a', 64), 4) };

        Assert.Null(UpdateFiles.DownloadedVersion(new UpdateStateData(), folder.Path));
        Assert.Null(UpdateFiles.DownloadedVersion(state, folder.Path));

        File.WriteAllBytes(UpdateFiles.SetupPath(folder.Path, Next), [1, 2, 3]);
        Assert.Null(UpdateFiles.DownloadedVersion(state, folder.Path));

        File.WriteAllBytes(UpdateFiles.SetupPath(folder.Path, Next), [1, 2, 3, 4]);
        Assert.Equal(Next, UpdateFiles.DownloadedVersion(state, folder.Path));

        Assert.Null(UpdateFiles.DownloadedVersion(state with { Pending = new PendingSetup("../0.2.0", new string('a', 64), 4) }, folder.Path));
    }
}
