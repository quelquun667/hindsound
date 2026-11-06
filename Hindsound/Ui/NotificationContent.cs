using Hindsound.Export;

namespace Hindsound.Ui;

/// <summary>Every state of the notification (PLAN section 3 quater).</summary>
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
    /// <summary>A click opens the settings, where the problem gets solved, instead of the folder of the clips.</summary>
    public bool ClickOpensSettings { get; init; }

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
    public const string RescuedBody = @"Clip kept in Hindsound\Rescued";
    public const string RescuedNotCopiedBody = @"Kept in Hindsound\Rescued. Not copied";

    private static readonly string[] PasteKeys = ["Ctrl", "V"];
    private static readonly BarColumn[] NoBars = [];

    /// <summary>The notification once a clip is written and the copy has been tried.</summary>
    /// <param name="result">The written clip.</param>
    /// <param name="requested">The duration asked for.</param>
    /// <param name="copyWanted">The settings ask for a copy to the clipboard.</param>
    /// <param name="copied">The copy succeeded.</param>
    /// <param name="accent">The accent colour.</param>
    public static NotificationContent ForClip(ClipResult result, TimeSpan requested, bool copyWanted, bool copied, uint accent)
    {
        var copyFailed = copyWanted && !copied;
        if (result.Rescued)
        {
            return Icon(NotificationKind.Rescued, "Output folder not writable", copyFailed ? RescuedNotCopiedBody : RescuedBody, NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);
        }

        var seconds = WholeSeconds(result.Duration);
        var requestedSeconds = WholeSeconds(requested);
        var partial = seconds < requestedSeconds;
        var title = partial
            ? $"Clip saved ({FormatDuration(seconds)} of {FormatDuration(requestedSeconds)})"
            : $"Clip saved ({FormatDuration(seconds)})";
        var bars = BarLayout.Arrange(result.Bars, result.Duration, requested, result.IsSilent);

        if (result.IsSilent)
        {
            return new NotificationContent(NotificationKind.Silent, title, copyFailed ? "It's silent, and not copied" : "It's silent. Check Settings > Audio.", [], NotificationPalette.Amber,
                NotificationVisual.Bars, accent, bars);
        }

        if (copyFailed)
        {
            // The sentence needs the wider text area, so the triangle takes the place of the bars.
            return new NotificationContent(NotificationKind.CopyFailed, title, "Couldn't copy. Use tray > Copy last clip.", [], NotificationPalette.Amber,
                NotificationVisual.Triangle, NotificationPalette.Amber, NoBars);
        }

        if (result.FellBackToWav)
        {
            return new NotificationContent(NotificationKind.WavFallback, title, copied ? "Saved as WAV. Paste" : "Saved as WAV. MP3 failed.", copied ? PasteKeys : [],
                NotificationPalette.Amber, NotificationVisual.Bars, accent, bars);
        }

        if (!copyWanted)
        {
            return new NotificationContent(NotificationKind.NotCopied, title, "In your clips folder.", [], partial ? NotificationPalette.Amber : accent,
                NotificationVisual.Bars, accent, bars);
        }

        return new NotificationContent(partial ? NotificationKind.Partial : NotificationKind.Saved, title, "Copied. Paste", PasteKeys,
            partial ? NotificationPalette.Amber : accent, NotificationVisual.Bars, accent, bars);
    }

    public static NotificationContent NoDevice() =>
        Icon(NotificationKind.NoDevice, "No audio device", "Nothing is being captured", NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);

    public static NotificationContent Paused() =>
        Icon(NotificationKind.Paused, "Capture paused", "Nothing new is being recorded", NotificationPalette.Muted, NotificationVisual.Pause, NotificationPalette.Muted);

    /// <param name="bufferSeconds">How much is kept.</param>
    /// <param name="saveGesture">The save shortcut, such as <c>Ctrl+Shift+F10</c>.</param>
    /// <param name="saveHotkeyProblem">Said instead of the keys when pressing them would do nothing: the user finds out now, not when the moment is gone.</param>
    public static NotificationContent CaptureOn(int bufferSeconds, string? saveGesture, string? saveHotkeyProblem = null)
    {
        if (saveHotkeyProblem is not null)
        {
            return new NotificationContent(NotificationKind.CaptureOn, "Capture on", $"Keeping the last {FormatDuration(bufferSeconds)}. {saveHotkeyProblem}", [], NotificationPalette.Amber,
                NotificationVisual.Dot, NotificationPalette.Hot, NoBars)
            {
                ClickOpensSettings = true,
            };
        }

        var keys = string.IsNullOrWhiteSpace(saveGesture) ? [] : saveGesture.Split('+');
        var body = keys.Length == 0 ? $"Keeping the last {FormatDuration(bufferSeconds)}" : $"Keeping the last {FormatDuration(bufferSeconds)}. Save:";
        return new NotificationContent(NotificationKind.CaptureOn, "Capture on", body, keys, NotificationPalette.Hot, NotificationVisual.Dot, NotificationPalette.Hot, NoBars);
    }

    public static NotificationContent DeviceChanged(string deviceName) =>
        Icon(NotificationKind.DeviceChanged, "Audio device changed", "Now capturing " + deviceName, NotificationPalette.Muted, NotificationVisual.Headset, NotificationPalette.Muted);

    public static NotificationContent Saving(TimeSpan requested) =>
        Icon(NotificationKind.Saving, $"Saving clip ({FormatDuration(WholeSeconds(requested))})", "One moment", NotificationPalette.Muted, NotificationVisual.Pending, NotificationPalette.Muted);

    public static NotificationContent ClosingAfterClip() =>
        Icon(NotificationKind.ClosingAfterClip, "Saving your clip", "Hindsound closes when it's done", NotificationPalette.Muted, NotificationVisual.Pending, NotificationPalette.Muted);

    public static NotificationContent NothingToSave() =>
        Icon(NotificationKind.NothingToSave, "Nothing to save yet", "No sound has been captured so far", NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent SaveFailed() =>
        Icon(NotificationKind.SaveFailed, "Couldn't save the clip", "The reason is in hindsound.log", NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);

    public static NotificationContent StillSaving() =>
        Icon(NotificationKind.StillSaving, "Still saving your last clip", "Try again in a moment", NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent LastClipGone() =>
        Icon(NotificationKind.LastClipGone, "Last clip is gone", "It was moved or deleted", NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent MicLost() =>
        Icon(NotificationKind.MicLost, "Microphone lost", "Clips have no voice until it's back", NotificationPalette.Amber, NotificationVisual.Triangle, NotificationPalette.Amber);

    public static NotificationContent MicBack() =>
        Icon(NotificationKind.MicBack, "Microphone back", "Clips have your voice again", NotificationPalette.Muted, NotificationVisual.Headset, NotificationPalette.Muted);

    /// <summary>The start found a broken settings file.</summary>
    /// <param name="copyKept">A copy of it exists; otherwise it stayed in its place.</param>
    public static NotificationContent SettingsDamaged(bool copyKept) =>
        Problem(NotificationKind.SettingsDamaged, "Settings file was damaged", copyKept ? "Defaults restored, mic off. Copy kept as .bak" : "Defaults restored, mic off");

    public static NotificationContent SettingsUnreadable() =>
        Problem(NotificationKind.SettingsUnreadable, "Can't read the settings file", "Using defaults, mic off, until it can be read");

    public static NotificationContent SettingsHasError() =>
        Problem(NotificationKind.SettingsHasError, "Settings file has an error", "Keeping the current settings");

    public static NotificationContent SettingsNotSaved() =>
        Problem(NotificationKind.SettingsNotSaved, "Couldn't save your settings", "This change is lost at the next start");

    public static NotificationContent StartupNotChanged() =>
        Problem(NotificationKind.StartupNotChanged, "Couldn't change Start with Windows", "It stays as it was");

    /// <summary><c>m:ss</c>.</summary>
    public static string FormatDuration(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>29.98 s of audio reads as the 0:30 asked for.</summary>
    public static int WholeSeconds(TimeSpan duration) => (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);

    private static NotificationContent Icon(NotificationKind kind, string title, string body, uint stripe, NotificationVisual visual, uint color) =>
        new(kind, title, body, [], stripe, visual, color, NoBars);

    private static NotificationContent Problem(NotificationKind kind, string title, string body) =>
        Icon(kind, title, body, NotificationPalette.Hot, NotificationVisual.Triangle, NotificationPalette.Hot);
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
