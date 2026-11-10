using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A key held down on Play or Stop: Windows sends it again with the repeat bit set, and the button must act once.</summary>
public sealed class ScreenLibraryPreviewKeyRepeatTests : IDisposable
{
    private readonly ScreenRig _rig = new();
    private readonly PlayingLibrary _library = new();

    public ScreenLibraryPreviewKeyRepeatTests()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new NoNow());
        Screen.SetLibrary(_library);
        Screen.ShowSection(Section.Library);
        Screen.SelectClip(_library.Lines!.Lines[0].Main.Path);
        _rig.FocusOn(SettingsScreen.ClipPlayId);
    }

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private ClipGroup First => _library.Lines!.Lines[0];

    private void Repeat(uint key, int times = 4)
    {
        for (var i = 0; i < times; i++)
        {
            Screen.Key(key, 0, repeat: true);
        }
    }

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Space)]
    public void Holding_a_key_on_Play_plays_once(uint vk)
    {
        Screen.Key(vk, 0);
        Repeat(vk);
        Screen.KeyUp(vk, 0);

        Assert.Single(_library.Played);
        Assert.Equal(0, _library.Stops);
    }

    [Fact]
    public void Holding_enter_on_Play_does_not_stop_the_sound_once_Stop_takes_its_place()
    {
        Screen.Key(Vk.Enter, 0);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, First));
        Repeat(Vk.Enter);
        Screen.KeyUp(Vk.Enter, 0);

        Assert.Single(_library.Played);
        Assert.Equal(0, _library.Stops);
        Assert.Equal(SettingsScreen.ClipPlayId, Screen.FocusId);
    }

    [Fact]
    public void Holding_enter_on_Stop_stops_once_and_does_not_play_again_once_Play_is_back()
    {
        Screen.Key(Vk.Enter, 0);
        Screen.KeyUp(Vk.Enter, 0);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, First));

        Screen.Key(Vk.Enter, 0);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Stopped, null));
        Repeat(Vk.Enter);
        Screen.KeyUp(Vk.Enter, 0);

        Assert.Single(_library.Played);
        Assert.Equal(1, _library.Stops);
    }

    [Fact]
    public void A_second_real_press_on_Stop_after_Play_still_counts()
    {
        Screen.Key(Vk.Enter, 0);
        Screen.KeyUp(Vk.Enter, 0);
        Screen.PreviewChanged(new PreviewChange(PreviewState.Playing, First));
        Screen.Key(Vk.Enter, 0);
        Screen.KeyUp(Vk.Enter, 0);

        Assert.Single(_library.Played);
        Assert.Equal(1, _library.Stops);
    }

    private sealed class NoNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    /// <summary>Three clips, which says what it was asked to play or stop.</summary>
    private sealed class PlayingLibrary : ILibraryPanel
    {
        public ClipLines? Lines { get; } = new(Enumerable.Range(0, 3).Select(i => FakeLibrary.Clip($"dogs{i:0000}.mp3")).ToList(), 0);

        public List<ClipGroup> Played { get; } = [];

        public int Stops { get; private set; }

        public LibraryData? Index { get; } = new();

        public LibraryStatus IndexStatus => LibraryStatus.Loaded;

        public ClipLabels LabelsOf(ClipGroup line) => new(false, []);

        public TimeSpan? LengthOf(ClipGroup line) => TimeSpan.FromSeconds(30);

        public bool IsRescued(ClipGroup line) => false;

        public void Copy(ClipGroup line)
        {
        }

        public void ShowInFolder(ClipGroup line)
        {
        }

        public void ReadAgain()
        {
        }

        public void SetFavorite(ClipGroup line, bool favorite)
        {
        }

        public void Play(ClipGroup line) => Played.Add(line);

        public void StopPlaying() => Stops++;
    }
}
