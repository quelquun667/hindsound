namespace Hindsound.Library;

/// <summary>
/// The Recycle Bin of Windows, through IFileOperation. Never deletes a file for good: a file the bin can't take stays where it
/// is, and the answer says so.
/// </summary>
internal sealed class WindowsRecycleBin : IRecycleBin
{
    // Until the shell is reached, nothing moves.
    public RecycleOutcome Recycle(IReadOnlyList<string> paths) => RecycleOutcome.CannotRecycle;
}
