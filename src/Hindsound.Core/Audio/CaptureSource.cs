using NAudio.Wave;

namespace Hindsound.Audio;

/// <summary>Which side of the audio a capture listens to.</summary>
public enum AudioFlow
{
    /// <summary>The sound of the PC.</summary>
    System,

    /// <summary>The microphone.</summary>
    Mic,
}

/// <param name="Flow">Which side.</param>
/// <param name="DeviceId">The endpoint, from <see cref="ICaptureSourceFactory.ResolveDevice"/>.</param>
/// <param name="ExcludeOwnProcess">For the PC sound: leave this app's own sounds out. Only possible on the default output. The engine reports the exclusion from this flag, not from what the source did: a factory that cannot leave them out must throw, so that the engine opens an ordinary capture and reports it as such. Never open a plain capture for it.</param>
/// <param name="TargetProcessId">Capture only this process and its children, rather than an endpoint.</param>
/// <param name="TargetName">The name to show for that process.</param>
public sealed record CaptureRequest(AudioFlow Flow, string DeviceId, bool ExcludeOwnProcess, int? TargetProcessId = null, string? TargetName = null);

/// <summary>
/// What the system says about a packet. The values are those of WASAPI's buffer flags, so the Windows capture passes them on
/// as they come.
/// </summary>
[Flags]
public enum PacketFlags
{
    None = 0,

    /// <summary>This packet does not follow the previous one: frames were lost in between.</summary>
    DataDiscontinuity = 1,

    /// <summary>The packet holds silence, whatever its bytes say.</summary>
    Silent = 2,

    /// <summary>The packet's timestamp is not to be trusted.</summary>
    TimestampError = 4,
}

/// <summary>One packet of a capture, on the audio thread. The buffer is only valid during the call.</summary>
public delegate void CapturePacketHandler(ReadOnlySpan<byte> buffer, PacketFlags flags, long devicePosition, long qpcPosition);

/// <summary>An audio capture that can be started once, separated from the hardware so the engine can be tested.</summary>
public interface ICaptureSource : IDisposable
{
    WaveFormat Format { get; }

    /// <summary>The device name to show the user, if known.</summary>
    string? DeviceName { get; }

    /// <summary>Raised on the audio thread for each packet.</summary>
    event CapturePacketHandler PacketAvailable;

    /// <summary>Raised when the capture ends. The exception says why it ended by itself, for instance a device unplugged.</summary>
    event Action<Exception?> Stopped;

    void Start();

    void Stop();
}

/// <summary>Finds devices and opens captures.</summary>
public interface ICaptureSourceFactory
{
    /// <summary>The endpoint to capture from now, or null if there is none. Never throws.</summary>
    /// <param name="flow">Which side.</param>
    /// <param name="requestedId">A specific endpoint, or null for the system default.</param>
    string? ResolveDevice(AudioFlow flow, string? requestedId);

    Task<ICaptureSource> OpenAsync(CaptureRequest request);
}

/// <summary>An application that can be captured on its own: its exe name for the settings, its name for the user.</summary>
public sealed record AudioAppInfo(string ExeName, string Name);

/// <summary>An audio endpoint: its id for the settings, its name for the user.</summary>
public sealed record AudioDeviceInfo(string Id, string Name);
