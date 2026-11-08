using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>A status worth telling the user about.</summary>
public enum StatusNotice
{
    None,
    CaptureOn,
    Paused,
    NoDevice,
    DeviceChanged,
    MicLost,
    MicBack,
}

/// <summary>
/// Whether the microphone was lost while the sound of the PC goes on. The engine shows a capture as stopped from the moment
/// it dies until the new one is open, also when the microphone only changes device: so the loss is told once it has lasted
/// <see cref="SettleMs"/>. A microphone that never was there says nothing here. Pure: tested without any engine.
/// </summary>
internal sealed class MicLossWatch
{
    public const int SettleMs = 2000;

    private bool _hadMic;
    private bool _told;
    private long? _lostAt;

    /// <summary>A loss has yet to last long enough: <see cref="Check"/> must be called later, as no new status may come.</summary>
    public bool Waiting => _lostAt is not null;

    /// <summary>Takes a new status. What it returns is to be told now.</summary>
    public StatusNotice Note(AudioEngineStatus next, long nowMs)
    {
        if (next.MicCapturing)
        {
            _hadMic = true;
            _lostAt = null;
            var back = _told;
            _told = false;
            return back ? StatusNotice.MicBack : StatusNotice.None;
        }

        if (!next.MicEnabled)
        {
            _hadMic = false;
            _told = false;
            _lostAt = null;
            return StatusNotice.None;
        }

        // A pause or no device at all: the microphone is not what's wrong.
        if (next.Activity != CaptureActivity.Capturing)
        {
            _lostAt = null;
            return StatusNotice.None;
        }

        if (_hadMic)
        {
            _lostAt ??= nowMs;
        }

        return Check(nowMs);
    }

    /// <summary>A loss the user never got to read needs no good news, and the news of the capture says enough on its own.</summary>
    public static bool WorthTelling(StatusNotice mic, StatusNotice capture, bool lossNeverShown) =>
        mic != StatusNotice.MicBack || (capture == StatusNotice.None && !lossNeverShown);

    /// <summary>The loss, if it has now lasted long enough. Told once, until the microphone comes back.</summary>
    public StatusNotice Check(long nowMs)
    {
        if (_lostAt is not { } since || nowMs - since < SettleMs)
        {
            return StatusNotice.None;
        }

        _lostAt = null;
        _hadMic = false;
        _told = true;
        return StatusNotice.MicLost;
    }
}

/// <summary>
/// "Capture on" is promised at every launch and after every pause, but a device that arrives only says
/// "device changed", which the Notifications setting can hide. So until the reminder has really been shown, that news is the
/// reminder. Pure: tested without any window.
/// </summary>
internal sealed class CaptureOnReminder
{
    private bool _due = true;

    /// <summary>What to tell about a new status, instead of what <see cref="StatusTransitions.Decide"/> said.</summary>
    public StatusNotice Adjust(StatusNotice notice, AudioEngineStatus status)
    {
        if (status.Activity == CaptureActivity.Paused)
        {
            _due = true;
        }

        return notice == StatusNotice.DeviceChanged && _due ? StatusNotice.CaptureOn : notice;
    }

    /// <summary>A notification has reached the screen.</summary>
    public void Shown(NotificationKind kind)
    {
        if (kind == NotificationKind.CaptureOn)
        {
            _due = false;
        }
    }
}

/// <summary>From two successive engine statuses to what the user sees. Pure: tested without any window.</summary>
internal static class StatusTransitions
{
    public const string ClosingTip = "Hindsound - finishing a clip, then closing";
    public const string OnDefaultsProblem = "Settings file not read: defaults, mic off";

    public static StatusNotice Decide(AudioEngineStatus? previous, AudioEngineStatus next)
    {
        var from = previous?.Activity ?? CaptureActivity.Stopped;
        switch (next.Activity)
        {
            case CaptureActivity.Paused:
                return from == CaptureActivity.Paused ? StatusNotice.None : StatusNotice.Paused;

            case CaptureActivity.NoDevice:
                return from == CaptureActivity.NoDevice ? StatusNotice.None : StatusNotice.NoDevice;

            case CaptureActivity.Capturing:
                if (from is CaptureActivity.Stopped or CaptureActivity.Paused)
                {
                    return StatusNotice.CaptureOn;
                }

                if (from == CaptureActivity.NoDevice)
                {
                    return next.SystemDeviceName is null || next.SourceApp is not null ? StatusNotice.CaptureOn : StatusNotice.DeviceChanged;
                }

                // An application that closes, comes back or gets chosen changes no device.
                if (next.SourceApp is not null || previous?.SourceApp is not null)
                {
                    return StatusNotice.None;
                }

                return next.SystemDeviceName is not null
                    && !string.Equals(previous?.SystemDeviceName, next.SystemDeviceName, StringComparison.Ordinal)
                    ? StatusNotice.DeviceChanged
                    : StatusNotice.None;

            default:
                return StatusNotice.None;
        }
    }

    /// <summary>Whether a status still waiting to be shown has stopped being true.</summary>
    public static bool HasCeased(NotificationKind kind, AudioEngineStatus status) => kind switch
    {
        NotificationKind.CaptureOn => status.Activity != CaptureActivity.Capturing,
        NotificationKind.NoDevice => status.Activity != CaptureActivity.NoDevice,
        NotificationKind.MicLost => status.MicCapturing || !status.MicEnabled,
        _ => false,
    };

    /// <summary>The save shortcut, if it is not registered although one is set.</summary>
    public static HotkeyResult? RefusedSave(IReadOnlyList<HotkeyResult> hotkeys) =>
        hotkeys.FirstOrDefault(result => result.Action == HotkeyAction.SaveDefault && !result.Ok);

