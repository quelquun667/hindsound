using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>
/// The pictures of the editor for <c>--dump-settings</c>. Each scene takes a screen already on the Library page, with its clips, and
/// opens the editor over it on a made-up clip: nothing is read, played or written.
/// </summary>
internal static class DumpScenesEditor
{
    private const string Folder = @"C:\Clips\", Name = "Hindsound_Discord_2026-11-08_20-47-51";

    /// <summary>The scenes, by the name of their file.</summary>
    public static readonly (string Name, Action<SettingsScreen> Set)[] Scenes =
    [
        ("edit-clip", s => Open(s, EditorMode.Normal, 1, 3_200, 8_000, playheadMs: 5_600)),
        ("edit-deux-pistes", s =>
        {
            Open(s, EditorMode.Normal, 2, 5_000, 9_600, playheadMs: 6_400);
            Volume(s, 1, 6);
        }),
        ("edit-volume", s =>
        {
            Open(s, EditorMode.Normal, 1, 3_200, 8_000);
            Volume(s, 0, 6);
        }),
        ("edit-enregistrer", s =>
        {
            Open(s, EditorMode.Normal, 1, 3_200, 8_000);
            Volume(s, 0, 6);
            s.TabTo(SettingsScreen.EditorSaveId);
            s.Key(Vk.Enter, 0);
        }),
        ("edit-soundboard", s => Open(s, EditorMode.Soundboard, 1, 3_200, 8_000)),
        ("edit-soundboard-long", s => Open(s, EditorMode.Normal, 1, 3_200, 10_400)),
        ("edit-illisible", s =>
        {
            s.SetEditor(new SampleEditor());
            s.OpenEditor(Clip(1), EditorMode.Normal);
            s.EditorLoaded(new EditorLoad(EditorLoadOutcome.Unreadable, TimeSpan.Zero, [], ExportFormat.Mp3));
        }),
    ];

    private static void Open(SettingsScreen s, EditorMode mode, int files, long startMs, long endMs, long? playheadMs = null)
    {
        s.SetEditor(new SampleEditor());
        s.OpenEditor(Clip(files), mode);
        s.EditorLoaded(new EditorLoad(EditorLoadOutcome.Loaded, TimeSpan.FromSeconds(30), [.. Enumerable.Range(0, files).Select(Peaks)], ExportFormat.Mp3));
        s.EditorSelection!.Move(EditorEnd.End, endMs);
        s.EditorSelection!.Move(EditorEnd.Start, startMs);
        if (playheadMs is { } head)
        {
            s.EditorClock = () => TimeSpan.Zero;
            s.TabTo(SettingsScreen.EditorPlayId);
            s.Key(Vk.Enter, 0);
            s.EditorPreviewChanged(PreviewState.Playing);
            s.EditorClock = () => TimeSpan.FromMilliseconds(head - startMs);
        }
    }

    private static void Volume(SettingsScreen s, int file, int steps)
    {
        s.TabTo(SettingsScreen.EditorVolumePrefix + file);
        for (var i = 0; i < steps; i++)
        {
            s.Key(Vk.Right, 0);
        }
    }

    private static ClipGroup Clip(int files) => files > 1
        ? new([new ClipFile(Folder + Name + "_system.wav", 5_760_000, default), new ClipFile(Folder + Name + "_mic.wav", 5_760_000, default)])
        : new([new ClipFile(Folder + Name + ".mp3", 720_000, default)]);

    /// <summary>Peaks, one per 50 ms: words and pauses, the same at each run.</summary>
    private static IReadOnlyList<float> Peaks(int file)
    {
        var peaks = new float[600];
        var seed = 9 + 12 * file;
        for (var i = 0; i < peaks.Length; i++)
        {
            var t = i * 0.05;
            var phrase = Math.Sin(t * (0.9 + 0.3 * file) + seed);
            var word = Math.Abs(Math.Sin(t * 5.7 + seed * 0.3));
            peaks[i] = phrase < 0.1 ? 0 : (float)(0.12 + 0.35 * phrase * word);
        }

        return peaks;
    }

    /// <summary>An editor that does nothing but give a size: the scenes of the Library use it too.</summary>
    internal sealed class SampleEditor : IEditorPanel
    {
        private const long BytesPerSecond = 24_000;

        public void Load(ClipGroup clip)
        {
        }

        public long EstimateBytes(EditRequest request) => (long)(request.Kept.TotalSeconds * BytesPerSecond);

        public void Play(EditRequest request)
        {
        }

        public void StopPlaying()
        {
        }

        public void Save(EditRequest request, EditSave save)
        {
        }

        public void HandToDiscord(string path)
        {
        }

        public void Close()
        {
        }
    }
}
