using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Audio;

namespace Hindsound.Mac.Audio.Devices;

/// <summary>A device of the HAL as the settings need it.</summary>
/// <param name="Uid">kAudioDevicePropertyDeviceUID: the id kept in the settings, which lasts across restarts (an AudioDeviceID does not).</param>
/// <param name="Name">Its name, if it gives one.</param>
/// <param name="Inputs">Its streams of input.</param>
/// <param name="Outputs">Its streams of output.</param>
internal sealed record HalDevice(string? Uid, string? Name, int Inputs, int Outputs);

/// <summary>A client of the HAL: its pid, and whether it plays sound now.</summary>
internal sealed record HalProcess(int ProcessId, bool PlaysOutput, string? BundleId);

/// <summary>What the settings offer, from what the HAL says; no call to the system, so it is tested everywhere.</summary>
internal static class MacAudioLists
{
    /// <summary>The outputs (the PC sound) or the inputs (the microphone), in the order of the HAL; a device with no id is left out.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Devices(IEnumerable<HalDevice> devices, AudioFlow flow) =>
    [
        .. devices
            .Where(device => !string.IsNullOrEmpty(device.Uid) && (flow == AudioFlow.System ? device.Outputs : device.Inputs) > 0)
            .Select(device => new AudioDeviceInfo(device.Uid!, string.IsNullOrWhiteSpace(device.Name) ? device.Uid! : device.Name)),
    ];

    /// <summary>
    /// The applications that play now, one line each: the processes of one application share its name
    /// (<see cref="MacProcessNames"/>), and <see cref="AudioAppList.Build"/> groups them by it, leaving this app out.
    /// </summary>
    public static IReadOnlyList<AudioAppInfo> Apps(IEnumerable<HalProcess> clients, IReadOnlyList<ProcessEntry> processes, int ownProcessId) =>
        AudioAppList.Build(clients.Where(client => client.PlaysOutput).Select(client => (client.ProcessId, (string?)null)), processes, ownProcessId);
}

/// <summary>
/// The outputs and the microphones of the HAL, and the applications that play sound now, read afresh at each call. An
/// application is in the list while one of its processes does output (kAudioProcessPropertyIsRunningOutput, on the process
/// objects of the HAL, macOS 14): one that has stopped playing is not.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAudioChoices : IAudioChoices
{
    private readonly Action<string> _log;
    private readonly IProcessSnapshot _processes;

    public MacAudioChoices(Action<string> log, IProcessSnapshot? processes = null)
    {
        _log = log;
        _processes = processes ?? new MacProcessSnapshot();
    }

    public IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow)
    {
        try
        {
            return MacAudioLists.Devices(ReadDevices(), flow);
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
            return MacAudioLists.Apps(ReadClients(), _processes.Take(), Environment.ProcessId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log("the applications could not be listed: " + exception.GetType().Name + ": " + exception.Message);
            return [];
        }
    }

    /// <summary>Every device of the HAL, with its id, its name and its streams.</summary>
    public static IReadOnlyList<HalDevice> ReadDevices() =>
    [
        .. CoreAudioHal.ReadIds(HalCodes.SystemObject, HalCodes.Devices, HalCodes.ScopeGlobal).Select(id => new HalDevice(
            CoreAudioHal.ReadString(id, HalCodes.DeviceUid, HalCodes.ScopeGlobal),
            CoreAudioHal.ReadString(id, HalCodes.Name, HalCodes.ScopeGlobal),
            CoreAudioHal.StreamCount(id, HalCodes.ScopeInput),
            CoreAudioHal.StreamCount(id, HalCodes.ScopeOutput))),
    ];

    /// <summary>The name of the default output or input device, for a log; null when there is none.</summary>
    public static string? DefaultDeviceName(AudioFlow flow) =>
        CoreAudioHal.ReadUInt(HalCodes.SystemObject, flow == AudioFlow.System ? HalCodes.DefaultOutput : HalCodes.DefaultInput, HalCodes.ScopeGlobal) is { } id and not 0
            ? CoreAudioHal.ReadString(id, HalCodes.Name, HalCodes.ScopeGlobal)
            : null;

    /// <summary>Every client of the HAL, playing or not; a process gone between the list and its read is left out.</summary>
    public static IReadOnlyList<HalProcess> ReadClients()
    {
        var clients = new List<HalProcess>();
        foreach (var id in CoreAudioHal.ReadIds(HalCodes.SystemObject, HalCodes.ProcessObjects, HalCodes.ScopeGlobal))
        {
            if (CoreAudioHal.ReadUInt(id, HalCodes.ProcessId, HalCodes.ScopeGlobal) is { } pid and > 0)
            {
                var plays = CoreAudioHal.ReadUInt(id, HalCodes.ProcessRunsOutput, HalCodes.ScopeGlobal) is > 0;
                clients.Add(new HalProcess((int)pid, plays, CoreAudioHal.ReadString(id, HalCodes.ProcessBundleId, HalCodes.ScopeGlobal)));
            }
        }

        return clients;
    }
}
