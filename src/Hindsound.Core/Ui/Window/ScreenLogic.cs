using System.Globalization;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Ui.Window;

internal enum CaptureStepKind
{
    /// <summary>Still waiting for a combination.</summary>
    Listening,

    /// <summary>Escape: nothing changes.</summary>
    Cancelled,

    /// <summary>Tab alone: nothing changes, and the focus moves on.</summary>
    CancelledAndMoveOn,

    /// <summary>Backspace: the hotkey is removed.</summary>
    Cleared,

    /// <summary>A combination that can be tried.</summary>
    Captured,
}

internal readonly record struct CaptureStep(CaptureStepKind Kind, string? Gesture = null);

/// <summary>
/// "Press your combo...": what each key press means while a hotkey field listens. The global hotkeys are suspended for
/// that time by the screen. Modifiers alone only show up in the field.
/// </summary>
internal sealed class HotkeyCapture
{
    public static string Prompt => Tr.Of("Press your combo...", "Appuie sur ta combinaison...");

    public static string CannotClearMain => Tr.Of("The main save hotkey can't be empty", "Le raccourci principal d'enregistrement ne peut pas être vide");

    /// <summary>The action being set, while listening.</summary>
    public HotkeyAction? Action { get; private set; }

    public bool Listening => Action is not null;

    /// <summary>The modifiers held right now, to draw them as they go down.</summary>
    public uint Held { get; private set; }

    /// <summary>Why the last key did not count.</summary>
    public string? Message { get; private set; }

    public void Begin(HotkeyAction action)
    {
        Action = action;
        Held = 0;
        Message = null;
    }

    public void End()
    {
        Action = null;
        Held = 0;
        Message = null;
    }

    /// <param name="vk">The key that went down.</param>
    /// <param name="modifiers">The modifiers held, as <see cref="HotkeyGesture"/> flags.</param>
    public CaptureStep Key(uint vk, uint modifiers)
    {
        Held = modifiers;
        if (Vk.IsModifier(vk))
        {
            return new CaptureStep(CaptureStepKind.Listening);
        }

        if (modifiers == 0)
        {
            switch (vk)
            {
                case Vk.Escape:
                    return new CaptureStep(CaptureStepKind.Cancelled);
                case Vk.Tab:
                    return new CaptureStep(CaptureStepKind.CancelledAndMoveOn);
                case Vk.Back:
                    if (Action == HotkeyAction.SaveDefault)
                    {
                        Message = CannotClearMain;
                        return new CaptureStep(CaptureStepKind.Listening);
                    }

                    return new CaptureStep(CaptureStepKind.Cleared);
            }
        }

        if (!HotkeyGesture.TryCreate(modifiers, vk, out var gesture))
        {
            Message = HotkeyResult.InvalidMessage;
            return new CaptureStep(CaptureStepKind.Listening);
        }

        return new CaptureStep(CaptureStepKind.Captured, gesture.ToString());
    }

    /// <summary>A modifier went up: the field no longer shows it.</summary>
    public void KeyUp(uint modifiers) => Held = modifiers;

    /// <summary>The held modifiers as text, such as "Ctrl+Shift", or nothing.</summary>
    public string HeldText()
    {
        var parts = new List<string>();
        if ((Held & HotkeyGesture.ModControl) != 0) parts.Add("Ctrl");
        if ((Held & HotkeyGesture.ModAlt) != 0) parts.Add("Alt");
        if ((Held & HotkeyGesture.ModShift) != 0) parts.Add("Shift");
        if ((Held & HotkeyGesture.ModWin) != 0) parts.Add("Win");
        return string.Join('+', parts);
    }
}

internal enum StatusMark
{
    /// <summary>The red dot of the capture.</summary>
    Dot,

    /// <summary>Two bars.</summary>
    Pause,

    /// <summary>The triangle of the errors.</summary>
    Warning,

    /// <summary>The same triangle in amber: nothing is broken, but what was asked for is not there.</summary>
    Caution,

    /// <summary>Nothing yet.</summary>
    None,
}

/// <summary>What the band at the top says, from the engine's status alone.</summary>
internal readonly record struct StatusBand(StatusMark Mark, string Title, string Mic, string Duration, string Ram, string PauseLabel, bool PauseEnabled, bool SaveEnabled);

internal static class StatusText
{
    /// <summary>The words of the band's button while the capture runs.</summary>
    public static string PauseLabel => Tr.Of("Pause", "Pause");

