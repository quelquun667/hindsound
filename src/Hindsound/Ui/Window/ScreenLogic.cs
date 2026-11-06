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
    public const string Prompt = "Press your combo...";
    public const string CannotClearMain = "The main save hotkey can't be empty";

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
internal readonly record struct StatusBand(StatusMark Mark, string Title, string Mic, string Duration, string Ram, string PauseLabel, bool PauseEnabled);

internal static class StatusText
{
    public static StatusBand From(AudioEngineStatus? status)
    {
        if (status is null)
        {
            return new StatusBand(StatusMark.None, "Starting", string.Empty, string.Empty, string.Empty, "Pause", false);
        }

        var (mark, title) = Headline(status);
        var mic = !status.MicEnabled ? "mic off" : status.MicCapturing ? "mic on" : "mic not captured";
        var paused = status.Activity == CaptureActivity.Paused;
        return new StatusBand(
            mark,
            title,
            mic,
            NotificationStates.FormatDuration(NotificationStates.WholeSeconds(status.Buffered)),
            Megabytes(status.MemoryBytes),
            paused ? "Resume" : "Pause",
            status.Activity != CaptureActivity.Stopped);
    }

    private static (StatusMark Mark, string Title) Headline(AudioEngineStatus status)
    {
        var app = status.SourceApp is { } exe ? Path.GetFileNameWithoutExtension(exe) : null;
        return status.Activity switch
        {
            CaptureActivity.Paused => (StatusMark.Pause, "Paused"),
            CaptureActivity.NoDevice => (StatusMark.Warning, app is null ? "No audio device" : "Can't capture " + app),
            CaptureActivity.Capturing when app is null => (StatusMark.Dot, "Capturing"),
            // The state comes first: a long name gets cut at the end of the title.
            CaptureActivity.Capturing when status.SourceAppMissing => (StatusMark.Caution, "Not running: " + app),
            CaptureActivity.Capturing when status.SystemCapturing => (StatusMark.Dot, "Capturing " + app),
            CaptureActivity.Capturing => (StatusMark.Warning, "Can't capture " + app),
            _ => (StatusMark.None, "Not capturing"),
        };
    }

    public static string Megabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"~{Math.Round(bytes / 1_000_000.0, MidpointRounding.AwayFromZero):0} MB");

    /// <summary>The words of the big button: "30s", "1 min", "1:30".</summary>
    public static string SaveLabel(int clipSeconds) =>
        "Save last " + (clipSeconds < 60 ? clipSeconds + "s"
            : clipSeconds % 60 == 0 ? clipSeconds / 60 + " min"
            : NotificationStates.FormatDuration(clipSeconds));

    /// <summary>"720 KB", "1.4 MB".</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 1_000_000)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (long)Math.Round(bytes / 1000.0, MidpointRounding.AwayFromZero))} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_000_000.0:0.0} MB");
    }

    public static string GainLabel(double db) =>
        string.Create(CultureInfo.InvariantCulture, $"{(db > 0 ? "+" : string.Empty)}{Math.Round(db):0} dB");
}

/// <summary>What the foot of the window shows about the last clip.</summary>
internal sealed record LastClipInfo(TimeSpan Duration, DateTime Time, long SizeBytes)
{
    /// <summary>What the notification says about this clip now: the first-launch card says the same. Null when it was copied without a word.</summary>
    public NotificationContent? Notice { get; init; }

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
