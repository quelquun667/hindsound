using Hindsound.App;
using Hindsound.Export;

namespace Hindsound.Ui;

/// <summary>Every state of the notification.</summary>
public enum NotificationKind
{
    Saved,
    Partial,
    Silent,
    CopyFailed,
    NotCopied,
    WavFallback,
    Rescued,
    NoDevice,
    Paused,
    CaptureOn,
    DeviceChanged,
    Saving,
    NothingToSave,
    SaveFailed,
    SettingsDamaged,
    SettingsUnreadable,
    SettingsHasError,
    SettingsNotSaved,
    StartupNotChanged,
    StillSaving,
    LastClipGone,
    MicLost,
    MicBack,
    ClosingAfterClip,
    UpdateReady,
    UpdateFailed,
}

/// <summary>What sits on the inner side of the tab, before the text.</summary>
internal enum NotificationVisual
{
    Bars,
    Triangle,
    Pause,
    Dot,
    Headset,
    Pending,
}

/// <summary>One bar of the notification, in design pixels, and whether it holds sound (accent) or none (faint).</summary>
internal readonly record struct BarColumn(int Height, bool Active);

/// <summary>Everything the bitmap needs: no decision is left to the drawing.</summary>
internal sealed record NotificationContent(
    NotificationKind Kind,
    string Title,
    string Body,
    IReadOnlyList<string> Keys,
    uint Stripe,
    NotificationVisual Visual,
    uint VisualColor,
    IReadOnlyList<BarColumn> Bars)
{
    /// <summary>The clip this is about, by the path of its first file: a click chooses it in the Library page.</summary>
    public string? ClipPath { get; init; }

    /// <summary>A click opens the settings, where the problem gets solved, instead of the Library page.</summary>
    public bool ClickOpensSettings { get; init; }

    /// <summary>A click opens the Updates section of the settings.</summary>
    public bool ClickShowsUpdates { get; init; }

    /// <summary>The capture-on row holds three keys, so they are drawn smaller.</summary>
    public bool SmallKeys => Keys.Count > 2;

    /// <summary>Grey is the quiet colour: it has no glow.</summary>
    public bool StripeGlows => Stripe != NotificationPalette.Muted;

    public bool HasBars => Visual == NotificationVisual.Bars;
}

internal static class NotificationPalette
{
    public const uint Border = 0x3A404C, Fill = 0x15181E, Title = 0xF2F4F7, Muted = 0xAAB1BE, Faint = 0x8A92A0,
        CapBorder = 0x6B7382, CapFill = 0x21252D, Amber = 0xFFB547, Hot = 0xFF6A5C, Lime = 0xD4F53C;
}

/// <summary>Chooses the text, the colours and the visual of each state. Pure: tested without any window.</summary>
internal static class NotificationStates
{
    // The folder is named by the separator of the system, as the user sees it in a file manager.
    // Properties, read at each use: a text kept in a field would keep the language of the first use.
    public static string RescuedBody => Tr.Format($"Clip kept in {RescuedFolder}", $"Clip gardé dans {RescuedFolder}");

    public static string RescuedNotCopiedBody => Tr.Format($"Kept in {RescuedFolder}. Not copied", $"Gardé dans {RescuedFolder}. Non copié");

    private static string RescuedFolder => Path.Combine("Hindsound", "Rescued");

    private const char NoBreakSpace = '\u00A0';

    private static readonly string[] PasteKeys = ["Ctrl", "V"];
    private static readonly BarColumn[] NoBars = [];

    /// <summary>The notification once a clip is written and the copy has been tried.</summary>
    /// <param name="result">The written clip.</param>
    /// <param name="requested">The duration asked for.</param>
    /// <param name="copyWanted">The settings ask for a copy to the clipboard.</param>
    /// <param name="copied">The copy succeeded.</param>
    /// <param name="accent">The accent colour.</param>
    /// <param name="features">The system, whose keys paste; the one installed when not given, Windows when none is.</param>
    public static NotificationContent ForClip(ClipResult result, TimeSpan requested, bool copyWanted, bool copied, uint accent, PlatformFeatures? features = null) =>
        WordsForClip(result, requested, copyWanted, copied, accent, PasteKeysOf(features)) with { ClipPath = result.Paths.Count > 0 ? result.PrimaryPath : null };

