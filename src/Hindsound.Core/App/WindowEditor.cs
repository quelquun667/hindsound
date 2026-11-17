using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>
/// What the editor page of one window gets from the disk: the clip decoded, the selection played, the edit written. It lives
/// and dies with that window. The work runs off the loop, and every answer comes back through it.
/// </summary>
/// <param name="screen">The screen of the window, the only one this ever answers to.</param>
/// <param name="post">Runs an action on the thread of the loop.</param>
/// <param name="decoder">Reads the files of a clip back.</param>
/// <param name="recycleBin">Where a replaced original goes. Never a deletion for good.</param>
/// <param name="index">The labels that follow a replaced original, and the collections a copy joins.</param>
/// <param name="export">The export settings as of now: the bitrate of an mp3, the ceiling of the peaks.</param>
/// <param name="player">What plays the selection: MCI, or a fake. Without it, Play does nothing.</param>
/// <param name="mayPlay">Whether this file may be played at all; off screen, only pure silence. Without it, any may.</param>
/// <param name="stopLibraryPreview">Stops what the list of the Library plays, before the selection plays.</param>
/// <param name="opener">Opens <c>discord://</c>. Null off screen: nothing is opened.</param>
/// <param name="shell">Shows a file in its folder.</param>
/// <param name="tempFolder">The temporary folder of the system, where the WAV of the selection is written, under a name of its own.</param>
/// <param name="journal">Told of failures, by type and code only: a file name can hold a first name.</param>
internal sealed class WindowEditor(
    SettingsScreen screen,
    Action<Action> post,
    IClipDecoder decoder,
    IRecycleBin recycleBin,
    IClipIndex index,
    Func<ExportSettings> export,
    Func<IPreviewPlayer>? player,
    Func<ClipGroup, bool>? mayPlay,
    Action stopLibraryPreview,
    IFileOpener? opener,
    IFolderShell shell,
    string tempFolder,
    Action<string> journal) : IEditorPanel
{
    private readonly SettingsScreen _screen = screen;
    private readonly Action<Action> _post = post;
    private readonly IClipDecoder _decoder = decoder;
    private readonly IRecycleBin _recycleBin = recycleBin;
    private readonly IClipIndex _index = index;
    private readonly Func<ExportSettings> _export = export;
    private readonly Func<IPreviewPlayer>? _player = player;
    private readonly Func<ClipGroup, bool>? _mayPlay = mayPlay;
    private readonly Action _stopLibraryPreview = stopLibraryPreview;
    private readonly IFileOpener? _opener = opener;
    private readonly IFolderShell _shell = shell;
    private readonly string _tempFolder = tempFolder;
    private readonly Action<string> _journal = journal;

    public void Load(ClipGroup clip) =>
        _post(() => _screen.EditorLoaded(new EditorLoad(EditorLoadOutcome.Unreadable, TimeSpan.Zero, [], Export.ExportFormat.Mp3)));

    public long EstimateBytes(EditRequest request) => 0;

    public void Play(EditRequest request)
    {
    }

    public void StopPlaying()
    {
    }

    public void Save(EditRequest request, EditSave save) =>
        _post(() => _screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Failed, [])));

    public void HandToDiscord(string path)
    {
    }

    public void Close()
    {
    }
}
