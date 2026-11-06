using Hindsound.Audio;
using Hindsound.Input;
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
}

/// <summary>From two successive engine statuses to what the user sees. Pure: tested without any window.</summary>
internal static class StatusTransitions
{
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

    /// <summary>
    /// The icon and its tip, from everything that is true now, so that nothing stays out of date. The tip names the first
    /// problem. A microphone not captured, or another shortcut refused, only goes in the tip: clips still get saved.
    /// </summary>
    /// <param name="status">The last status of the engine, if any.</param>
    /// <param name="hotkeys">The last results of the shortcuts.</param>
    /// <param name="importantClipWaiting">The user has yet to hear about a clip that went wrong.</param>
    /// <param name="engineFailed">The engine could not start, so no status will come.</param>
    public static (TrayState State, string Tip) TrayOf(AudioEngineStatus? status, IReadOnlyList<HotkeyResult> hotkeys, bool importantClipWaiting, bool engineFailed = false)
    {
        var activity = engineFailed ? CaptureActivity.NoDevice : status?.Activity ?? CaptureActivity.Stopped;
        var noDevice = activity == CaptureActivity.NoDevice;
        var refused = RefusedSave(hotkeys);
        var problem = noDevice ? "No audio device"
            : refused is not null ? Describe(refused)
            : importantClipWaiting ? "A clip has a problem"
            : status is { Activity: CaptureActivity.Capturing, MicEnabled: true, MicCapturing: false } ? "Microphone not captured"
            : hotkeys.FirstOrDefault(result => !result.Ok) is { } other ? Describe(other)
            : null;
        var state = noDevice || refused is not null || importantClipWaiting ? TrayState.Error : TrayStateOf(activity);
        return (state, problem is null ? "Hindsound" : "Hindsound - " + problem);
    }

    private static string Describe(HotkeyResult result) => $"{result.Gesture}: {result.Message}";

    public static TrayState TrayStateOf(CaptureActivity activity) => activity switch
    {
        CaptureActivity.Paused => TrayState.Paused,
        CaptureActivity.NoDevice => TrayState.Error,
        _ => TrayState.Active,
    };
}
