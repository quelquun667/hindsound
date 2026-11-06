using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>The microphone after a start that could not read the file, and a write that would destroy a file it never read.</summary>
public sealed class SettingsLoadAndSaveTests : IDisposable
{
    private const string WithMic = """{"audio":{"micDeviceId":"my mic"}}""";

    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-loadsave-").FullName;
    private readonly List<string> _lines = [];
    private readonly SettingsFileState _state = new();
    private int _saves;
    private int _copies;

    private string Path => System.IO.Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private (AppSettings Settings, StartDecision Start) Load(Func<bool?>? readRunKey = null) =>
        _state.Load(new SettingsStore(Path), 2, () => { }, readRunKey ?? (() => null));

    private SettingsNotice Save(bool copyWorks = true, bool saveWorks = true) => _state.TrySave(
        () =>
        {
            _copies++;
            if (copyWorks)
            {
                _state.BrokenKept();
            }

            return copyWorks;
        },
        () =>
        {
            _saves++;
            return saveWorks;
        },
        () => DateTime.UnixEpoch,
        _lines.Add);

    [Fact]
    public void A_locked_file_leaves_the_mic_off_whatever_it_says()
    {
        File.WriteAllText(Path, WithMic);
        using var locked = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var (settings, start) = Load();

        Assert.Equal(AudioSettings.DisabledMic, settings.Audio.MicDeviceId);
        Assert.True(start.MakeSafe);
    }

    [Fact]
    public void A_broken_file_leaves_the_mic_off_and_the_startup_entry_as_it_is()
    {
        File.WriteAllText(Path, "{ broken");

        var (settings, _) = Load(() => true);

        Assert.Equal(AudioSettings.DisabledMic, settings.Audio.MicDeviceId);
        Assert.True(settings.General.StartWithWindows);
    }

    [Fact]
    public void A_file_read_keeps_its_mic_and_never_asks_the_run_key()
    {
        File.WriteAllText(Path, WithMic);

        var (settings, _) = Load(() => throw new InvalidOperationException("no question expected"));

        Assert.Equal("my mic", settings.Audio.MicDeviceId);
    }

    [Fact]
    public void A_file_never_read_is_not_written_over()
    {
        _state.Started(SettingsLoadStatus.Unreadable, null);

        Assert.Equal(SettingsNotice.NotSaved, Save());

        Assert.Equal(0, _saves);
        Assert.Single(_lines);
    }

    [Fact]
    public void A_broken_file_is_copied_before_it_is_written_over()
    {
        _state.Started(SettingsLoadStatus.Corrupt, null);

        Assert.Equal(SettingsNotice.None, Save());

        Assert.Equal(1, _copies);
        Assert.Equal(1, _saves);
    }

    [Fact]
    public void Without_a_copy_a_broken_file_is_not_written_over()
    {
        _state.Started(SettingsLoadStatus.Corrupt, null);

        Assert.Equal(SettingsNotice.NotSaved, Save(copyWorks: false));

        Assert.Equal(0, _saves);
    }

    [Fact]
    public void A_good_file_is_written_at_once_and_a_failure_is_said_once()
    {
        _state.Started(SettingsLoadStatus.Loaded, null);

        Assert.Equal(SettingsNotice.None, Save());
        Assert.Equal(0, _copies);
        Assert.Equal(DateTime.UnixEpoch, _state.Noted);

        Assert.Equal(SettingsNotice.NotSaved, Save(saveWorks: false));
        Assert.Equal(SettingsNotice.None, Save(saveWorks: false));
    }
}
