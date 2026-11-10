using System.Collections.Concurrent;
using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>An action whose look fails still gets an answer: a screen waiting on it would otherwise wait for ever.</summary>
public sealed class LibraryReadsActionFailedTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly List<LibrarySnapshot> _delivered = [];
    private readonly List<LibraryAction> _failed = [];
    private bool _scanFails = true;

    public void Dispose() => _temp.Dispose();

    private LibraryReads Reads() => new(
        new LibraryStore(_temp.Combine("settings.json")),
        _loop.Enqueue,
        _delivered.Add,
        () => { },
        _journal.Enqueue,
        (_, _, _) => _scanFails ? throw new IOException(@"C:\Collection-Canary-7F3\pets.mp3 is being used") : new ClipListing([], []),
        (_, _) => null,
        work => work(),
        () => DateTime.UtcNow)
    {
        ActionFailed = _failed.Add,
    };

    private void Drain()
    {
        while (_loop.TryDequeue(out var work))
        {
            work();
        }
    }

    [Fact]
    public void A_failed_look_for_an_action_tells_that_action_and_writes_nothing()
    {
        var reads = Reads();
        var action = LibraryAction.CreateCollection("pets");

        reads.Act(action, new ClipScanRequest());
        Drain();

        Assert.Same(action, Assert.Single(_failed));
        Assert.Empty(_delivered);
        Assert.False(File.Exists(_temp.Combine("library.json")));
        Assert.DoesNotContain(_journal, line => line.Contains("Canary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_failed_look_with_no_action_tells_nothing()
    {
        var reads = Reads();

        reads.Request(new ClipScanRequest());
        Drain();

        Assert.Empty(_failed);
        Assert.Empty(_delivered);
    }

    [Fact]
    public void After_the_close_a_failure_is_not_told()
    {
        var reads = Reads();
        var held = new List<Action>();
        reads.Act(LibraryAction.CreateCollection("pets"), new ClipScanRequest());
        held.AddRange(_loop);
        _loop.Clear();

        reads.Close();
        held.ForEach(work => work());

        Assert.Empty(_failed);
    }

    [Fact]
    public void Start_over_goes_through_an_action_and_needs_no_clip()
    {
        _scanFails = false;
        File.WriteAllText(_temp.Combine("library.json"), "{ not json");
        var reads = Reads();

        reads.Act(LibraryAction.StartOver(), new ClipScanRequest());
        Drain();

        var acted = Assert.Single(_delivered).Acted!;
        Assert.Equal(LibraryOutcome.Done, acted.Outcome);
        Assert.Equal(LibraryStatus.Loaded, _delivered[0].Library.Status);
        Assert.Contains(Directory.GetFiles(_temp.Path, "library.json*"), file => File.ReadAllText(file) == "{ not json");
    }

    [Fact]
    public void Start_over_on_a_sound_file_is_refused_and_changes_nothing()
    {
        _scanFails = false;
        const string sound = """{"version":1,"collections":[{"id":"c1","name":"Sam"}]}""";
        File.WriteAllText(_temp.Combine("library.json"), sound);
        var reads = Reads();

        reads.Act(LibraryAction.StartOver(), new ClipScanRequest());
        Drain();

        Assert.Equal(LibraryOutcome.ReadOnly, Assert.Single(_delivered).Acted!.Outcome);
        Assert.Equal(sound, File.ReadAllText(_temp.Combine("library.json")));
    }
}
