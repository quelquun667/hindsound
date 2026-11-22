using System.Diagnostics;
using System.Runtime.InteropServices;
using Hindsound.Audio;

namespace Hindsound.Mac.Audio.Capture;

// What the capture of the Mac decides without touching Core Audio: tested on every system.

/// <summary>AudioBuffer of Core Audio: one stream's interleaved channels.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioBuffer
{
    public uint NumberChannels;
    public uint DataByteSize;
    public nint Data;
}

/// <summary>AudioBufferList of Core Audio: a count, then that many <see cref="AudioBuffer"/> in a row.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioBufferList
{
    public uint NumberBuffers;
    public AudioBuffer First;
}

/// <summary>AudioTimeStamp of Core Audio: the sample time, the host time, and the flags that say which are valid (64 bytes).</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct AudioTimeStamp
{
    [FieldOffset(0)]
    public double SampleTime;

    [FieldOffset(8)]
    public ulong HostTime;

    [FieldOffset(56)]
    public uint Flags;
}

/// <summary>Reads the channels the engine wants out of what an IOProc receives.</summary>
internal static unsafe class HalBuffers
{
    /// <summary>The channels of all the buffers together.</summary>
    public static int TotalChannels(AudioBufferList* list)
    {
        var total = 0;
        for (var i = 0; i < (int)list->NumberBuffers; i++)
        {
            total += (int)(&list->First)[i].NumberChannels;
        }

        return total;
    }

    /// <summary>
    /// Copies <paramref name="count"/> channels, from channel <paramref name="first"/> counted across all the buffers in
    /// order, into <paramref name="destination"/>, interleaved. The aggregate of a tap gives its main device's channels
    /// first and the tap's last, in one buffer or several: the tap is read by its place, whatever the buffers are. A
    /// channel missing from the list is written as silence. Returns the frames copied: those of the shortest buffer, cut to
    /// what <paramref name="destination"/> holds.
    /// </summary>
    public static int Extract(AudioBufferList* list, int first, int count, Span<float> destination)
    {
        var frames = int.MaxValue;
        for (var i = 0; i < (int)list->NumberBuffers; i++)
        {
            var buffer = (&list->First)[i];
            if (buffer.NumberChannels > 0)
            {
                frames = Math.Min(frames, (int)(buffer.DataByteSize / (sizeof(float) * buffer.NumberChannels)));
            }
        }

        if (frames == int.MaxValue || count <= 0)
        {
            return 0;
        }

        frames = Math.Min(frames, destination.Length / count);
        destination[..(frames * count)].Clear();
        var channel = 0;
        for (var i = 0; i < (int)list->NumberBuffers; i++)
        {
            var buffer = (&list->First)[i];
            var channels = (int)buffer.NumberChannels;
            var data = (float*)buffer.Data;
            for (var c = 0; c < channels; c++, channel++)
            {
                var target = channel - first;
                if (target < 0 || target >= count || data == null)
                {
                    continue;
                }

                for (var f = 0; f < frames; f++)
                {
                    destination[(f * count) + target] = data[(f * channels) + c];
                }
            }
        }

        return frames;
    }
}

/// <summary>
/// The position and the stamp of each packet. The sample time of the HAL counts the device's frames: a jump means frames
/// were lost. The host time says when the first frame was captured, on mach_absolute_time; the engine's clock is the
/// Stopwatch, so the stamp is the engine's now less the packet's age, measured on the host clock.
/// </summary>
internal sealed class PacketStamper
{
    internal const long TicksPerSecond = 10_000_000;

    private readonly Func<long> _nowTicks;
    private readonly Func<ulong> _hostNow;
    private readonly double _nanosecondsPerHostTick;
    private double _origin = double.NaN;
    private long _next = -1;
    private long _counted;

    /// <param name="nowTicks">The engine's clock, in 100 ns ticks.</param>
    /// <param name="hostNow">mach_absolute_time.</param>
    /// <param name="nanosecondsPerHostTick">What a tick of the host clock is worth.</param>
    public PacketStamper(Func<long> nowTicks, Func<ulong> hostNow, double nanosecondsPerHostTick)
    {
        _nowTicks = nowTicks;
        _hostNow = hostNow;
        _nanosecondsPerHostTick = nanosecondsPerHostTick;
    }

