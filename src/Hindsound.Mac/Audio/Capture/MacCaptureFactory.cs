using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Audio.Capture;

/// <summary>
/// The capture of the Mac, through Core Audio, by the method the probe of M0 proved on the runner: a process tap read by a
/// private aggregate device whose main device is the Null Audio Device (or the output, on a Mac without it), through an
/// IOProc. The sound of the PC is a global stereo tap that leaves this app out when asked; one application is a tap of its
/// processes and their children, followed every second; the microphone is an IOProc on the input.
/// <para>
/// The device ids are the UIDs of Core Audio. The sound of the PC resolves to the default output: the global tap does not
/// depend on it, but a new output (a headset plugged in) is a new id, and the engine opens the capture again, aggregate and
/// all. An output chosen by name is tapped alone.
/// </para>
/// <para>
/// Opening asks macOS for the permission (Screen &amp; System Audio Recording for a tap, Microphone for an input) and, when it
/// is not decided yet, waits for the person: it runs on a thread of the pool, and the engine stops waiting after its time
/// limit and takes the capture if it comes later.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacCaptureFactory : ICaptureSourceFactory
{
    private readonly Action<string> _log;
    private readonly IProcessSnapshot _processes;
    private readonly bool _outputAsMain;
    private readonly bool _deviceTapAlways;
    private readonly Func<AudioFlow, PermissionState> _permission;

    /// <param name="log">The log of the run.</param>
    /// <param name="processes">The process list, where the children of an application are found.</param>
    public MacCaptureFactory(Action<string> log, IProcessSnapshot processes)
        : this(log, processes, outputAsMain: false, deviceTapAlways: false, permission: null)
    {
    }

    /// <param name="log">The log of the run.</param>
    /// <param name="processes">The process list, where the children of an application are found.</param>
    /// <param name="outputAsMain">The output as the aggregate's main device even where the Null Audio Device is: the method of a Mac without it, which the CI proves this way.</param>
    /// <param name="deviceTapAlways">Tap the output resolved even when it is the default one: the path of an output chosen by name, which the CI proves this way.</param>
    /// <param name="permission">Reads the permission of a side; TCC's preflight if null.</param>
    internal MacCaptureFactory(Action<string> log, IProcessSnapshot processes, bool outputAsMain, bool deviceTapAlways, Func<AudioFlow, PermissionState>? permission)
    {
        _log = log;
        _processes = processes;
        _outputAsMain = outputAsMain;
        _deviceTapAlways = deviceTapAlways;
        _permission = permission ?? MacPermission.Preflight;
    }

    public string? ResolveDevice(AudioFlow flow, string? requestedId)
    {
        var scope = flow == AudioFlow.System ? CoreAudio.ScopeOutput : CoreAudio.ScopeInput;
        if (requestedId is not null)
        {
            var device = CoreAudio.DeviceOf(requestedId);
            return device != CoreAudio.Unknown && CoreAudio.StreamChannels(device, scope).Length > 0 ? requestedId : null;
        }

        var selector = flow == AudioFlow.System ? CoreAudio.DefaultOutputDevice : CoreAudio.DefaultInputDevice;
        if (!CoreAudio.TryGetUInt(CoreAudio.SystemObject, selector, out var current))
        {
            throw new IOException("Core Audio did not say which device is the default one.");
        }

        return current == CoreAudio.Unknown ? null : CoreAudio.GetString(current, CoreAudio.DeviceUid);
    }

    public Task<ICaptureSource> OpenAsync(CaptureRequest request)
    {
        if (MacCaptureRules.Refuse(request) is { } refusal)
        {
            return Task.FromException<ICaptureSource>(refusal);
        }

        return Task.Run<ICaptureSource>(() =>
        {
            var watch = Stopwatch.StartNew();
            var source = request.TargetProcessId is { } target ? OpenApp(target, request.TargetName)
                : request.Flow == AudioFlow.Mic ? OpenMic(request.DeviceId)
                : OpenSystem(request);
            _log($"capture: {Describe(request)} opened in {watch.ElapsedMilliseconds} ms, {source.Format.SampleRate} Hz, {source.Format.Channels} channels");
            return source;
        });
    }

    private static string Describe(CaptureRequest request) => request.TargetProcessId is { } target
        ? $"the application {request.TargetName ?? "?"} (process {target}) and its children"
        : request.Flow == AudioFlow.Mic ? $"the microphone {request.DeviceId}"
        : $"the sound of the PC on {request.DeviceId}{(request.ExcludeOwnProcess ? ", this app left out" : string.Empty)}";

    private ICaptureSource OpenMic(string uid)
    {
        var device = CoreAudio.DeviceOf(uid);
        var streams = CoreAudio.StreamChannels(device, CoreAudio.ScopeInput);
        if (device == CoreAudio.Unknown || streams.Length == 0)
        {
            throw new IOException($"No input has the id {uid}.");
        }

        return MacCaptureSource.Open(device, fromEnd: false, streams[0], AudioFlow.Mic, CoreAudio.GetString(device, CoreAudio.Name), _log, null, [device], _permission);
    }

    private ICaptureSource OpenSystem(CaptureRequest request)
    {
        uint[] excluded = [];
        if (request.ExcludeOwnProcess)
        {
            var self = CoreAudio.ProcessObjectOf(Environment.ProcessId);
            if (self == CoreAudio.Unknown)
            {
                throw new PlatformNotSupportedException("Core Audio has no object for this app yet: its sounds cannot be left out.");
            }

            excluded = [self];
        }

        var output = CoreAudio.DeviceOf(request.DeviceId);
        if (output == CoreAudio.Unknown)
        {
            throw new IOException($"No output has the id {request.DeviceId}.");
        }

        var isDefault = CoreAudio.TryGetUInt(CoreAudio.SystemObject, CoreAudio.DefaultOutputDevice, out var current) && current == output;
        var tapOneOutput = _deviceTapAlways || !isDefault;
        var description = MacTapParts.Description(global: true, excluded, tapOneOutput ? request.DeviceId : null);
        return OpenTap(description, request.DeviceId, AudioFlow.System, CoreAudio.GetString(output, CoreAudio.Name), null);
    }

    private ICaptureSource OpenApp(int target, string? name)
    {
        var follower = new MacAppFollower(target, _processes, _log);
        var description = MacTapParts.Description(global: false, follower.Objects());
        var outputUid = ResolveDevice(AudioFlow.System, null);
        return OpenTap(description, outputUid, AudioFlow.System, name, follower);
    }

    // Creates the tap from its description (released here), the aggregate that reads it, and the IOProc.
    private ICaptureSource OpenTap(nint description, string? outputUid, AudioFlow flow, string? name, MacAppFollower? follower)
    {
        var mount = new TapMount(follower);
        try
        {
            unsafe
            {
                uint tap = 0;
                CoreAudio.Check(CoreAudio.AudioHardwareCreateProcessTap(description, &tap), "a process tap");
                mount.Tap = tap;
                var tapSays = MacTapParts.Summary(description);
                var channels = CoreAudio.TapChannels(tap, 2);
                var tapUid = CoreAudio.GetString(tap, CoreAudio.TapUid) ?? MacTapParts.UuidOf(description)
                    ?? throw new IOException("The tap has no UID.");

                var nullPresent = CoreAudio.DeviceOf(CoreAudioNames.NullDeviceUid) != CoreAudio.Unknown;
                var mainUid = MacCaptureRules.MainDeviceOf(nullPresent, outputUid, _outputAsMain);
                var dictionary = MacTapParts.Aggregate(tapUid, mainUid);
                uint aggregate = 0;
                try
                {
                    CoreAudio.Check(CoreAudio.AudioHardwareCreateAggregateDevice(dictionary, &aggregate), "a private aggregate device");
                }
                finally
                {
                    ObjC.Release(dictionary);
                }

                mount.Aggregate = aggregate;
                follower?.Attach(tap);
                var main = mainUid is null || mainUid == CoreAudioNames.NullDeviceUid ? CoreAudio.Unknown : CoreAudio.DeviceOf(mainUid);
                uint[] watched = main == CoreAudio.Unknown ? [aggregate] : [aggregate, main];
                _log($"capture: tap {tap} ({tapUid}; {tapSays}; {channels} channels) in the aggregate {aggregate}, main device {mainUid ?? "none"}");
                return MacCaptureSource.Open(aggregate, fromEnd: true, channels, flow, name, _log, mount, watched, _permission);
            }
        }
        catch
        {
            mount.Dispose();
            throw;
        }
        finally
        {
            ObjC.Release(description);
        }
    }

    /// <summary>The tap and its aggregate, destroyed together: the aggregate first, which reads the tap.</summary>
    private sealed class TapMount(MacAppFollower? follower) : IDisposable
    {
        private int _disposed;

        public uint Tap { get; set; }

        public uint Aggregate { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            follower?.Dispose();
            if (Aggregate != CoreAudio.Unknown)
            {
                CoreAudio.AudioHardwareDestroyAggregateDevice(Aggregate);
            }

            if (Tap != CoreAudio.Unknown)
            {
                CoreAudio.AudioHardwareDestroyProcessTap(Tap);
            }
        }
    }
}

