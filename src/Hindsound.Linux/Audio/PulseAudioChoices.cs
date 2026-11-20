using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The outputs and the microphones of the sound server, and the applications that play sound now, read afresh at each call.
/// An application is in the list while it has a playback stream: one that has played nothing yet, or has closed its stream,
/// is not.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class PulseAudioChoices : IAudioChoices
{
    private readonly Action<string> _log;

    public PulseAudioChoices(Action<string> log) => _log = log;

    public IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow)
    {
        try
        {
            return PulseConnection.ReadServer().Devices(flow);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log("the devices could not be listed: " + exception.GetType().Name + ": " + exception.Message);
            return [];
        }
    }

    public IReadOnlyList<AudioAppInfo> Apps()
    {
        try
        {
            var streams = PulseAppCaptureSource.ReadStreams();
            var processes = new ProcProcessSnapshot().Take();
            return AudioAppList.Build(PulseAppRules.Sessions(streams, processes), processes, Environment.ProcessId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log("the applications could not be listed: " + exception.GetType().Name + ": " + exception.Message);
            return [];
        }
    }
}