    /// <summary>The Stopwatch in 100 ns ticks, as the engine's timeline reads it.</summary>
    public static long StopwatchTicks()
    {
        var counter = Stopwatch.GetTimestamp();
        var frequency = Stopwatch.Frequency;
        return (counter / frequency * TicksPerSecond) + (counter % frequency * TicksPerSecond / frequency);
    }

    public (long Position, long Qpc, PacketFlags Flags) Stamp(in AudioTimeStamp time, int frames)
    {
        var flags = PacketFlags.None;
        long position;
        if ((time.Flags & 1) != 0)
        {
            if (double.IsNaN(_origin))
            {
                _origin = time.SampleTime;
            }

            position = (long)Math.Round(time.SampleTime - _origin);
            if (_next >= 0 && Math.Abs(position - _next) > 1)
            {
                flags |= PacketFlags.DataDiscontinuity;
            }
        }
        else
        {
            position = _counted;
        }

        _next = position + frames;
        _counted += frames;

        long qpc = 0;
        if ((time.Flags & 2) != 0 && time.HostTime != 0)
        {
            var ageHostTicks = (double)(long)(_hostNow() - time.HostTime);
            qpc = _nowTicks() - (long)(ageHostTicks * _nanosecondsPerHostTick / 100);
        }
        else
        {
            flags |= PacketFlags.TimestampError;
        }

        return (position, qpc, flags);
    }
}

/// <summary>
/// Notices a capture that has given only exact zeros since it opened. macOS gives that, and nothing else, to an app whose
/// permission was refused; a PC that plays nothing gives it too, so it is a reason to ask the permission, not a verdict.
/// </summary>
internal sealed class ExactZeroWatch(int framesLimit)
{
    private long _frames;
    private bool _done;

    /// <summary>True once, when <paramref name="framesLimit"/> frames went by since the start with every sample at exactly 0.</summary>
    public bool Feed(ReadOnlySpan<float> samples, int frames)
    {
        if (_done)
        {
            return false;
        }

        foreach (var sample in samples)
        {
            if (sample != 0f)
            {
                _done = true;
                return false;
            }
        }

        _frames += frames;
        if (_frames < framesLimit)
        {
            return false;
        }

        _done = true;
        return true;
    }
}

/// <summary>What TCC says of a permission, from TCCAccessPreflight.</summary>
internal enum PermissionState
{
    Granted = 0,
    Denied = 1,
    Undecided = 2,
    Unknown = -1,
}

internal static class MacCaptureRules
{
    /// <summary>How long a capture may give only exact zeros, from its opening, before the permission is asked.</summary>
    public const double ZeroSeconds = 2.0;

    /// <summary>The TCC service of a side: the sound of the Mac (a tap) or the microphone.</summary>
    public static string ServiceOf(AudioFlow flow) => flow == AudioFlow.System ? "kTCCServiceAudioCapture" : "kTCCServiceMicrophone";

    public static PermissionState StateOf(int preflight) => preflight is >= 0 and <= 2 ? (PermissionState)preflight : PermissionState.Unknown;

    /// <summary>What the log and the error of a refused capture say.</summary>
    public static string DenialText(AudioFlow flow) => flow == AudioFlow.System
        ? "macOS refuses this app the sound of the Mac: only exact zeros since the capture opened, and the permission is denied. "
            + "Allow Hindsound in System Settings > Privacy & Security > Screen & System Audio Recording."
        : "macOS refuses this app the microphone: only exact zeros since the capture opened, and the permission is denied. "
            + "Allow Hindsound in System Settings > Privacy & Security > Microphone.";

