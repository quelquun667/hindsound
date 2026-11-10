using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hindsound.Audio;

/// <summary>A <see cref="WasapiRecorder"/> behind <see cref="ICaptureSource"/>.</summary>
public sealed class CaptureSource : ICaptureSource
{
    private readonly WasapiRecorder _recorder;

    public CaptureSource(WasapiRecorder recorder, string? deviceName)
    {
        _recorder = recorder;
        _forwarder = new PacketForwarder(
            () => _recorder.DataAvailable += _forwarder!.Forward,
            () => _recorder.DataAvailable -= _forwarder!.Forward);
        DeviceName = deviceName;
        _recorder.RecordingStopped += (_, args) => Stopped?.Invoke(args.Exception);
    }

    public WaveFormat Format => _recorder.WaveFormat;

    public string? DeviceName { get; }

    private readonly PacketForwarder _forwarder;

    /// <summary>Subscribed to the recorder only while someone listens, as when this event forwarded straight to it.</summary>
    public event CapturePacketHandler PacketAvailable
    {
        add => _forwarder.Add(value);
        remove => _forwarder.Remove(value);
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
    /// The applications to offer: those with an audio session on any output, as an app can be set to play on one that
    /// is not the default, without this app. The sessions come from the audio API and the names from the process list:
    /// nothing here opens a handle to any application.
    /// </summary>
    public static IReadOnlyList<AudioAppInfo> ListApps()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var found = new List<(int ProcessId, string? DisplayName)>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (!session.IsSystemSoundsSession)
                        {
                            found.Add(((int)session.GetProcessID, session.DisplayName));
                        }
                    }
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