    /// <summary>What the reminder says instead of the keys when pressing them would do nothing, or null if they work.</summary>
    public static string? SaveHotkeyProblem(IReadOnlyList<HotkeyResult> hotkeys) => RefusedSave(hotkeys)?.Status switch
    {
        null => null,
        HotkeyStatus.InUse or HotkeyStatus.Duplicate => "Save hotkey is taken",
        _ => "Save hotkey doesn't work",
    };

    /// <summary>"Capture on", with the length and the keys of now: the settings are read again while it waits for its turn.</summary>
    /// <param name="updatedTo">The version an update just installed, said in the title; null for the usual one.</param>
    /// <param name="keptSeconds">What the memory holds at the moment it is shown; null when nothing has said yet.</param>
    public static NotificationContent CaptureOnNow(AppSettings settings, IReadOnlyList<HotkeyResult> hotkeys, string? updatedTo = null, int? keptSeconds = null) =>
        NotificationStates.CaptureOn(settings.BufferSeconds, settings.Hotkeys.SaveDefault, SaveHotkeyProblem(hotkeys), updatedTo, keptSeconds);

    /// <summary>
    /// What to show of something that waited: only "Capture on" names settings, and a memory, that may have changed. It is
    /// made again, so the version of an update that waited with it is given again too.
    /// </summary>
    public static NotificationContent Fresh(NotificationContent waited, AppSettings settings, IReadOnlyList<HotkeyResult> hotkeys, string? updatedTo = null, int? keptSeconds = null) =>
        waited.Kind == NotificationKind.CaptureOn ? CaptureOnNow(settings, hotkeys, updatedTo, keptSeconds) : waited;

    /// <summary>
    /// What the memory holds in whole seconds, read from the last status at the moment a text is made; null before the
    /// first status. Every argument is required: a caller that forgets one does not compile instead of saying a stale number.
    /// </summary>
    public static int? KeptSecondsOf(AudioEngineStatus? status) => status is null ? null : KeptDuration.SecondsOf(status.Buffered);

    /// <summary><see cref="Fresh"/> with the memory read from the last status.</summary>
    public static NotificationContent FreshFrom(NotificationContent waited, AppSettings settings, IReadOnlyList<HotkeyResult> hotkeys, string? updatedTo, AudioEngineStatus? status) =>
        Fresh(waited, settings, hotkeys, updatedTo, KeptSecondsOf(status));

    /// <summary>What a notice from the engine says, made from the status that raised it; null for a notice with no text of its own here.</summary>
    public static NotificationContent? NoticeContent(StatusNotice notice, AudioEngineStatus status, AppSettings settings, IReadOnlyList<HotkeyResult> hotkeys, string? updatedTo) => notice switch
    {
        StatusNotice.CaptureOn => CaptureOnNow(settings, hotkeys, updatedTo, KeptSecondsOf(status)),
        StatusNotice.Paused => NotificationStates.Paused(KeptSecondsOf(status) ?? 0),
        StatusNotice.NoDevice => NotificationStates.NoDevice(),
        StatusNotice.DeviceChanged => NotificationStates.DeviceChanged(status.SystemDeviceName ?? "the default output"),
        _ => null,
    };

    /// <summary>
    /// The icon and its tip, from everything that is true now, so that nothing stays out of date. The tip names the first
    /// problem. A microphone not captured, or another shortcut refused, only goes in the tip: clips still get saved.
    /// </summary>
    /// <param name="status">The last status of the engine, if any.</param>
    /// <param name="hotkeys">The last results of the shortcuts.</param>
    /// <param name="importantClipWaiting">The user has yet to hear about a clip that went wrong.</param>
    /// <param name="engineFailed">The engine could not start, so no status will come.</param>
    /// <param name="closingAfterClip">Quit was asked for and a clip is still being written: that comes before any problem, the app is on its way out.</param>
    /// <param name="onDefaults">The settings file could not be read: the defaults run, without the microphone. Clips still get saved, but not as the user set them.</param>
    public static (TrayState State, string Tip) TrayOf(AudioEngineStatus? status, IReadOnlyList<HotkeyResult> hotkeys, bool importantClipWaiting, bool engineFailed = false, bool closingAfterClip = false, bool onDefaults = false)
    {
        var activity = engineFailed ? CaptureActivity.NoDevice : status?.Activity ?? CaptureActivity.Stopped;
        var refused = RefusedSave(hotkeys);
        var problem = activity == CaptureActivity.NoDevice ? "No audio device"
            : refused is not null ? Describe(refused)
            : importantClipWaiting ? "A clip has a problem"
            : onDefaults ? OnDefaultsProblem
            : status is { Activity: CaptureActivity.Capturing, MicEnabled: true, MicCapturing: false } ? "Microphone not captured"
            : hotkeys.FirstOrDefault(result => !result.Ok) is { } other ? Describe(other)
            : null;
        var state = refused is not null || importantClipWaiting || onDefaults ? TrayState.Error : TrayStateOf(activity);
        return (state, closingAfterClip ? ClosingTip : problem is null ? "Hindsound" : "Hindsound - " + problem);
    }

    private static string Describe(HotkeyResult result) => $"{result.Gesture}: {result.Message}";

    public static TrayState TrayStateOf(CaptureActivity activity) => activity switch
    {
        CaptureActivity.Paused => TrayState.Paused,
        CaptureActivity.NoDevice => TrayState.Error,
        _ => TrayState.Active,
    };
}
