using Hindsound.Library;

namespace Hindsound.Linux.App;

/// <summary>No listening in the Linux app: the Library page that plays clips is part of the window, which this version has none of.</summary>
internal sealed class NoPreviewPlayer : IPreviewPlayer
{
    // Any code but zero is an error to ClipPreview.
    private const int NotPlayable = -1;

    public int Open(string path, string track) => NotPlayable;

    public int Play(string track) => NotPlayable;

    public int Stop(string track) => NotPlayable;

    public int Close(string track) => 0;

    public int StateOf(string track, out PreviewTrackState state)
    {
        state = default;
        return NotPlayable;
    }

    public void Wait(WaitHandle wake, int milliseconds) => wake.WaitOne(milliseconds);
}
