using Hindsound.Audio;
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

    public static TrayState TrayStateOf(CaptureActivity activity) => activity switch
    {
        CaptureActivity.Paused => TrayState.Paused,
        CaptureActivity.NoDevice => TrayState.Error,
        _ => TrayState.Active,
    };
}