    /// <summary>The words of the band's button while the capture is paused. The longer of the two sets the button's width.</summary>
    public static string ResumeLabel => Tr.Of("Resume", "Reprendre");

    // French puts a space before a colon; a no-break one, so that the line never starts with the colon.
    private static string Colon => Tr.Of(": ", "\u00A0: ");

    /// <param name="status">The last status of the engine, if any.</param>
    /// <param name="capSeconds">What the setting lets the memory hold; null to show only what it holds.</param>
    public static StatusBand From(AudioEngineStatus? status, int? capSeconds = null)
    {
        if (status is null)
        {
            // Nothing says the memory is empty yet, and pressing save at this moment gets its own answer: "Nothing to save yet".
            return new StatusBand(StatusMark.None, Tr.Of("Starting", "Démarrage"), string.Empty, string.Empty, string.Empty, PauseLabel, false, true);
        }

        var (mark, title) = Headline(status);
        var mic = !status.MicEnabled ? Tr.Of("mic off", "micro coupé") : status.MicCapturing ? Tr.Of("mic on", "micro actif") : Tr.Of("mic not captured", "micro non capté");
        var paused = status.Activity == CaptureActivity.Paused;
        if (status.Activity == CaptureActivity.NoDevice)
        {
            // A state of error is never cut: with the figures beside it, the title became "No audio d...".
            return new StatusBand(mark, title, string.Empty, string.Empty, string.Empty, PauseLabel, true, KeptDuration.CanSave(status.Buffered));
        }

        return new StatusBand(
            mark,
            title,
            mic,
            DurationCell(KeptDuration.SecondsOf(status.Buffered), capSeconds),
            Megabytes(status.MemoryBytes),
            paused ? ResumeLabel : PauseLabel,
            status.Activity != CaptureActivity.Stopped,
            KeptDuration.CanSave(status.Buffered));
    }

    // Filling, the cell says both figures: the setting alone would read as what the memory already holds.
    private static string DurationCell(int kept, int? cap)
    {
        if (cap is not { } setting)
        {
            return NotificationStates.FormatDuration(kept);
        }

        return KeptDuration.StateOf(kept, setting) == KeptState.Full
            ? NotificationStates.FormatDuration(setting)
            : $"{NotificationStates.FormatDuration(kept)} / {NotificationStates.FormatDuration(setting)}";
    }

    private static (StatusMark Mark, string Title) Headline(AudioEngineStatus status)
    {
        var app = status.SourceApp is { } exe ? AudioAppList.NameOf(exe) : null;
        return status.Activity switch
        {
            CaptureActivity.Paused => (StatusMark.Pause, Tr.Of("Paused", "En pause")),
            CaptureActivity.NoDevice => (StatusMark.Warning, app is null ? Tr.Of("No audio device", "Aucun périphérique audio") : CannotCapture(app)),
            CaptureActivity.Capturing when app is null => (StatusMark.Dot, Tr.Of("Capturing", "Capture")),
            // The state comes first: a long name gets cut at the end of the title.
            CaptureActivity.Capturing when status.SourceAppMissing => (StatusMark.Caution, Tr.Of("Not running", "Pas lancé") + Colon + app),
            CaptureActivity.Capturing when status.SystemCapturing && status.SourceAppLost => (StatusMark.Warning, Tr.Of("Sound lost", "Son perdu") + Colon + app),
            CaptureActivity.Capturing when status.SystemCapturing => (StatusMark.Dot, Tr.French ? "Capture " + Tr.FrenchOf(app!) : "Capturing " + app),
            CaptureActivity.Capturing => (StatusMark.Warning, CannotCapture(app)),
            _ => (StatusMark.None, Tr.Of("Not capturing", "Pas de capture")),
        };
    }

    internal static string CannotCapture(string? app) => Tr.French ? "Capture impossible" + Colon + app : "Can't capture " + app;

    public static string Megabytes(long bytes) =>
        "~" + Tr.Size(Math.Round(bytes / 1_000_000.0, MidpointRounding.AwayFromZero), "0", SizeUnit.MB);

    /// <summary>
    /// The words of the big button: "30s", "1 min", "1:30". The same at any moment: its width places the Pause button, and "up to"
    /// stays true with 0:12 in memory. French says the length alone: "jusqu'à" took the room of the title.
    /// </summary>
    public static string SaveLabel(int clipSeconds) =>
        Tr.Of("Save up to ", "Enregistrer ") + (clipSeconds < 60 ? clipSeconds + Tr.Of("s", "\u00A0s")
            : clipSeconds % 60 == 0 ? clipSeconds / 60 + Tr.Of(" min", " min")
            : NotificationStates.FormatDuration(clipSeconds));

