using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The choice follows a file whose path changes only in its case. That holds where the file system ignores case
/// (Windows, macOS) and not elsewhere, so this stays out of the project that also runs on Linux.
/// </summary>
public class RowListStateCaseTests
{
    private static string[] Paths(int count) => Enumerable.Range(0, count).Select(i => $@"C:\Clips\clip{i:0000}.mp3").ToArray();

    private static void Read(RowListState state, IReadOnlyList<string> paths) =>
        state.SetRows(paths.Count, i => paths[i]);

    [Fact]
    public void A_file_renamed_only_in_its_case_stays_chosen()
    {
        var state = new RowListState { ViewRows = 6 };
        Read(state, Paths(10));
        state.Select(5);

        Read(state, Paths(10).Select(path => path.ToUpperInvariant()).ToArray());

        Assert.Equal(5, state.Selected);
        Assert.Equal(@"C:\CLIPS\CLIP0005.MP3", state.SelectedPath);
    }
}
