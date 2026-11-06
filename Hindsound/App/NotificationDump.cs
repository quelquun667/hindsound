using Hindsound.Export;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary><c>--dump-notifications</c>: every state as a BMP file, to look at the drawing with nothing shown on screen.</summary>
internal static class NotificationDump
{
    public static int Write(string folder, Journal journal)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var accent = NotificationPalette.Lime;
            foreach (var (name, content) in Samples(accent))
            {
                foreach (var scale in new[] { 1.0, 1.5 })
                {
                    using var bitmap = new NotificationBitmap(content, scale, accent, mirrored: false);
                    bitmap.DumpBmp(Path.Combine(folder, $"{name}@{(int)(scale * 100)}.bmp"));
                }
            }

            using var left = new NotificationBitmap(Samples(accent)[0].Content, 1.5, accent, mirrored: true);
            left.DumpBmp(Path.Combine(folder, "saved-left@150.bmp"));
            return 0;
        }
        catch (Exception ex)
        {
            journal.Write("dump failed: " + ex);
            return 3;
        }
    }

    private static List<(string Name, NotificationContent Content)> Samples(uint accent)
    {
        float[] bars =
        [
            0.25f, 0.25f, 0.3f, 0.39f, 0.42f, 0.4f, 0.15f, 0.12f, 0.38f, 0.44f, 0.51f, 0.26f,
            0.14f, 0.23f, 0.35f, 0.44f, 0.36f, 0.35f, 0.78f, 0.84f, 0.88f, 0.59f, 0.8f, 0.8f,
        ];
        var thirty = TimeSpan.FromSeconds(30);
        ClipResult Clip(double seconds, bool silent = false, bool rescued = false, bool wav = false) =>
            new(["clip.mp3"], TimeSpan.FromSeconds(seconds), 1, silent, bars, rescued, wav);

        return
        [
            ("saved", NotificationStates.ForClip(Clip(30), thirty, true, true, accent)),
            ("partial", NotificationStates.ForClip(Clip(12), thirty, true, true, accent)),
            ("silent", NotificationStates.ForClip(Clip(30, silent: true), thirty, true, true, accent)),
            ("copyfailed", NotificationStates.ForClip(Clip(30), thirty, true, false, accent)),
            ("notcopied", NotificationStates.ForClip(Clip(30), thirty, false, false, accent)),
            ("wav", NotificationStates.ForClip(Clip(30, wav: true), thirty, true, true, accent)),
            ("rescued", NotificationStates.ForClip(Clip(30, rescued: true), thirty, true, true, accent)),
            ("rescued-notcopied", NotificationStates.ForClip(Clip(30, rescued: true), thirty, true, false, accent)),
            ("silent-notcopied", NotificationStates.ForClip(Clip(30, silent: true), thirty, true, false, accent)),
            ("nodevice", NotificationStates.NoDevice()),
            ("paused", NotificationStates.Paused()),
            ("on", NotificationStates.CaptureOn(300, "Ctrl+Shift+F10")),
            ("on-long", NotificationStates.CaptureOn(300, "Ctrl+Alt+Shift+F24")),
            ("device", NotificationStates.DeviceChanged("Headset Earphone")),
            ("device-long", NotificationStates.DeviceChanged("Speakers (Realtek High Definition Audio with a very long name)")),
            ("saving", NotificationStates.Saving(thirty)),
            ("nothing", NotificationStates.NothingToSave()),
            ("failed", NotificationStates.SaveFailed()),
            ("settings-damaged", NotificationStates.SettingsDamaged(copyKept: true)),
            ("settings-damaged-nocopy", NotificationStates.SettingsDamaged(copyKept: false)),
            ("settings-unreadable", NotificationStates.SettingsUnreadable()),
            ("settings-error", NotificationStates.SettingsHasError()),
            ("settings-notsaved", NotificationStates.SettingsNotSaved()),
            ("startup-notchanged", NotificationStates.StartupNotChanged()),
            ("stillsaving", NotificationStates.StillSaving()),
            ("lastclip-gone", NotificationStates.LastClipGone()),
            ("miclost", NotificationStates.MicLost()),
            ("micback", NotificationStates.MicBack()),
        ];
    }
}
