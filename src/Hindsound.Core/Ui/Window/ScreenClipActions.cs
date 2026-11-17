using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>The actions on one clip of the Library: its menu, renaming it, and sending it to the Recycle Bin.</summary>
internal sealed partial class SettingsScreen
{
    /// <summary>The right button went down here. On a clip of the list, its menu opens; anywhere else, nothing happens.</summary>
    public void RightClick(float x, float y)
    {
    }

    /// <summary>The answer to <see cref="ILibraryPanel.Rename"/>, on the loop. A new reading follows.</summary>
    /// <param name="line">The clip as it was asked for.</param>
    /// <param name="outcome">What the renaming did.</param>
    public void ClipRenameAnswered(ClipGroup line, ClipRenameOutcome outcome)
    {
    }

    /// <summary>The answer to <see cref="ILibraryPanel.Delete"/>, on the loop. A new reading follows.</summary>
    public void ClipDeleteAnswered(ClipGroup line, ClipDeleteOutcome outcome)
    {
    }

    /// <summary>A key on the Library page, before the widgets have it: F2 and Delete. Null when this does not want it.</summary>
    private bool? LibraryKey(uint vk, uint modifiers) => null;
}
