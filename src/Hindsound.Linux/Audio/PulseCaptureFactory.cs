using System.Runtime.Versioning;
using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The capture through the sound server (PulseAudio, or PipeWire through its PulseAudio side): the monitor of the default
/// output for the PC's sound, and the default input, or the one set, for the microphone. Each capture is one record stream
/// on a connection of its own; each lookup reads the server afresh, so a server that restarts is simply found again.
/// Its choices are made before any call to the server, so they are tested on every system.
/// </summary>
internal sealed class PulseCaptureFactory : ICaptureSourceFactory
{
    /// <summary>The name the desktop's sound settings show for the stream of the PC's sound.</summary>
    public const string SystemStreamName = "Sound of the PC";

    /// <summary>The name the desktop's sound settings show for the stream of the microphone.</summary>
    public const string MicStreamName = "Microphone";

    private readonly Action<string> _log;
    private readonly Func<PulseServerState> _readServer;
    private readonly Lock _gate = new();
    private PulseServerState _last = PulseServerState.Empty;
    private string? _lastProblem;

    public PulseCaptureFactory(Action<string> log)
        : this(log, ReadServer)
    {
    }

    /// <param name="log">The log of the run.</param>
    /// <param name="readServer">Reads what the server has. Tests pass their own.</param>
    internal PulseCaptureFactory(Action<string> log, Func<PulseServerState> readServer)
    {
        _log = log;
        _readServer = readServer;
    }

    /// <summary>
    /// The device to capture now, or null when the server answered without one, or could not be reached. Unlike the
    /// interface says, this throws when the server was reached but did not answer in time: it is busy, for instance
    /// reconfiguring while a headset connects, which is when the engine asks. Null would make the engine close a capture
    /// that still works; a failure makes it keep the capture and look again later.
    /// </summary>
    public string? ResolveDevice(AudioFlow flow, string? requestedId)
    {
        try
        {
            var state = _readServer();
            lock (_gate)
            {
                _last = state;
                _lastProblem = null;
            }

            return state.Resolve(flow, requestedId);
        }
        catch (TimeoutException exception)
        {
            Report(exception);

            // Not a TimeoutException: the engine reads that one as its own limit on the call, and would set the lookup aside.
            throw new IOException("The sound server did not answer in time.", exception);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Report(exception);
            return null;
        }
    }

    public Task<ICaptureSource> OpenAsync(CaptureRequest request)
    {
        if (PulseCaptureRules.Refuse(request) is { } refusal)
        {
            return Task.FromException<ICaptureSource>(refusal);
        }

        PulseServerState state;
        lock (_gate)
        {
            state = _last;
        }

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromException<ICaptureSource>(new PlatformNotSupportedException("The sound server is reached on Linux only."));
        }

        var name = state.DisplayNameOf(request.Flow, request.DeviceId) ?? request.DeviceId;
        var streamName = request.Flow == AudioFlow.System ? SystemStreamName : MicStreamName;
        return OpenOnServer(request, streamName, name);
    }

    [SupportedOSPlatform("linux")]
    private static Task<ICaptureSource> OpenOnServer(CaptureRequest request, string streamName, string name) =>
        Task.Run<ICaptureSource>(() => PulseCaptureSource.Open(request.Flow, request.DeviceId, streamName, name));

    private static PulseServerState ReadServer() => OperatingSystem.IsLinux()
        ? PulseConnection.ReadServer()
        : throw new PlatformNotSupportedException("The sound server is reached on Linux only.");

    // The engine looks again every second while there is nothing to capture: the same problem is written once.
    private void Report(Exception exception)
    {
        var line = "the sound server could not be read: " + exception.GetType().Name + ": " + exception.Message;
        lock (_gate)
        {
            if (line == _lastProblem)
            {
                return;
            }

            _lastProblem = line;
        }

        _log(line);
    }
}
