using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>An output of the sound server.</summary>
/// <param name="Name">Its name, which the settings keep as its id.</param>
/// <param name="Description">The name to show.</param>
/// <param name="MonitorSource">The source that carries what it plays, if it has one.</param>
internal sealed record PulseSink(string Name, string Description, string? MonitorSource);

/// <summary>An input of the sound server: a microphone, or the monitor of an output.</summary>
internal sealed record PulseSource(string Name, string Description, bool IsMonitor);

/// <summary>
/// What the sound server has, read at one moment, and what the app makes of it. The PC's sound is the monitor of an output;
/// the microphone is any source that is not a monitor.
/// </summary>
internal sealed record PulseServerState(string? DefaultSink, string? DefaultSource, IReadOnlyList<PulseSink> Sinks, IReadOnlyList<PulseSource> Sources)
{
    public static readonly PulseServerState Empty = new(null, null, [], []);

    /// <summary>
    /// The source to capture, or null for none. For the PC's sound, the id is an output and the answer its monitor, so a new
    /// default output gives a new answer and the engine moves to it. A microphone must be a real input: with no microphone,
    /// the server makes the monitor of an output its default source, and recording it as the microphone would record the PC
    /// twice.
    /// </summary>
    public string? Resolve(AudioFlow flow, string? requestedId)
    {
        if (flow == AudioFlow.System)
        {
            var sink = Sinks.FirstOrDefault(sink => sink.Name == (requestedId ?? DefaultSink));
            return string.IsNullOrEmpty(sink?.MonitorSource) ? null : sink.MonitorSource;
        }

        var source = Sources.FirstOrDefault(source => source.Name == (requestedId ?? DefaultSource));
        return source is null || source.IsMonitor ? null : source.Name;
    }

    /// <summary>The name to show for a source <see cref="Resolve"/> gave: the output it is the monitor of, or the microphone.</summary>
    public string? DisplayNameOf(AudioFlow flow, string sourceName)
    {
        if (flow == AudioFlow.System)
        {
            return Sinks.FirstOrDefault(sink => sink.MonitorSource == sourceName)?.Description;
        }

        return Sources.FirstOrDefault(source => source.Name == sourceName)?.Description;
    }

    /// <summary>The devices to offer for a side: the outputs, or the inputs that are not monitors.</summary>
    public IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow) => flow == AudioFlow.System
        ? [.. Sinks.Select(sink => new AudioDeviceInfo(sink.Name, sink.Description))]
        : [.. Sources.Where(source => !source.IsMonitor).Select(source => new AudioDeviceInfo(source.Name, source.Description))];

    /// <summary>The name to show for a device: its description, else its name.</summary>
    public static string ShownName(string name, string? description) => string.IsNullOrWhiteSpace(description) ? name : description;
}

/// <summary>What the Linux capture cannot do, said before anything is opened.</summary>
internal static class PulseCaptureRules
{
    /// <summary>
    /// Why a request cannot be opened, or null. The engine asks first to leave this app's sounds out, and opens an ordinary
    /// capture when that throws, reporting it as such: the monitor of an output carries everything the output plays, and the
    /// server has no way to leave one application out of it. This one is for a factory without the preview's silence.
    /// </summary>
    public static Exception? Refuse(CaptureRequest request) => Refuse(request, canSilence: false);

    /// <summary>
    /// Why a request cannot be opened, or null. With <paramref name="canSilence"/>, the app's sounds can be left out of the PC's
    /// sound: its only sound is the preview, and the capture writes silence while it plays. The microphone has nothing to
    /// leave out. One application is captured as the PC's sound, as the engine asks it, and never holds this app's sounds:
    /// there is nothing to leave out of it, and the engine never asks to.
    /// </summary>
    public static Exception? Refuse(CaptureRequest request, bool canSilence)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TargetProcessId is not null)
        {
            if (request.Flow != AudioFlow.System)
            {
                return new PlatformNotSupportedException("An application is captured as the PC's sound, not as a microphone.");
            }

            return request.ExcludeOwnProcess
                ? new PlatformNotSupportedException("The capture of one application has none of this app's sounds to leave out.")
                : null;
        }

        if (request.ExcludeOwnProcess && !(canSilence && request.Flow == AudioFlow.System))
        {
            return new PlatformNotSupportedException("The monitor of an output cannot leave one application's sounds out.");
        }

        return null;
    }

    /// <summary>
    /// The silence a capture reads: the PC's sound asked to leave the app's sounds out, and only it. The microphone keeps
    /// what it hears, and an output chosen by name is captured as it is, as on Windows.
    /// </summary>
    public static PulsePlaybackSilence? SilenceFor(CaptureRequest request, PulsePlaybackSilence? silence) =>
        request.Flow == AudioFlow.System && request.ExcludeOwnProcess && request.TargetProcessId is null ? silence : null;
    /// <summary>The silence a capture of this flow reads: the PC's sound only, whoever opens it. The microphone keeps what it hears.</summary>
    public static PulsePlaybackSilence? SilenceOf(AudioFlow flow, PulsePlaybackSilence? silence) => flow == AudioFlow.System ? silence : null;
}