    /// <summary>The same length in a sentence: "30 seconds", "1 minute", "1:30".</summary>
    public static string ClipWords(int clipSeconds) =>
        clipSeconds < 60 ? Tr.Plural(clipSeconds, "{0} second", "{0} seconds", "{0} seconde", "{0} secondes")
        : clipSeconds % 60 == 0 ? Tr.Plural(clipSeconds / 60, "{0} minute", "{0} minutes", "{0} minute", "{0} minutes")
        : NotificationStates.FormatDuration(clipSeconds);

    /// <summary>The first step of the first-launch card.</summary>
    public static string FirstRunHotkeyNote(int clipSeconds) =>
        Tr.Format(
            $"Saves up to the last {ClipWords(clipSeconds)}, from anywhere, even in a game.",
            $"Enregistre jusqu'à {ClipWords(clipSeconds)}, de partout, même en jeu.");

    /// <summary>What the update card says is lost. No figure under a few seconds: there is nothing to warn about.</summary>
    /// <param name="status">The last status of the engine; before any, only the setting can be named.</param>
    /// <param name="capSeconds">What the setting lets the memory hold.</param>
    public static string UpdateCardBody(AudioEngineStatus? status, int capSeconds)
    {
        var restart = Tr.Of("Updating restarts Hindsound.", "La mise à jour redémarre Hindsound.");
        if (status is null)
        {
            return Lost(restart, capSeconds);
        }

        var kept = KeptDuration.SecondsOf(status.Buffered);
        return kept < KeptDuration.StartingBelowSeconds
            ? restart
            : Lost(restart, KeptDuration.Shown(kept, capSeconds));
    }

    private static string Lost(string restart, int seconds)
    {
        var duration = NotificationStates.FormatDuration(seconds);
        return Tr.Format($"{restart} The last {duration} in memory will be lost.", $"{restart} Ce qui est gardé en mémoire ({duration}) sera perdu.");
    }

    /// <summary>"720 KB", "1.4 MB"; "720 Ko", "1,4 Mo" in French.</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 1_000_000)
        {
            return Tr.Size(Math.Max(1, (long)Math.Round(bytes / 1000.0, MidpointRounding.AwayFromZero)), "0", SizeUnit.KB);
        }

        return Tr.Size(bytes / 1_000_000.0, "0.0", SizeUnit.MB);
    }

    public static string GainLabel(double db) =>
        string.Create(CultureInfo.InvariantCulture, $"{(db > 0 ? "+" : string.Empty)}{Math.Round(db):0}") + Tr.Of(" dB", " dB");
}

/// <summary>What the foot of the window shows about the last clip.</summary>
internal sealed record LastClipInfo(TimeSpan Duration, DateTime Time, long SizeBytes)
{
    /// <summary>What the notification says about this clip now: the first-launch card says the same. Null when it was copied without a word.</summary>
    public NotificationContent? Notice { get => NoticeMaker is { } make ? make() : _notice; init => _notice = value; }

    /// <summary>Makes what the notification says about this clip, each time it is asked: its words follow the language of the moment.</summary>
    public Func<NotificationContent?>? NoticeMaker { get; init; }

    private readonly NotificationContent? _notice;

    public string DurationText => NotificationStates.FormatDuration(NotificationStates.WholeSeconds(Duration));

    public string TimeText => Time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string SizeText => StatusText.FileSize(SizeBytes);
}

/// <summary>Turns the loudest sample of each reading into the height of the bar: fast up, slow down, on a dB scale.</summary>
internal sealed class MicMeter
{
    private const float FloorDb = -60;
    private const float FallPerTick = 0.06f;

    /// <summary>From 0 to 1.</summary>
    public float Level { get; private set; }

    public void Feed(float peak)
    {
        var target = ScaleOf(peak);
        Level = target >= Level ? target : Math.Max(target, Level - FallPerTick);
    }

    public void Reset() => Level = 0;

    public static float ScaleOf(float peak)
    {
        var magnitude = float.IsNaN(peak) ? 0 : Math.Abs(peak);
        if (magnitude <= 0)
        {
            return 0;
        }

        var db = 20 * MathF.Log10(magnitude);
        return Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
    }