    private static IReadOnlyList<string> PasteKeysOf(PlatformFeatures? features) =>
        (features ?? PlatformDefaults.Features) is { } system && system.PasteKeys is not ["Ctrl", "V"] ? system.PasteKeys : PasteKeys;

    private static NotificationContent WordsForClip(ClipResult result, TimeSpan requested, bool copyWanted, bool copied, uint accent, IReadOnlyList<string> pasteKeys)
    {
        var copyFailed = copyWanted && !copied;
        if (result.Rescued)
        {
            return Icon(NotificationKind.Rescued, Tr.Of("Output folder not writable", "Dossier de sortie inaccessible"), copyFailed ? RescuedNotCopiedBody : RescuedBody, NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);
        }

        var seconds = WholeSeconds(result.Duration);
        var requestedSeconds = WholeSeconds(requested);
        var partial = seconds < requestedSeconds;
        var length = FormatDuration(seconds);
        var asked = FormatDuration(requestedSeconds);
        var title = partial
            ? Tr.Format($"Clip saved ({length} of {asked})", $"Clip enregistré ({length} sur {asked})")
            : Tr.Format($"Clip saved ({length})", $"Clip enregistré ({length})");
        var bars = BarLayout.Arrange(result.Bars, result.Duration, requested, result.IsSilent);

        if (result.IsSilent)
        {
            return new NotificationContent(NotificationKind.Silent, title, copyFailed ? Tr.Of("It's silent, and not copied", "Il est silencieux, et non copié") : Tr.Of("It's silent. Check Settings > Audio.", "Il est silencieux. Vérifie Réglages > Audio."), [], NotificationPalette.Amber,
                NotificationVisual.Bars, accent, bars);
        }

        if (copyFailed)
        {
            // The sentence needs the wider text area, so the triangle takes the place of the bars.
            return new NotificationContent(NotificationKind.CopyFailed, title, Tr.Of("Couldn't copy. Use tray > Copy last clip.", "Copie impossible. Utilise l'icône > Copier le dernier clip."), [], NotificationPalette.Amber,
                NotificationVisual.Triangle, NotificationPalette.Amber, NoBars);
        }

        if (result.FellBackToWav)
        {
            return new NotificationContent(NotificationKind.WavFallback, title, copied ? Tr.Of("Saved as WAV. Paste", "Enregistré en WAV. Colle") : Tr.Of("Saved as WAV. MP3 failed.", "Enregistré en WAV. Échec du MP3."), copied ? pasteKeys : [],
                NotificationPalette.Amber, NotificationVisual.Bars, accent, bars);
        }

        if (!copyWanted)
        {
            return new NotificationContent(NotificationKind.NotCopied, title, Tr.Of("In your clips folder.", "Dans ton dossier de clips."), [], partial ? NotificationPalette.Amber : accent,
                NotificationVisual.Bars, accent, bars);
        }

        return new NotificationContent(partial ? NotificationKind.Partial : NotificationKind.Saved, title, Tr.Of("Copied. Paste", "Copié. Colle"), pasteKeys,
            partial ? NotificationPalette.Amber : accent, NotificationVisual.Bars, accent, bars);
    }

    /// <summary>The notification tells the user to paste: the clip really is on the clipboard.</summary>
    public static bool SaysPaste(NotificationContent content) =>
        content.Keys.Count > 0 && content.Kind is NotificationKind.Saved or NotificationKind.Partial or NotificationKind.WavFallback;

    public static NotificationContent NoDevice() =>
        Icon(NotificationKind.NoDevice, Tr.Of("No audio device", "Aucun périphérique audio"), Tr.Of("Nothing is being captured", "Rien n'est capturé"), NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);

