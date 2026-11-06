using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>A file locked at the start that turns out broken or gone: the defaults still run, mic off, and the user's repair must be seen.</summary>
public sealed class SettingsStillOnDefaultsTests : IDisposable
{
    private static readonly DateTime Date = new(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-defaults-").FullName;
    private readonly Queue<Action> _timers = new();

    private string Path => System.IO.Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static SettingsFileState Locked()
    {
        var state = new SettingsFileState();
        state.Started(SettingsLoadStatus.Unreadable, null);
        return state;
    }

    [Theory]
    [InlineData(SettingsLoadStatus.Corrupt)]
    [InlineData(SettingsLoadStatus.Missing)]
    public void A_file_found_broken_or_gone_leaves_the_app_on_the_defaults_and_its_date_watched(SettingsLoadStatus status)
    {
        var state = Locked();
        Assert.True(state.StillOnDefaults);

        state.Reread(status, Date);

        Assert.False(state.AwaitingRead);
        Assert.True(state.StillOnDefaults);
        Assert.False(state.ShouldRead(Date));
        Assert.True(state.ShouldRead(Date.AddSeconds(1)));
    }

    [Fact]
    public void A_lock_that_stays_changes_nothing()
    {
        var state = Locked();

        state.Reread(SettingsLoadStatus.Unreadable, Date);

        Assert.True(state.AwaitingRead);
        Assert.True(state.StillOnDefaults);
    }

    [Fact]
    public void A_real_read_ends_it()
    {
        var state = Locked();
        state.Reread(SettingsLoadStatus.Corrupt, Date);

        state.Reread(SettingsLoadStatus.Loaded, Date.AddSeconds(1));

        Assert.False(state.StillOnDefaults);
    }

    [Theory]
    [InlineData(SettingsLoadStatus.Loaded)]
    [InlineData(SettingsLoadStatus.Missing)]
    [InlineData(SettingsLoadStatus.Corrupt)]
    public void A_start_that_could_read_the_file_is_not_this(SettingsLoadStatus status)
    {
        var state = new SettingsFileState();

        state.Started(status, "copy");

        Assert.False(state.StillOnDefaults);
    }

    [Fact]
    public void Settings_the_user_saved_end_it_and_a_failed_save_does_not()
    {
        var state = Locked();
        state.Reread(SettingsLoadStatus.Missing, Date);

        state.WriteEnded(saved: false, Date);
        Assert.True(state.StillOnDefaults);

        state.WriteEnded(saved: true, Date);
        Assert.False(state.StillOnDefaults);
    }

    [Fact]
    public void A_file_locked_then_broken_then_repaired_is_read_without_a_press()
    {
        File.WriteAllText(Path, """{"bufferSeconds":60}""");
        var store = new SettingsStore(Path);
        var state = new SettingsFileState();
        var reads = 0;
        AppSettings? applied = null;

        // What the app does at each look.
        void Look()
        {
            var written = File.GetLastWriteTimeUtc(Path);
            if (!state.ShouldRead(written))
            {
                return;
            }

            reads++;
            var read = store.Read();
            if (state.Reread(read.Status, written).Apply)
            {
                applied = read.Settings;
            }
        }

        var locked = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        state.Started(store.Read().Status, null);
        SettingsRetry.Watching(state, () => 0, (_, step) => _timers.Enqueue(step), Look).Arm();
        _timers.Dequeue()();
        Assert.Equal(1, reads);

        locked.Dispose();
        File.WriteAllText(Path, "{ broken");
        _timers.Dequeue()();
        Assert.Equal(2, reads);
        Assert.Null(applied);
        Assert.False(state.AwaitingRead);
        Assert.Single(_timers);

        _timers.Dequeue()();
        Assert.Equal(2, reads);
        Assert.Single(_timers);

        File.WriteAllText(Path, """{"bufferSeconds":90}""");
        File.SetLastWriteTimeUtc(Path, DateTime.UtcNow.AddMinutes(1));
        _timers.Dequeue()();

        Assert.Equal(90, applied?.BufferSeconds);
        Assert.False(state.StillOnDefaults);
        Assert.Empty(_timers);
    }
}