    /// <summary>
    /// What the log says when the capture gives only exact zeros and macOS has not decided the permission yet (neither granted
    /// nor denied): the permission, and where to give it. Not an error: the capture goes on, and plays as soon as the permission is given.
    /// </summary>
    public static string PendingText(AudioFlow flow) => flow == AudioFlow.System
        ? "macOS has not given this app the sound of the Mac yet: only exact zeros since the capture opened, and the permission is not granted or denied yet. "
            + "Allow Hindsound in System Settings > Privacy & Security > Screen & System Audio Recording."
        : "macOS has not given this app the microphone yet: only exact zeros since the capture opened, and the permission is not granted or denied yet. "
            + "Allow Hindsound in System Settings > Privacy & Security > Microphone.";

    /// <summary>What the log says of a capture of exact zeros whose permission is neither denied nor undecided.</summary>
    public static string ZerosText(PermissionState state, AudioFlow flow) =>
        $"only exact zeros for {ZeroSeconds:0} s since the {(flow == AudioFlow.System ? "sound of the Mac" : "microphone")} opened; the permission reads {state}: nothing plays";

    /// <summary>
    /// What a capture does once <see cref="ExactZeroWatch"/> has seen only exact zeros, from the state of its permission: the line
    /// for the log and, when the permission is denied, the error that ends the capture. The watch says it once per capture, so
    /// this says it once too.
    /// </summary>
    public static (string Line, Exception? Failure) VerdictOnZeros(PermissionState state, AudioFlow flow)
    {
        switch (state)
        {
            case PermissionState.Denied:
                var denial = DenialText(flow);
                return ("capture: " + denial, new UnauthorizedAccessException(denial));
            case PermissionState.Undecided:
                return ("capture: " + PendingText(flow), null);
            default:
                return ("capture: " + ZerosText(state, flow), null);
        }
    }

    /// <summary>The words of the log for a capture that is opened.</summary>
    public static string Describe(CaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.TargetProcessId is { } target
            ? $"the application {request.TargetName ?? "?"} (process {target}) and its children"
            : request.Flow == AudioFlow.Mic ? $"the microphone {request.DeviceId}"
            : $"the sound of the Mac on {request.DeviceId}{(request.ExcludeOwnProcess ? ", this app left out" : string.Empty)}";
    }

    /// <summary>Why a request cannot be opened, or null.</summary>
    public static Exception? Refuse(CaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TargetProcessId is not null)
        {
            if (request.Flow != AudioFlow.System)
            {
                return new PlatformNotSupportedException("An application is captured as the Mac's sound, not as a microphone.");
            }

            return request.ExcludeOwnProcess
                ? new PlatformNotSupportedException("The capture of one application has none of this app's sounds to leave out.")
                : null;
        }

