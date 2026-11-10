using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The outputs and the microphones of the sound server, read afresh at each call. Capturing a single application is not in
/// the Linux version, so there are no applications to offer.
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

    public IReadOnlyList<AudioAppInfo> Apps() => [];
}
