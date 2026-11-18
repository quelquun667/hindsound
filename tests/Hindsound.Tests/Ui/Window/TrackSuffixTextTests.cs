using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A name that ends as a track, for a clip in one file: the box of the name and the dialog of Save both say why.</summary>
public sealed class TrackSuffixTextTests
{
    private static ClipGroup OneFile => new([new ClipFile(@"C:\Clips\dogs.mp3", 720_000, default)]);

    [Fact]
    public void The_name_box_says_a_name_can_t_end_as_a_track()
    {
        Assert.Equal(ClipNames.TrackSuffixText, NameBoxState.ClipRefusalText(ClipRenameOutcome.TrackSuffix));
    }

    [Fact]
    public void The_dialog_of_Save_says_a_name_can_t_end_as_a_track()
    {
        Assert.Equal(ClipNames.TrackSuffixText, EditorSaveDialogState.MessageOf(ClipRenameOutcome.TrackSuffix));
        Assert.Equal(ClipNames.TrackSuffixText, new EditorSaveDialogState(OneFile).Refusal("dogs_mic"));
    }
}