    /// <summary>The loudest sample, from 0 to 1 and above.</summary>
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }
}

/// <summary>When the native text box may take the keyboard. WM_SETFOCUS gives it back when the window returns to the foreground.</summary>
internal static class EditFocus
{
    public static bool Wanted(bool wasVisible, bool foreground) => !wasVisible && foreground;

    /// <summary>A key on a field scrolled away. A modifier alone means nothing yet, and Alt and Win belong to Windows.</summary>
    public static bool KeyBringsBack(uint vk, uint modifiers) =>
        !Vk.IsModifier(vk) && (modifiers & (HotkeyGesture.ModAlt | HotkeyGesture.ModWin)) == 0;
}

/// <summary>What a key held down may do: move and type, but never save, toggle or open again.</summary>
internal static class KeyRepeat
{
    private const long RepeatBit = 1L << 30;

    /// <summary>Bit 30 of a key message: the key was already down.</summary>
    public static bool FromLParam(nint lParam) => ((long)lParam & RepeatBit) != 0;

    /// <param name="vk">The virtual key.</param>
    /// <param name="inField">The focus is on a text field: holding a key there types.</param>
    public static bool Acts(uint vk, bool inField) => vk switch
    {
        Vk.Left or Vk.Right or Vk.Up or Vk.Down or Vk.PageUp or Vk.PageDown or Vk.Home or Vk.End or Vk.Back => true,
        Vk.Tab or Vk.Enter or Vk.Escape => false,
        _ => inField,
    };

    /// <summary>A repeat that does nothing is still taken from Windows if the key itself means something here.</summary>
    public static bool Swallowed(uint vk) => vk is Vk.Tab or Vk.Enter or Vk.Escape or Vk.Space;
}

/// <summary>
/// The keys typed for a hotkey stay the capture's until their last message. The capture ends on the key down, and the
/// WM_SYSCHAR of that key comes after: Windows would open the window menu on Alt+Space, and beep on Alt+letter.
/// </summary>
internal sealed class CaptureKeys
{
    /// <summary>The last key that went down was typed for a hotkey: what follows from it stays away from Windows.</summary>
    public bool Swallow { get; private set; }

    /// <param name="listening">A hotkey was being typed when this key went down.</param>
    /// <param name="repeat">The key was already down: it stays what it was.</param>
    public void KeyDown(bool listening, bool repeat) => Swallow = listening || (repeat && Swallow);
}

/// <summary>Turns the wheel deltas of Windows into notches. A touchpad sends small deltas that only count together.</summary>
internal static class WheelNotches
{
    public const int PerNotch = 120;

    /// <param name="rest">What the last call left over.</param>
    /// <param name="delta">The delta of this message.</param>
    /// <returns>The whole notches, and what to pass as the rest next time. A change of direction forgets the rest.</returns>
    public static (int Notches, int LeftOver) Add(int rest, int delta)
    {
        var total = Math.Sign(rest) * Math.Sign(delta) < 0 ? delta : rest + delta;
        var notches = total / PerNotch;
        return (notches, total - notches * PerNotch);
    }
}

/// <summary>Moves a focus over a list where some entries cannot take it. Wraps around.</summary>
internal static class FocusRing
{
    /// <summary>The next entry that can take the focus, or -1 if none can.</summary>
    public static int Next(int count, int current, bool forward, Func<int, bool> canFocus)
    {
        if (count == 0)
        {
            return -1;
        }

        var step = forward ? 1 : -1;
        var index = current < 0 ? (forward ? -1 : 0) : current;
        for (var i = 0; i < count; i++)
        {
            index = (index + step + count) % count;
            if (canFocus(index))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>The clip of the accent preview and of "Show test notification".</summary>
internal static class SampleClip
{
    private static readonly float[] Bars =
    [
        0.25f, 0.25f, 0.3f, 0.39f, 0.42f, 0.4f, 0.15f, 0.12f, 0.38f, 0.44f, 0.51f, 0.26f,
        0.14f, 0.23f, 0.35f, 0.44f, 0.36f, 0.35f, 0.78f, 0.84f, 0.88f, 0.59f, 0.8f, 0.8f,
    ];

    public static NotificationContent Notification(uint accent)
    {
        var thirty = TimeSpan.FromSeconds(30);
        return NotificationStates.ForClip(new ClipResult(["clip.mp3"], thirty, 1, false, Bars, false), thirty, true, true, accent);
    }
}
