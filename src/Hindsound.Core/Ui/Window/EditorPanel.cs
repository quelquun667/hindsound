using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>
/// The app's side of the editor page: it reads the clip, plays the selection and writes the edit, off the loop. Every answer
/// comes back on the loop, through the screen. The real one is <see cref="App.WindowEditor"/>.
/// </summary>
internal interface IEditorPanel
{
    /// <summary>Opens the files of the clip and reads them once, for the wave. The answer is <see cref="SettingsScreen.EditorLoaded"/>.</summary>
    void Load(ClipGroup clip);

    /// <summary>The size the edit would have on disk, from its length and the format. No disk, no decoding: asked at paint time.</summary>
    long EstimateBytes(EditRequest request);

    /// <summary>
    /// Plays the selection with its volume, from a temporary WAV, in place of whatever plays. Each state comes back through
    /// <see cref="SettingsScreen.EditorPreviewChanged"/>.
    /// </summary>
    void Play(EditRequest request);

    /// <summary>Stops what plays. The state comes back through <see cref="SettingsScreen.EditorPreviewChanged"/>.</summary>
    void StopPlaying();

    /// <summary>Writes the edit. The answer is <see cref="SettingsScreen.EditorSaved"/>, then a new reading of the Library.</summary>
    void Save(EditRequest request, EditSave save);

    /// <summary>After a soundboard copy: opens Discord if it is installed (nothing otherwise), and shows the file in its folder.</summary>
    void HandToDiscord(string path);

    /// <summary>The page is left: stops what plays, deletes the temporary files and lets go of the clip. Never throws.</summary>
    void Close();
}
