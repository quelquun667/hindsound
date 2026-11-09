using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A list of clips whose index file is in the state the test names, which says every star it was asked for.</summary>
internal sealed class StatusLibrary : ILibraryPanel
{
    public ClipLines? Lines { get; set; } = new ClipLines(
        Enumerable.Range(0, 5).Select(i => new ClipGroup([new ClipFile($@"C:\Clips\clip{i:0000}.mp3", 720_000, default)])).ToList(), 0);

    public LibraryData? Index { get; set; } = new LibraryData();

    public LibraryStatus IndexStatus { get; set; } = LibraryStatus.Loaded;

    public bool FirstIsStarred { get; set; }

    public List<(string Path, bool On)> StarsAsked { get; } = [];

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

    public ClipLabels LabelsOf(ClipGroup line) => new(FirstIsStarred && line.Main.Path.EndsWith("clip0000.mp3"), []);

    public void SetFavorite(ClipGroup line, bool favorite) => StarsAsked.Add((line.Main.Path, favorite));
}

/// <summary>A star is for a file that can be written: on a file in trouble, a press would be refused and nothing would say so.</summary>
public sealed class ScreenStarsReadOnlyTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private const float ListTop = 118, StarX = 710;
    private const uint StarOutline = 0x8E95A3;

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static float RowY(int slot) => ListTop + slot * RowListState.RowHeight + 20;

    private StatusLibrary Open(LibraryStatus status, bool firstIsStarred = false)
    {
        var library = new StatusLibrary { IndexStatus = status, FirstIsStarred = firstIsStarred };
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(library);
        Screen.ShowSection(Section.Library);
        return library;
    }

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    private int StarStrokes()
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Canvas.Strokes.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Strokes.Count(s => s.Rgb == StarOutline);
    }

    [Theory]
    [InlineData(LibraryStatus.Corrupt)]
    [InlineData(LibraryStatus.Unreadable)]
    [InlineData(LibraryStatus.TooNew)]
    public void A_press_on_a_star_of_a_file_that_cannot_be_written_asks_for_nothing(LibraryStatus status)
    {
        var library = Open(status);

        Click(StarX, RowY(2));
        Screen.SelectClip(@"C:\Clips\clip0002.mp3");
        Screen.TabTo(SettingsScreen.ClipListId);
        _rig.Press(RowList.StarKey);

        Assert.Empty(library.StarsAsked);
    }

    [Theory]
    [InlineData(LibraryStatus.Corrupt)]
    [InlineData(LibraryStatus.Unreadable)]
    public void A_file_with_nothing_to_read_draws_no_star_that_would_be_empty_for_every_clip(LibraryStatus status)
    {
        Open(LibraryStatus.Loaded);
        Assert.True(StarStrokes() > 0, "a star of a sound file is drawn");

        Screen.SetLibrary(new StatusLibrary { IndexStatus = status });

        Assert.Equal(0, StarStrokes());
    }

    [Theory]
    [InlineData(LibraryStatus.Missing)]
    [InlineData(LibraryStatus.Loaded)]
    public void A_file_that_can_be_written_still_takes_the_press(LibraryStatus status)
    {
        var library = Open(status);

        Click(StarX, RowY(2));

        Assert.Equal([(@"C:\Clips\clip0002.mp3", true)], library.StarsAsked);
    }

    [Fact]
    public void A_newer_file_still_shows_the_star_it_holds()
    {
        Open(LibraryStatus.Loaded);
        var emptyWhenAllOpen = StarStrokes();

        Open(LibraryStatus.TooNew, firstIsStarred: true);

        Assert.True(StarStrokes() > 0);
        Assert.True(StarStrokes() < emptyWhenAllOpen, "the starred row is filled, so it has no outline");
    }
}
