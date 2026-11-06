using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>Repeated Alt+Tab on the settings window, next to a game: the lists must not pile up against a slow driver.</summary>
public sealed class LatestLoadLimitTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly List<string> _delivered = [];
    private readonly List<string> _started = [];
    private readonly SemaphoreSlim _startedOne = new(0);
    private readonly SemaphoreSlim _answered = new(0);
    private readonly ManualResetEventSlim _release = new();
    private readonly LatestLoad<string> _load;

    public LatestLoadLimitTests() => _load = new LatestLoad<string>(action =>
    {
        lock (_gate)
        {
            action();
        }

        _answered.Release();
    }, _delivered.Add, _ => _answered.Release());

    public void Dispose()
    {
        _release.Set();
        _release.Dispose();
        _startedOne.Dispose();
        _answered.Dispose();
    }

    private Func<string> Work(string name, bool fails = false) => () =>
    {
        lock (_started)
        {
            _started.Add(name);
        }

        _startedOne.Release();
        Assert.True(_release.Wait(Limit));
        return fails ? throw new InvalidOperationException(name) : name;
    };

    private void WaitForStarts(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Assert.True(_startedOne.Wait(Limit));
        }
    }

    private List<string> Started()
    {
        lock (_started)
        {
            return [.. _started];
        }
    }

    private void WaitUntilAnswered(Func<bool> done)
    {
        var until = DateTime.UtcNow + Limit;
        while (true)
        {
            lock (_gate)
            {
                if (done())
                {
                    return;
                }
            }

            Assert.True(DateTime.UtcNow < until);
            _answered.Wait(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public void Four_starts_run_two_at_a_time_and_only_the_newest_waits()
    {
        _load.Start(Work("first"));
        _load.Start(Work("second"));
        _load.Start(Work("third"));
        _load.Start(Work("fourth"));
        WaitForStarts(2);
        Assert.False(_startedOne.Wait(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(2, Started().Count);

        _release.Set();
        WaitForStarts(1);
        WaitUntilAnswered(() => _delivered.Contains("fourth"));

        Assert.Equal(3, Started().Count);
        Assert.Contains("fourth", Started());
        Assert.DoesNotContain("third", Started());
        Assert.Equal("fourth", _delivered[^1]);
    }

    [Fact]
    public void A_load_that_fails_gives_its_place_to_the_one_that_waits()
    {
        _load.Start(Work("first", fails: true));
        _load.Start(Work("second", fails: true));
        _load.Start(Work("third"));
        WaitForStarts(2);

        _release.Set();
        WaitForStarts(1);
        WaitUntilAnswered(() => _delivered.Contains("third"));

        Assert.Equal(["third"], _delivered);
    }

    [Fact]
    public void A_window_that_closes_lets_go_of_the_one_that_waits()
    {
        _load.Start(Work("first"));
        _load.Start(Work("second"));
        _load.Start(Work("for the closed window"));
        WaitForStarts(2);

        _load.Forget();
        _release.Set();
        Assert.True(_answered.Wait(Limit));
        Assert.True(_answered.Wait(Limit));

        Assert.False(_startedOne.Wait(TimeSpan.FromMilliseconds(200)));
        Assert.DoesNotContain("for the closed window", Started());
        Assert.Empty(_delivered);
    }

    [Fact]
    public void The_places_are_free_again_after_the_answers()
    {
        _load.Start(Work("first"));
        _load.Start(Work("second"));
        WaitForStarts(2);
        _release.Set();
        Assert.True(_answered.Wait(Limit));
        Assert.True(_answered.Wait(Limit));

        _load.Start(Work("third"));
        _load.Start(Work("fourth"));
        WaitForStarts(2);
        WaitUntilAnswered(() => _delivered.Contains("fourth"));

        Assert.Equal(4, Started().Count);
    }
}