/// <summary>
/// Keeps the tap of one application on its processes that have sound: the application and its children, from the process
/// list, matched to the HAL's audio processes every second. A process that starts playing is added to the tap, one that
/// ends leaves it, by setting the tap's description again. An application with no sound yet has a tap of nothing, which
/// gives silence until it plays. The engine notices by itself when the application closes.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacAppFollower : IDisposable
{
    private const int PeriodMs = 1000;

    private readonly int _target;
    private readonly IProcessSnapshot _processes;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private Timer? _timer;
    private uint _tap;
    private uint[] _current = [];
    private bool _disposed;

    public MacAppFollower(int target, IProcessSnapshot processes, Action<string> log)
    {
        _target = target;
        _processes = processes;
        _log = log;
    }

    /// <summary>The audio objects of the application's processes now, kept as the tap's first ones.</summary>
    public uint[] Objects()
    {
        var family = MacCaptureRules.FamilyOf(_processes.Take(), _target);
        _current = MacCaptureRules.ObjectsOf(CoreAudio.ProcessObjects(), family);
        _log($"capture: the application {_target} has {family.Count} processes, {_current.Length} with sound");
        return _current;
    }

    /// <summary>Starts following, once the tap exists.</summary>
    public void Attach(uint tap)
    {
        lock (_gate)
        {
            _tap = tap;
            _timer = new Timer(_ => Refresh(), null, PeriodMs, PeriodMs);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
        }
    }

    private void Refresh()
    {
        try
        {
            var family = MacCaptureRules.FamilyOf(_processes.Take(), _target);
            var objects = MacCaptureRules.ObjectsOf(CoreAudio.ProcessObjects(), family);
            lock (_gate)
            {
                if (_disposed || objects.SequenceEqual(_current))
                {
                    return;
                }

                var address = new CoreAudio.PropertyAddress(CoreAudio.TapDescription, CoreAudio.ScopeGlobal);
                nint description = 0;
                var size = (uint)sizeof(nint);
                CoreAudio.Check(CoreAudio.AudioObjectGetPropertyData(_tap, &address, 0, null, &size, &description), "the tap's description");
                try
                {
                    MacTapParts.SetProcesses(description, objects);
                    CoreAudio.Check(CoreAudio.AudioObjectSetPropertyData(_tap, &address, 0, null, (uint)sizeof(nint), &description), "a new list of processes for the tap");
                }
                finally
                {
                    ObjC.Release(description);
                }

                _log($"capture: the application {_target} now has {objects.Length} processes with sound (objects {string.Join(' ', objects)})");
                _current = objects;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log("capture: the processes of the application could not be followed: " + exception.Message);
        }
    }
}

/// <summary>Reads the permissions of the capture from TCC, without asking: TCCAccessPreflight, as AudioCap and the probe read it.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacPermission
{
    private static readonly Lazy<nint> Function = new(() =>
        NativeLibrary.TryLoad("/System/Library/PrivateFrameworks/TCC.framework/Versions/A/TCC", out var tcc)
        && NativeLibrary.TryGetExport(tcc, "TCCAccessPreflight", out var function) ? function : 0);

    public static PermissionState Preflight(AudioFlow flow)
    {
        var function = Function.Value;
        if (function == 0)
        {
            return PermissionState.Unknown;
        }

        var service = ObjC.NSString(MacCaptureRules.ServiceOf(flow));
        try
        {
            return MacCaptureRules.StateOf(((delegate* unmanaged<nint, nint, int>)function)(service, 0));
        }
        finally
        {
            ObjC.Release(service);
        }
    }
}
