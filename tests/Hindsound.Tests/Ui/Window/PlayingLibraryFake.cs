using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Six clips in one collection out of three, which says what it was asked to play and how many times it was asked to stop.</summary>
internal sealed class PlayingLibraryFake : ILibraryPanel
{
    public PlayingLibraryFake()
    {
        var rows = Enumerable.Range(0, 6).Select(i => FakeLibrary.Clip($"clip{i:0000}.mp3")).ToList();
        Lines = new ClipLines(rows, 0);
    }

    public ClipLines? Lines { get; set; }

    public List<ClipGroup> Played { get; } = [];

    public int Stops { get; private set; }

    public LibraryData? Index { get; } = new()
    {
        Collections = [new() { Id = "c1", Name = "pets" }, new() { Id = "c2", Name = "Sam" }],
    };

    public LibraryStatus IndexStatus => LibraryStatus.Loaded;

    public ClipLabels LabelsOf(ClipGroup line) => new(false, ["c1"]);

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
