using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>Coming back to the settings window lists the devices again, and an older list may arrive after a newer one.</summary>
public sealed class LatestLoadTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private sealed class Rig
    {
        private readonly object _gate = new();

        public Rig() => Load = new LatestLoad<string>(action =>
        {
            lock (_gate)
            {
                action();
            }

            Answered.Release();
        }, Delivered.Add, ex =>
        {
            Failures.Add(ex);
            Answered.Release();
        });

        public LatestLoad<string> Load { get; }

        public List<string> Delivered { get; } = [];

        public List<Exception> Failures { get; } = [];

        public SemaphoreSlim Answered { get; } = new(0);

        public void WaitForAnswers(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Assert.True(Answered.Wait(Limit));
            }
        }
    }

    [Fact]
    public void An_answer_arrives()
    {
        var rig = new Rig();

        rig.Load.Start(() => "list");
        rig.WaitForAnswers(1);

        Assert.Equal(["list"], rig.Delivered);
    }

    [Fact]
    public void An_older_answer_that_arrives_last_is_dropped()
    {
        var rig = new Rig();
        using var release = new ManualResetEventSlim();

        rig.Load.Start(() =>
        {
            release.Wait(Limit);
            return "old";
        });
        rig.Load.Start(() => "new");
        rig.WaitForAnswers(1);
        release.Set();
        rig.WaitForAnswers(1);

        Assert.Equal(["new"], rig.Delivered);
    }

    [Fact]
    public void An_older_answer_that_arrives_first_is_dropped_too()
    {
        var rig = new Rig();
        using var release = new ManualResetEventSlim();

        rig.Load.Start(() => "old");
        rig.WaitForAnswers(1);
        rig.Delivered.Clear();
        rig.Load.Start(() =>
        {
            release.Wait(Limit);
            return "new";
        });
        release.Set();
        rig.WaitForAnswers(1);

        Assert.Equal(["new"], rig.Delivered);
    }

    [Fact]
    public void A_failure_is_told_and_delivers_nothing()
    {
        var rig = new Rig();

        rig.Load.Start(() => throw new InvalidOperationException("no device"));
        rig.WaitForAnswers(1);

        Assert.Empty(rig.Delivered);
        Assert.Equal("no device", Assert.Single(rig.Failures).Message);
    }

    [Fact]
    public void A_failure_does_not_stop_the_next_load()
    {
        var rig = new Rig();

        rig.Load.Start(() => throw new InvalidOperationException("no device"));
        rig.WaitForAnswers(1);
        rig.Load.Start(() => "list");
        rig.WaitForAnswers(1);

        Assert.Equal(["list"], rig.Delivered);
    }

    [Fact]
    public void An_answer_for_a_window_that_has_closed_is_dropped()
    {
        var rig = new Rig();
        using var release = new ManualResetEventSlim();

        rig.Load.Start(() =>
        {
            release.Wait(Limit);
            return "for the closed window";
        });
        rig.Load.Forget();
        release.Set();
        rig.WaitForAnswers(1);

        Assert.Empty(rig.Delivered);
    }
}
