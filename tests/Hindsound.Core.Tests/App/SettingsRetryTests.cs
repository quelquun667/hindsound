using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>A file that could not be read at the start: a custom shortcut is not registered, so no press would ever read it.</summary>
public sealed class SettingsRetryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-retry-").FullName;
    private readonly Queue<(int Delay, Action Step)> _timers = new();
    private long _now;

    private string Path => System.IO.Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private SettingsRetry Retry(SettingsFileState state, Action read) =>
        new(() => state.AwaitingRead, () => _now, (delay, step) => _timers.Enqueue((delay, step)), read);

    private int FireNext()
    {
        var (delay, step) = _timers.Dequeue();
        step();
        return delay;
    }

    [Fact]
    public void A_file_read_at_the_start_arms_nothing()
    {
        var state = new SettingsFileState();
        state.Started(SettingsLoadStatus.Loaded, null);

        Retry(state, () => throw new InvalidOperationException("no read expected")).Arm();

        Assert.Empty(_timers);
    }

    [Fact]
    public void A_file_locked_at_the_start_is_read_by_itself_once_the_lock_falls_and_the_looks_stop()
    {
        File.WriteAllText(Path, """{"bufferSeconds":60}""");
        var store = new SettingsStore(Path);
        var state = new SettingsFileState();
        AppSettings? applied = null;
        void Read()
        {
            var read = store.Read();
            if (state.Reread(read.Status, default).Apply)
            {
                applied = read.Settings;
            }
        }

        var locked = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        state.Started(store.Read().Status, null);
        Assert.True(state.AwaitingRead);

        Retry(state, Read).Arm();
        FireNext();
        FireNext();

        Assert.Null(applied);
        Assert.True(state.AwaitingRead);
        Assert.Single(_timers);

        locked.Dispose();
        FireNext();

        Assert.Equal(60, applied?.BufferSeconds);
        Assert.False(state.AwaitingRead);
        Assert.Empty(_timers);
    }

    [Fact]
    public void A_long_lock_keeps_being_looked_at_with_a_slower_pace()
    {
        var state = new SettingsFileState();
        state.Started(SettingsLoadStatus.Unreadable, null);
        Retry(state, () => { }).Arm();

        Assert.Equal(PendingNotices.FastPollMs, FireNext());

        _now = PendingNotices.FastPollingLimitMs;
        Assert.Equal(PendingNotices.FastPollMs, FireNext());
        Assert.Equal(PendingNotices.SlowPollMs, FireNext());
        Assert.Single(_timers);
    }

    [Fact]
    public void Arming_twice_never_makes_two_looks()
    {
        var state = new SettingsFileState();
        state.Started(SettingsLoadStatus.Unreadable, null);
        var retry = Retry(state, () => { });

        retry.Arm();
        retry.Arm();

        Assert.Single(_timers);
    }
}