    /// <param name="keptSeconds">What the memory holds: it stays there during the pause, and can still be saved.</param>
    public static NotificationContent Paused(int keptSeconds = 0) =>
        Icon(NotificationKind.Paused, Tr.Of("Capture paused", "Capture en pause"),
            keptSeconds >= 1
                ? Tr.Format($"{FormatDuration(keptSeconds)} kept. Nothing new is recorded", $"{FormatDuration(keptSeconds)} en mémoire. Rien de nouveau n'est enregistré")
                : Tr.Of("Nothing new is being recorded", "Rien de nouveau n'est enregistré"),
            NotificationPalette.Muted, NotificationVisual.Pause, NotificationPalette.Muted);

    /// <param name="bufferSeconds">How much can be kept.</param>
    /// <param name="saveGesture">The save shortcut, such as <c>Ctrl+Shift+F10</c>.</param>
    /// <param name="saveHotkeyProblem">Said instead of the keys when pressing them would do nothing: the user finds out now, not when the moment is gone.</param>
    /// <param name="updatedTo">The version an update just installed: said in the title, the rest is the usual line.</param>
    /// <param name="keptSeconds">What the memory holds now; null when nothing has said yet.</param>
    /// <param name="features">The system, whose words name the keys; the one installed when not given, Windows when none is.</param>
    public static NotificationContent CaptureOn(int bufferSeconds, string? saveGesture, string? saveHotkeyProblem = null, string? updatedTo = null, int? keptSeconds = null, PlatformFeatures? features = null)
    {
        var title = updatedTo is null ? Tr.Of("Capture on", "Capture active") : Tr.Format($"Updated to {updatedTo}", $"Mis à jour vers {updatedTo}");
        var kept = KeptLine(bufferSeconds, keptSeconds);
        if (saveHotkeyProblem is not null)
        {
            return new NotificationContent(NotificationKind.CaptureOn, title, $"{kept}. {saveHotkeyProblem}", [], NotificationPalette.Amber,
                NotificationVisual.Dot, NotificationPalette.Hot, NoBars)
            {
                ClickOpensSettings = true,
            };
        }

        var keys = string.IsNullOrWhiteSpace(saveGesture) ? [] : (features ?? PlatformDefaults.Features ?? PlatformFeatures.Windows).KeyText(saveGesture).Split('+');
        var body = keys.Length == 0 ? kept : Tr.Format($"{kept}. Save:", $"{kept}. Enregistre{NoBreakSpace}:");
        return new NotificationContent(NotificationKind.CaptureOn, title, body, keys, NotificationPalette.Hot, NotificationVisual.Dot, NotificationPalette.Hot, NoBars);
    }

    private static string KeptLine(int cap, int? kept)
    {
        var length = FormatDuration(cap);
        if (kept is not { } seconds)
        {
            return Tr.Format($"Keeping up to {length}", $"Garde jusqu'à {length}");
        }

        return KeptDuration.StateOf(seconds, cap) switch
        {
            KeptState.Full => Tr.Format($"Keeping the last {length}", $"Garde {length} en mémoire"),
            KeptState.Starting => Tr.Format($"Filling, up to {length}", $"Remplissage, jusqu'à {length}"),
            _ => Tr.Format($"{FormatDuration(seconds)} of {length} kept", $"{FormatDuration(seconds)} sur {length} en mémoire"),
        };
    }

    public static NotificationContent DeviceChanged(string deviceName) =>
        Icon(NotificationKind.DeviceChanged, Tr.Of("Audio device changed", "Périphérique audio changé"), Tr.Format($"Now capturing {deviceName}", $"Capture sur {deviceName}"), NotificationPalette.Muted, NotificationVisual.Headset, NotificationPalette.Muted);

    public static NotificationContent Saving(TimeSpan requested) =>
        Icon(NotificationKind.Saving, Tr.Format($"Saving clip ({FormatDuration(WholeSeconds(requested))})", $"Enregistrement du clip ({FormatDuration(WholeSeconds(requested))})"), Tr.Of("One moment", "Un instant"), NotificationPalette.Muted, NotificationVisual.Pending, NotificationPalette.Muted);

    public static NotificationContent ClosingAfterClip() =>
        Icon(NotificationKind.ClosingAfterClip, Tr.Of("Saving your clip", "Enregistrement de ton clip"), Tr.Of("Hindsound closes when it's done", "Hindsound se ferme quand c'est fini"), NotificationPalette.Muted, NotificationVisual.Pending, NotificationPalette.Muted);

