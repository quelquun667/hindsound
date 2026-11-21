using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Mac.Audio.Capture;

/// <summary>
/// One IOProc on a device: the private aggregate of a tap (the sound of the PC, or of one application), or an input (the
/// microphone). Packets come on the HAL's IO thread, which is the audio thread the engine expects, as 32-bit float at the
/// device's rate: the engine's converter resamples. A tap's channels are the last of the aggregate's (its main device's
/// come first); a microphone's are those of its first stream.
/// <para>
/// The device is watched while the capture runs: a device that goes away, or whose rate changes, ends the capture with an
/// error, and the engine opens what it then resolves. A capture that has given only exact zeros for
/// <see cref="MacCaptureRules.ZeroSeconds"/> since it opened asks TCC for its permission: refused, it ends with an
/// <see cref="UnauthorizedAccessException"/> that says where to allow it, and the engine shows the side as not capturing.
/// </para>
/// The callbacks find their source through a table, by a number: a callback that comes after Dispose finds nothing.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacCaptureSource : ICaptureSource
{
    private const int MaxFrames = 32_768;

    private static readonly ConcurrentDictionary<nint, MacCaptureSource> Live = new();
    private static long _lastKey;

    [ThreadStatic]
    private static bool _inCallback;

    private readonly uint _device;
    private readonly bool _fromEnd;
    private readonly int _channels;
    private readonly AudioFlow _flow;
    private readonly double _rate;
    private readonly Action<string> _log;
    private readonly IDisposable? _owner;
    private readonly uint[] _watched;
    private readonly Func<AudioFlow, PermissionState> _permission;
    private readonly PacketStamper _stamper;
    private readonly ExactZeroWatch _zeros;
    private readonly float[] _scratch;
    private readonly nint _key;
    private nint _proc;
    private int _ended;
    private int _disposed;

    private MacCaptureSource(uint device, bool fromEnd, int channels, double rate, AudioFlow flow, string? name, Action<string> log, IDisposable? owner, uint[] watched, Func<AudioFlow, PermissionState> permission)
    {
        _device = device;
        _fromEnd = fromEnd;
        _channels = channels;
        _flow = flow;
        _rate = rate;
        _log = log;
        _owner = owner;
        _watched = watched;
        _permission = permission;
        DeviceName = name;
        Format = WaveFormat.CreateIeeeFloatWaveFormat((int)Math.Round(rate), channels);
        _stamper = new PacketStamper(PacketStamper.StopwatchTicks, CoreAudio.HostTimeNow, CoreAudio.NanosecondsPerHostTick);
        _zeros = new ExactZeroWatch((int)(rate * MacCaptureRules.ZeroSeconds));
        _scratch = new float[MaxFrames * channels];
        _key = (nint)Interlocked.Increment(ref _lastKey);
    }

    public WaveFormat Format { get; }

    public string? DeviceName { get; }

    public event CapturePacketHandler? PacketAvailable;

    public event Action<Exception?>? Stopped;

    /// <summary>
    /// Makes the IOProc on <paramref name="device"/>, not started. This is where macOS asks for the permission when it is not
    /// decided yet, and waits for the person: never on the interface's thread. On failure, <paramref name="owner"/> is
    /// disposed.
    /// </summary>
    /// <param name="device">The aggregate of a tap, or an input.</param>
    /// <param name="fromEnd">The channels are the last ones (a tap), not the first ones (a microphone).</param>
    /// <param name="channels">How many.</param>
    /// <param name="flow">Which side, for the permission.</param>
    /// <param name="name">The name to show.</param>
    /// <param name="log">The log of the run.</param>
    /// <param name="owner">What this capture holds and frees with it: the tap and its aggregate.</param>
    /// <param name="watched">Devices whose loss ends the capture, the first being the one read.</param>
    /// <param name="permission">Reads the permission of a side.</param>
    public static MacCaptureSource Open(uint device, bool fromEnd, int channels, AudioFlow flow, string? name, Action<string> log, IDisposable? owner, uint[] watched, Func<AudioFlow, PermissionState> permission)
    {
        MacCaptureSource? source = null;
        try
        {
            var rate = CoreAudio.GetDouble(device, CoreAudio.NominalSampleRate);
            if (rate <= 0 || channels <= 0)
            {
                throw new IOException($"The device {device} has no usable rate ({rate}) or channels ({channels}).");
            }

            source = new MacCaptureSource(device, fromEnd, channels, rate, flow, name, log, owner, watched, permission);
            Live[source._key] = source;
            source.Listen(true);
            nint proc = 0;
            CoreAudio.Check(CoreAudio.AudioDeviceCreateIOProcID(device, (nint)(delegate* unmanaged[Cdecl]<uint, AudioTimeStamp*, AudioBufferList*, AudioTimeStamp*, AudioBufferList*, AudioTimeStamp*, nint, int>)&OnIO, source._key, &proc), "an IOProc");
            source._proc = proc;
            return source;
        }
        catch
        {
            if (source is not null)
            {
                source.Dispose();
            }
            else
            {
                owner?.Dispose();
            }

            throw;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _ended) != 0, this);
        CoreAudio.Check(CoreAudio.AudioDeviceStart(_device, _proc), "starting the capture");
    }

    public void Stop()
    {
        if (End())
        {
            RaiseStopped(null);
        }
    }

    public void Dispose()
    {
        // The IOProc cannot be destroyed from its own thread: Core Audio waits for the callback that asks.
        if (_inCallback)
        {
            _ = Task.Run(Dispose);
            return;
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        End();
        Live.TryRemove(_key, out _);
        Listen(false);
        if (_proc != 0)
        {
            CoreAudio.AudioDeviceDestroyIOProcID(_device, _proc);
            _proc = 0;
        }

        _owner?.Dispose();
    }

    // Stops the IOProc once. True for the call that did it.
    private bool End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return false;
        }

        if (_proc != 0)
        {
            CoreAudio.AudioDeviceStop(_device, _proc);
        }

        return true;
    }

    private void RaiseStopped(Exception? exception) => _ = Task.Run(() => Stopped?.Invoke(exception));

    private void Fail(Exception exception)
    {
        if (End())
        {
            RaiseStopped(exception);
        }
    }

    private void Listen(bool add)
    {
        foreach (var device in _watched)
        {
            foreach (var selector in new[] { CoreAudio.DeviceIsAlive, CoreAudio.NominalSampleRate })
            {
                var address = new CoreAudio.PropertyAddress(selector, CoreAudio.ScopeGlobal);
                var listener = (nint)(delegate* unmanaged[Cdecl]<uint, uint, CoreAudio.PropertyAddress*, nint, int>)&OnProperty;
                _ = add
                    ? CoreAudio.AudioObjectAddPropertyListener(device, &address, listener, _key)
                    : CoreAudio.AudioObjectRemovePropertyListener(device, &address, listener, _key);
            }
        }
    }

    private void Deliver(AudioBufferList* input, AudioTimeStamp* time)
    {
        if (Volatile.Read(ref _ended) != 0 || input == null || time == null)
        {
            return;
        }

        var first = _fromEnd ? HalBuffers.TotalChannels(input) - _channels : 0;
        if (first < 0)
        {
            // The tap is not in the aggregate's buffers yet.
            return;
        }

        var frames = HalBuffers.Extract(input, first, _channels, _scratch);
        if (frames == 0)
        {
            return;
        }

        var samples = _scratch.AsSpan(0, frames * _channels);
        if (_zeros.Feed(samples, frames))
        {
            _ = Task.Run(CheckPermission);
        }

        var (position, qpc, flags) = _stamper.Stamp(*time, frames);
        PacketAvailable?.Invoke(MemoryMarshal.AsBytes(samples), flags, position, qpc);
    }

    private void CheckPermission()
    {
        var state = _permission(_flow);
        if (state == PermissionState.Denied)
        {
            var text = MacCaptureRules.DenialText(_flow);
            _log("capture: " + text);
            Fail(new UnauthorizedAccessException(text));
            return;
        }

        _log($"capture: only exact zeros for {MacCaptureRules.ZeroSeconds:0} s since the {(_flow == AudioFlow.System ? "sound of the PC" : "microphone")} opened; "
            + $"the permission reads {state}: nothing plays");
    }

    private void OnChanged(uint device, uint selector)
    {
        if (Volatile.Read(ref _ended) != 0)
        {
            return;
        }

        if (selector == CoreAudio.DeviceIsAlive && CoreAudio.TryGetUInt(device, CoreAudio.DeviceIsAlive, out var alive) && alive == 0)
        {
            Fail(new IOException($"The device {DeviceName ?? device.ToString(System.Globalization.CultureInfo.InvariantCulture)} went away."));
        }
        else if (selector == CoreAudio.NominalSampleRate && device == _device && CoreAudio.GetDouble(device, CoreAudio.NominalSampleRate) is var rate && rate != _rate)
        {
            Fail(new IOException($"The device's rate went from {_rate} to {rate} Hz."));
        }
    }

    // The callbacks below run on threads of the HAL, called by C: nothing may be thrown out of them.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnIO(uint device, AudioTimeStamp* now, AudioBufferList* input, AudioTimeStamp* inputTime, AudioBufferList* output, AudioTimeStamp* outputTime, nint key)
    {
        if (!Live.TryGetValue(key, out var self))
        {
            return 0;
        }

        _inCallback = true;
        try
        {
            self.Deliver(input, inputTime);
        }
        catch (Exception exception)
        {
            // A listener that throws ends the capture, as on Windows and Linux.
            try
            {
                self.Fail(exception);
            }
            catch (Exception)
            {
                // Nothing more can be done from here.
            }
        }
        finally
        {
            _inCallback = false;
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnProperty(uint device, uint count, CoreAudio.PropertyAddress* addresses, nint key)
    {
        try
        {
            if (Live.TryGetValue(key, out var self))
            {
                for (var i = 0; i < (int)count; i++)
                {
                    self.OnChanged(device, addresses[i].Selector);
                }
            }
        }
        catch (Exception)
        {
            // The capture goes on; a device really gone stops giving packets, and the engine notices.
        }

        return 0;
    }
}
