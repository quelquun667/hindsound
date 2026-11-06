using NAudio.CoreAudioApi;
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
/// <param name="ExcludeOwnProcess">For the PC sound: leave this app's own sounds out. Only possible on the default output.</param>
/// <param name="TargetProcessId">Capture only this process and its children, rather than an endpoint.</param>
/// <param name="TargetName">The name to show for that process.</param>
public sealed record CaptureRequest(AudioFlow Flow, string DeviceId, bool ExcludeOwnProcess, int? TargetProcessId = null, string? TargetName = null);

/// <summary>An audio capture that can be started once, separated from the hardware so the engine can be tested.</summary>
public interface ICaptureSource : IDisposable
{
    WaveFormat Format { get; }

    /// <summary>The device name to show the user, if known.</summary>
    string? DeviceName { get; }

    /// <summary>Raised on the audio thread for each packet.</summary>
    event CaptureDataAvailableHandler PacketAvailable;

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

/// <summary>A <see cref="WasapiRecorder"/> behind <see cref="ICaptureSource"/>.</summary>
public sealed class CaptureSource : ICaptureSource
{
    private readonly WasapiRecorder _recorder;

    public CaptureSource(WasapiRecorder recorder, string? deviceName)
    {
        _recorder = recorder;
        DeviceName = deviceName;
        _recorder.RecordingStopped += (_, args) => Stopped?.Invoke(args.Exception);
    }

    public WaveFormat Format => _recorder.WaveFormat;

    public string? DeviceName { get; }

    public event CaptureDataAvailableHandler PacketAvailable
    {
        add => _recorder.DataAvailable += value;
        remove => _recorder.DataAvailable -= value;
    }

    public event Action<Exception?>? Stopped;

    public void Start() => _recorder.StartRecording();

    public void Stop() => _recorder.StopRecording();

    public void Dispose() => _recorder.Dispose();
}

/// <summary>Opens the real WASAPI captures.</summary>
public sealed class WasapiCaptureSourceFactory : ICaptureSourceFactory
{
    private readonly uint _processId;

    /// <param name="processId">The process whose sounds stay out of the PC capture: by default, this app.</param>
    public WasapiCaptureSourceFactory(uint? processId = null)
    {
        _processId = processId ?? (uint)Environment.ProcessId;
    }

    /// <summary>The active devices of one side, to offer in the settings.</summary>
    public static IReadOnlyList<AudioDeviceInfo> ListDevices(AudioFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = new List<AudioDeviceInfo>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlowOf(flow), DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                }
            }

            return devices;
        }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            return [];
        }
    }

    /// <summary>
    /// The applications to offer: those with an audio session on the default output, without this app. The sessions
    /// come from the audio API and the names from the process list: nothing here opens a handle to any application.
    /// </summary>
    public static IReadOnlyList<AudioAppInfo> ListApps()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return [];
            }

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var found = new List<(int ProcessId, string? DisplayName)>();
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (!session.IsSystemSoundsSession)
                {
                    found.Add(((int)session.GetProcessID, session.DisplayName));
                }
            }

            return AudioAppList.Build(found, new ToolHelpProcessSnapshot().Take(), Environment.ProcessId);
        }
        catch (Exception exception) when (IsAudioFailure(exception) || exception is System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    public string? ResolveDevice(AudioFlow flow, string? requestedId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var dataFlow = DataFlowOf(flow);
            if (requestedId is null)
            {
                if (!enumerator.HasDefaultAudioEndpoint(dataFlow, Role.Multimedia))
                {
                    return null;
                }

                using var fallback = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
                return fallback.ID;
            }

            using var device = enumerator.GetDevice(requestedId);
            return device.State == DeviceState.Active && device.DataFlow == dataFlow ? device.ID : null;
        }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            return null;
        }
    }

    public async Task<ICaptureSource> OpenAsync(CaptureRequest request)
    {
        if (request.TargetProcessId is { } target)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                throw new PlatformNotSupportedException("Process loopback needs Windows 10 version 2004 or later.");
            }

            var appRecorder = await new WasapiRecorderBuilder()
                .WithProcessLoopback((uint)target, ProcessLoopbackMode.IncludeTargetProcessTree)
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(InternalFormat.SampleRate, InternalFormat.SystemChannels))
                .BuildAsync()
                .ConfigureAwait(false);
            return new CaptureSource(appRecorder, request.TargetName);
        }

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(request.DeviceId);
        var name = device.FriendlyName;

        if (request.Flow == AudioFlow.Mic)
        {
            return new CaptureSource(new WasapiRecorderBuilder().WithDevice(device).Build(), name);
        }

        if (!request.ExcludeOwnProcess)
        {
            return new CaptureSource(new WasapiRecorderBuilder().WithLoopbackCapture().WithDevice(device).Build(), name);
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("Process loopback needs Windows 10 version 2004 or later.");
        }

        var recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback(_processId, ProcessLoopbackMode.ExcludeTargetProcessTree)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(InternalFormat.SampleRate, InternalFormat.SystemChannels))
            .BuildAsync()
            .ConfigureAwait(false);
        return new CaptureSource(recorder, name);
    }

    private static DataFlow DataFlowOf(AudioFlow flow) => flow == AudioFlow.Mic ? DataFlow.Capture : DataFlow.Render;

    private static bool IsAudioFailure(Exception exception) =>
        exception is System.Runtime.InteropServices.COMException or CoreAudioException or InvalidOperationException or ArgumentException or UnauthorizedAccessException;
}

/// <summary>An application that can be captured on its own: its exe name for the settings, its name for the user.</summary>
public sealed record AudioAppInfo(string ExeName, string Name);

/// <summary>An audio endpoint: its id for the settings, its name for the user.</summary>
public sealed record AudioDeviceInfo(string Id, string Name);