    public static NotificationContent NothingToSave() =>
        Icon(NotificationKind.NothingToSave, Tr.Of("Nothing to save yet", "Rien à enregistrer pour l'instant"), Tr.Of("No sound has been captured so far", "Aucun son n'a encore été capturé"), NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent SaveFailed() =>
        Icon(NotificationKind.SaveFailed, Tr.Of("Couldn't save the clip", "Clip non enregistré"), Tr.Of("The reason is in hindsound.log", "La raison est dans hindsound.log"), NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);

    public static NotificationContent StillSaving() =>
        Icon(NotificationKind.StillSaving, Tr.Of("Still saving your last clip", "Ton dernier clip s'enregistre encore"), Tr.Of("Try again in a moment", "Réessaie dans un instant"), NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent LastClipGone() =>
        Icon(NotificationKind.LastClipGone, Tr.Of("Last clip is gone", "Dernier clip introuvable"), Tr.Of("It was moved or deleted", "Il a été déplacé ou supprimé"), NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent MicLost() =>
        Icon(NotificationKind.MicLost, Tr.Of("Microphone lost", "Micro perdu"), Tr.Of("Clips have no voice until it's back", "Les clips sont sans voix jusqu'à son retour"), NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent MicBack() =>
        Icon(NotificationKind.MicBack, Tr.Of("Microphone back", "Micro de retour"), Tr.Of("Clips have your voice again", "Les clips ont de nouveau ta voix"), NotificationPalette.Muted, NotificationVisual.Headset, NotificationPalette.Muted);

    /// <summary>A new version is downloaded, and installs by itself when the app leaves.</summary>
    public static NotificationContent UpdateReady(string version, uint accent) =>
        Icon(NotificationKind.UpdateReady, Tr.Format($"Update ready ({version})", $"Mise à jour prête ({version})"), Tr.Of("Installs when Hindsound closes. Click for details.", "S'installe à la fermeture de Hindsound. Clique pour les détails."), accent, NotificationVisual.Pending, accent)
            with { ClickShowsUpdates = true };

    /// <summary>A new version is known, and nothing installs until the user says so: "installs when it closes" would be false.</summary>
    public static NotificationContent UpdateAvailable(string version, uint accent) =>
        Icon(NotificationKind.UpdateReady, Tr.Format($"Update available ({version})", $"Mise à jour disponible ({version})"), Tr.Of("Click to see what's new.", "Clique pour voir les nouveautés."), accent, NotificationVisual.Pending, accent)
            with { ClickShowsUpdates = true };

    /// <summary>Setup did not install the new version, and the app came back on the one it had.</summary>
    /// <param name="installed">The version that runs.</param>
    /// <param name="willRetry">Something tries again by itself; otherwise only the button can.</param>
    public static NotificationContent UpdateFailed(string installed, bool willRetry) =>
        Problem(NotificationKind.UpdateFailed, Tr.Of("Update failed", "Mise à jour échouée"),
            willRetry
                ? Tr.Format($"Still on {installed}. It will try again later.", $"Toujours en {installed}. Nouvel essai plus tard.")
                : Tr.Format($"Still on {installed}. Update from Settings > General.", $"Toujours en {installed}. Mets à jour depuis Réglages > Général."))
            with { ClickShowsUpdates = true };

    /// <summary>The start found a broken settings file.</summary>
    /// <param name="copyKept">A copy of it exists; otherwise it stayed in its place.</param>
    public static NotificationContent SettingsDamaged(bool copyKept) =>
        Problem(NotificationKind.SettingsDamaged, Tr.Of("Settings file was damaged", "Fichier de réglages endommagé"),
            copyKept ? Tr.Of("Defaults restored, mic off. Copy kept as .bak", "Valeurs par défaut rétablies, micro coupé. Copie gardée en .bak") : Tr.Of("Defaults restored, mic off", "Valeurs par défaut rétablies, micro coupé"));

    public static NotificationContent SettingsUnreadable() =>
        Problem(NotificationKind.SettingsUnreadable, Tr.Of("Can't read the settings file", "Fichier de réglages illisible"), Tr.Of("Using defaults, mic off, until it can be read", "Valeurs par défaut et micro coupé, jusqu'à sa lecture"));

    public static NotificationContent SettingsHasError() =>
        Problem(NotificationKind.SettingsHasError, Tr.Of("Settings file has an error", "Erreur dans le fichier de réglages"), Tr.Of("Keeping the current settings", "Les réglages actuels sont gardés"));

    public static NotificationContent SettingsNotSaved() =>
        Problem(NotificationKind.SettingsNotSaved, Tr.Of("Couldn't save your settings", "Réglages non enregistrés"), Tr.Of("This change is lost at the next start", "Ce changement sera perdu au prochain démarrage"));

    /// <summary>Named as the toggle is on this system: "Start with Windows", or "Start with the session" on Linux.</summary>
    /// <param name="features">The words of the system; the running one's when left out.</param>
    public static NotificationContent StartupNotChanged(PlatformFeatures? features = null) =>
        Problem(NotificationKind.StartupNotChanged, StartupChangeTitle((features ?? PlatformDefaults.Features ?? PlatformFeatures.Windows).StartupLabel), Tr.Of("It stays as it was", "Rien n'a changé"));

    private static string StartupChangeTitle(string label) =>
        Tr.Format($"Couldn't change {label}", $"Impossible de changer «{NoBreakSpace}{label}{NoBreakSpace}»");

    /// <summary><c>m:ss</c>.</summary>
    public static string FormatDuration(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>29.98 s of audio reads as the 0:30 asked for.</summary>
    public static int WholeSeconds(TimeSpan duration) => (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);

    private static NotificationContent Icon(NotificationKind kind, string title, string body, uint stripe, NotificationVisual visual, uint color) =>
        new(kind, title, body, [], stripe, visual, color, NoBars);

    private static NotificationContent Problem(NotificationKind kind, string title, string body) =>
        Icon(kind, title, body, NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot) with { ClickOpensSettings = true };
}

/// <summary>Turns the 24 values of <see cref="ClipResult.Bars"/> into the 24 columns drawn.</summary>
internal static class BarLayout
{
    private const int HalfHeight = 20;
    private const int FlatHeight = 2;

    /// <summary>
    /// A clip shorter than asked sits on the right, its share of the width, after a flat line. A silent clip is
    /// entirely flat and faint.
    /// </summary>
    public static BarColumn[] Arrange(IReadOnlyList<float> bars, TimeSpan duration, TimeSpan requested, bool silent)
    {
        var columns = new BarColumn[ClipPeaks.BarCount];
        if (silent || bars.Count == 0)
        {
            Array.Fill(columns, new BarColumn(FlatHeight, false));
            return columns;
        }

        var share = requested > TimeSpan.Zero ? duration / requested : 1;
        var active = NotificationStates.WholeSeconds(duration) >= NotificationStates.WholeSeconds(requested)
            ? ClipPeaks.BarCount
            : Math.Clamp((int)Math.Round(ClipPeaks.BarCount * share), 1, ClipPeaks.BarCount);
        var flat = ClipPeaks.BarCount - active;
        for (var i = 0; i < flat; i++)
        {
            columns[i] = new BarColumn(FlatHeight, false);
        }

        for (var i = 0; i < active; i++)
        {
            // Each column keeps the loudest of the values it stands for.
            var from = i * bars.Count / active;
            var to = Math.Max(from + 1, (i + 1) * bars.Count / active);
            var peak = 0f;
            for (var j = from; j < to; j++)
            {
                peak = Math.Max(peak, bars[j]);
            }

            columns[flat + i] = new BarColumn(HeightOf(peak), true);
        }

        return columns;
    }

    /// <summary>An even height, so the bar stays centred: 2 to 40 design pixels.</summary>
    public static int HeightOf(float value)
    {
        var clamped = float.IsNaN(value) ? 0 : Math.Clamp(value, 0f, 1f);
        return Math.Max(FlatHeight, 2 * (int)MathF.Round(clamped * HalfHeight));
    }
}
