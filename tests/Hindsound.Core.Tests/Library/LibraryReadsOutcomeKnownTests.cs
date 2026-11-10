using System.Collections.Concurrent;
using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>
/// An action whose look fails after the action was applied has an outcome: the file was written, or refused. Saying that nothing
/// was saved would be false for a change that is on disk.
/// </summary>
public sealed class LibraryReadsOutcomeKnownTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly List<LibraryAction> _failed = [];
    private readonly List<(LibraryAction Action, LibraryOutcome Outcome)> _known = [];
    private bool _scanFails;
    private bool _deliveryFails;

    public void Dispose() => _temp.Dispose();

    private LibraryReads Reads(bool withKnown = true) => new(
        new LibraryStore(_temp.Combine("settings.json")),
        work =>
        {
            if (_deliveryFails)
            {
                // Once: the answer of the failure goes through the same door.
                _deliveryFails = false;
                throw new IOException(@"C:\Collection-Canary-7F3\pets.mp3 is being used");
            }

            _loop.Enqueue(work);
        },
        _ => { },
        () => { },
        null,
        (_, _, _) => _scanFails ? throw new IOException("busy") : new ClipListing([], []),
        (_, _) => null,
        work => work(),
        () => DateTime.UtcNow)
    {
        ActionFailed = _failed.Add,
        ActionOutcomeKnown = withKnown ? (action, outcome) => _known.Add((action, outcome)) : null,
    };

    private void Drain()
    {
        while (_loop.TryDequeue(out var work))
        {
            work();
        }
    }

    [Fact]
    public void A_look_that_fails_after_the_action_was_written_tells_what_became_of_it()
    {
        var reads = Reads();
        var action = LibraryAction.CreateCollection("pets");
        _deliveryFails = true;

        reads.Act(action, new ClipScanRequest());
        Drain();

        var told = Assert.Single(_known);
        Assert.Same(action, told.Action);
        Assert.Equal(LibraryOutcome.Done, told.Outcome);
        Assert.Empty(_failed);
        Assert.Contains("pets", File.ReadAllText(_temp.Combine("library.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_look_that_fails_after_the_action_was_refused_tells_that_it_was_refused()
    {
        File.WriteAllText(_temp.Combine("library.json"), "{ not json");
        var reads = Reads();
        _deliveryFails = true;

        reads.Act(LibraryAction.CreateCollection("pets"), new ClipScanRequest());
        Drain();

        Assert.Equal(LibraryOutcome.ReadOnly, Assert.Single(_known).Outcome);
        Assert.Empty(_failed);
    }

    [Fact]
    public void A_look_that_fails_before_the_action_is_applied_stays_unknown()
    {
        var reads = Reads();
        _scanFails = true;

        reads.Act(LibraryAction.CreateCollection("pets"), new ClipScanRequest());
        Drain();

        Assert.Single(_failed);
        Assert.Empty(_known);
        Assert.False(File.Exists(_temp.Combine("library.json")));
    }

    [Fact]
    public void With_nobody_to_tell_the_outcome_the_failure_is_told_as_before()
    {
        var reads = Reads(withKnown: false);
        _deliveryFails = true;

        reads.Act(LibraryAction.CreateCollection("pets"), new ClipScanRequest());
        Drain();

        Assert.Single(_failed);
    }
}