        return request.ExcludeOwnProcess && request.Flow != AudioFlow.System
            ? new PlatformNotSupportedException("The microphone has none of this app's sounds to leave out.")
            : null;
    }

    /// <summary>
    /// The main device of a tap's aggregate: the Null Audio Device when there is one (the method the probe proved, which
    /// leaves every output alone), the output otherwise (no Mac has the Null Audio Device unless something installed it).
    /// </summary>
    public static string? MainDeviceOf(bool nullDevicePresent, string? outputUid, bool preferOutput) =>
        nullDevicePresent && !preferOutput ? CoreAudioNames.NullDeviceUid : outputUid;

    /// <summary>The process and its descendants, by the parent each one names. A process that names itself has no parent.</summary>
    public static HashSet<int> FamilyOf(IReadOnlyList<ProcessEntry> processes, int target)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var children = new Dictionary<int, List<int>>();
        foreach (var process in processes)
        {
            if (process.ParentId == process.Id)
            {
                continue;
            }

            if (!children.TryGetValue(process.ParentId, out var list))
            {
                children[process.ParentId] = list = [];
            }

            list.Add(process.Id);
        }

        var family = new HashSet<int> { target };
        var queue = new Queue<int>([target]);
        while (queue.Count > 0)
        {
            if (children.TryGetValue(queue.Dequeue(), out var list))
            {
                foreach (var child in list)
                {
                    if (family.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return family;
    }

    /// <summary>The audio objects of the processes of <paramref name="family"/>, sorted.</summary>
    public static uint[] ObjectsOf(IEnumerable<(uint Object, int Pid)> audioProcesses, IReadOnlySet<int> family) =>
        [.. audioProcesses.Where(process => family.Contains(process.Pid)).Select(process => process.Object).Order()];
}

/// <summary>Names of Core Audio that the pure logic needs too.</summary>
internal static class CoreAudioNames
{
    public const string NullDeviceUid = "NullAudioDevice_UID";
}

/// <summary>What a stretch of captured sound holds, as the probe of M0 measured it: the peak, the samples that are not exactly 0, and the amplitude at 1000 and 440 Hz and the dominant frequency of the first channel.</summary>
internal sealed record SignalMeasure(long Frames, long NonZero, double PeakDb, double DominantHz, double A1000Db, double A440Db)
{
    private const int MaxAnalysed = 8192;

    /// <summary>Every sample at exactly 0: what macOS gives an app whose permission is refused, and a tap of a silent process.</summary>
    public bool ExactZero => Frames > 0 && NonZero == 0;

    /// <summary>A clear 1 kHz: the tone the CI plays, at about -6 dBFS.</summary>
    public bool Tone1000 => Frames > 0 && Math.Abs(DominantHz - 1000) <= 10 && A1000Db > -20;

    public string Verdict => Frames == 0 ? "NO_DATA" : NonZero == 0 ? "SILENCE_EXACT_ZERO" : PeakDb < -60 ? "SILENCE_NEAR_ZERO" : "SIGNAL";

    public static SignalMeasure Of(ReadOnlySpan<float> interleaved, int channels, int rate)
    {
        var frames = channels > 0 ? interleaved.Length / channels : 0;
        long nonZero = 0;
        var peak = 0.0;
        foreach (var sample in interleaved)
        {
            if (sample != 0f)
            {
                nonZero++;
            }

            peak = Math.Max(peak, Math.Abs(sample));
        }

        var first = new float[Math.Min(frames, MaxAnalysed)];
        for (var i = 0; i < first.Length; i++)
        {
            first[i] = interleaved[i * channels];
        }

        double dominant = 0, best = 0;
        if (first.Length >= 1024)
        {
            for (double frequency = 50; frequency <= 6000; frequency += 5)
            {
                var amplitude = Goertzel(first, frequency, rate);
                if (amplitude > best)
                {
                    best = amplitude;
                    dominant = frequency;
                }
            }
        }

        return new SignalMeasure(frames, nonZero, Db(peak), dominant, Db(Goertzel(first, 1000, rate)), Db(Goertzel(first, 440, rate)));
    }

    public override string ToString() => FormattableString.Invariant(
        $"{Verdict} frames={Frames} nonzero={NonZero} peak={PeakDb:0.0}dBFS dominant={DominantHz:0}Hz a1000={A1000Db:0.0}dBFS a440={A440Db:0.0}dBFS");

    /// <summary>The amplitude of <paramref name="frequency"/> in <paramref name="samples"/>: 0.5 for a sine of amplitude 0.5.</summary>
    public static double Goertzel(ReadOnlySpan<float> samples, double frequency, int rate)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / rate), s1 = 0, s2 = 0;
        foreach (var sample in samples)
        {
            var s0 = sample + (coefficient * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        var power = (s1 * s1) + (s2 * s2) - (coefficient * s1 * s2);
        return 2 * Math.Sqrt(Math.Max(power, 0)) / samples.Length;
    }

    private static double Db(double amplitude) => amplitude > 0 ? 20 * Math.Log10(amplitude) : -999;
}

/// <summary><c>--prove-capture folder [--denied]</c>, for the CI.</summary>
internal static class MacCaptureProofOptions
{
    public const string Option = "--prove-capture";
    public const string DeniedOption = "--denied";

    /// <summary>The folder after <see cref="Option"/> and whether the permission is expected refused, or null when it was not asked.</summary>
    public static (string Folder, bool Denied)? Read(string[] args)
    {
        var at = Array.IndexOf(args, Option);
        return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal)
            ? (args[at + 1], args.Contains(DeniedOption))
            : null;
    }
}
