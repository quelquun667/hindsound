using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;

namespace Hindsound.Mac.Audio.Capture;

/// <summary>
/// <c>--prove-capture folder</c>, for the CI: every capture through the real <see cref="MacCaptureFactory"/>, with real
/// sound. The folder holds <c>tone.wav</c> (1 kHz at -6 dBFS) and <c>silence.wav</c>; the runner has BlackHole 2ch as its
/// default output and input, and the permissions written in TCC before anything opened a device. The app plays a 440 Hz tone
/// of its own, starts <c>afplay</c> on each file, and measures each capture (level in dBFS, dominant frequency, exact
/// zeros): the sound of the PC with this app left out (the 1 kHz without the 440 Hz), and without (both), with the output
/// as the aggregate's main device, one output alone; one application (the silent <c>afplay</c>: exact zeros; a shell whose
/// child starts <c>afplay</c> 2 s after the tap opened: zeros, then the 1 kHz); the microphone (BlackHole gives back the 1 kHz).
/// With <c>--denied</c> (the permissions written refused), the sound of the PC and the microphone give exact zeros and the
/// capture ends saying the permission is refused. 0 when every case passes, 3 otherwise.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacCaptureProof
{
    private const int ExitFailed = 3;
    private const double CaseSeconds = 3.5;

    private static double _phase;
    private static double _step;

    public static int Run(string folder, bool denied, Action<string> say)
    {
        void Log(string line) => say("capture proof: " + line);

        var tone = Path.Combine(folder, "tone.wav");
        var silence = Path.Combine(folder, "silence.wav");
        var processes = new PsProcessSnapshot();
        var factory = new MacCaptureFactory(Log, processes);
        var children = new List<Process>();
        var pass = true;
        nint selfTone = 0;
        uint toneDevice = 0;
        try
        {
            Log($"self audio object before any sound: {CoreAudio.ProcessObjectOf(Environment.ProcessId)}");
            var output = factory.ResolveDevice(AudioFlow.System, null);
            var input = factory.ResolveDevice(AudioFlow.Mic, null);
            var nullPresent = CoreAudio.DeviceOf(CoreAudioNames.NullDeviceUid) != CoreAudio.Unknown;
            Log($"output {output ?? "none"}, input {input ?? "none"}, Null Audio Device {(nullPresent ? "present" : "absent")}, "
                + $"permissions: audio capture {MacPermission.Preflight(AudioFlow.System)}, microphone {MacPermission.Preflight(AudioFlow.Mic)}");
            if (output is null || input is null)
            {
                Log("result FAIL: no default output or input");
                return ExitFailed;
            }

            if (!denied)
            {
                (selfTone, toneDevice) = StartSelfTone(Log);
            }

            children.Add(Start(Log, "/usr/bin/afplay", tone));
            var silent = Start(Log, "/usr/bin/afplay", silence);
            children.Add(silent);
            Thread.Sleep(1500);
            Log($"self audio object with its tone playing: {CoreAudio.ProcessObjectOf(Environment.ProcessId)}");

            if (denied)
            {
                pass &= DeniedCase(Log, "system_denied", factory, new CaptureRequest(AudioFlow.System, output, false));
                pass &= DeniedCase(Log, "mic_denied", factory, new CaptureRequest(AudioFlow.Mic, input, false));
            }
            else
            {
                pass &= Case(Log, "system_excl", factory, new CaptureRequest(AudioFlow.System, output, true), m => m.Tone1000 && m.A440Db < -50);
                pass &= Case(Log, "system_plain", factory, new CaptureRequest(AudioFlow.System, output, false), m => m.A1000Db > -20 && m.A440Db > -30);
                var outputMain = new MacCaptureFactory(Log, processes, outputAsMain: true, deviceTapAlways: false, permission: null);
                pass &= Case(Log, "system_excl_output_main", outputMain, new CaptureRequest(AudioFlow.System, output, true), m => m.Tone1000 && m.A440Db < -50);
                var oneOutput = new MacCaptureFactory(Log, processes, outputAsMain: false, deviceTapAlways: true, permission: null);
                pass &= Case(Log, "system_one_output", oneOutput, new CaptureRequest(AudioFlow.System, output, false), m => m.Tone1000);
                if (nullPresent)
                {
                    pass &= Case(Log, "system_other_output", factory, new CaptureRequest(AudioFlow.System, CoreAudioNames.NullDeviceUid, false), m => m.ExactZero);
                }

                pass &= Case(Log, "app_silent", factory, new CaptureRequest(AudioFlow.System, output, false, silent.Id, "afplay"), m => m.ExactZero);
                pass &= FamilyCase(Log, factory, output, tone, children);
                pass &= Case(Log, "mic", factory, new CaptureRequest(AudioFlow.Mic, input, false), m => m.Verdict == "SIGNAL" && m.A1000Db > -20);
            }
        }
        catch (Exception exception)
        {
            Log("error: " + exception);
            pass = false;
        }
        finally
        {
            if (selfTone != 0)
            {
                CoreAudio.AudioDeviceStop(toneDevice, selfTone);
                CoreAudio.AudioDeviceDestroyIOProcID(toneDevice, selfTone);
            }

            foreach (var child in children)
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
            }
        }

        // Launched through Launch Services (its own responsible process for TCC), the app's exit code does not reach the CI: the file does.
        Log("result " + (pass ? "pass" : "FAIL"));
        File.WriteAllText(Path.Combine(folder, denied ? "denied.result" : "granted.result"), pass ? "pass\n" : "FAIL\n");
        return pass ? 0 : ExitFailed;
    }

    private static bool Case(Action<string> log, string name, ICaptureSourceFactory factory, CaptureRequest request, Func<SignalMeasure, bool> expected)
    {
        var run = Capture(log, name, factory, request, CaseSeconds);
        if (run is null)
        {
            return false;
        }

        var measure = run.Measure(0.5, CaseSeconds);
        var received = run.Received(0.5, CaseSeconds);
        var ok = expected(measure) && run.Stopped is null && received > 0;
        log($"{name} {measure} received={received} {run.Facts} stopped={run.Stopped?.Message ?? "no"} result {(ok ? "pass" : "FAIL")}");
        return ok;
    }

    // One application whose sound comes from a child that starts after the tap opened: the tap must follow it.
    private static bool FamilyCase(Action<string> log, ICaptureSourceFactory factory, string output, string tone, List<Process> children)
    {
        var parent = Start(log, "/bin/bash", "-c", $"sleep 2; /usr/bin/afplay '{tone}'; sleep 30");
        children.Add(parent);
        var run = Capture(log, "app_family", factory, new CaptureRequest(AudioFlow.System, output, false, parent.Id, "bash"), 6.5);
        if (run is null)
        {
            return false;
        }

        // Placed by their dates, as the Core does: while the application has no process with sound, the tap may send no
        // packet at all (run 44941099326: 2.2 s without one), and that time is silence in a clip.
        var before = run.Measure(0.2, 1.5);
        var after = run.Measure(4.0, 6.5);
        var ok = before.ExactZero && after.Tone1000 && run.Stopped is null;
        log($"app_family before_child {before} received={run.Received(0.2, 1.5)} after_child {after} received={run.Received(4.0, 6.5)} {run.Facts} result {(ok ? "pass" : "FAIL")}");
        return ok;
    }

    private static bool DeniedCase(Action<string> log, string name, ICaptureSourceFactory factory, CaptureRequest request)
    {
        var run = Capture(log, name, factory, request, 6);
        if (run is null)
        {
            return false;
        }

        var measure = run.Measure(0, 6);
        var received = run.Received(0, 6);
        var ok = measure.ExactZero && received > 0 && run.Stopped is UnauthorizedAccessException;
        log($"{name} {measure} received={received} {run.Facts} stopped={run.Stopped?.GetType().Name ?? "no"}: {run.Stopped?.Message} result {(ok ? "pass" : "FAIL")}");
        return ok;
    }

    private static Recording? Capture(Action<string> log, string name, ICaptureSourceFactory factory, CaptureRequest request, double seconds)
    {
        var watch = Stopwatch.StartNew();
        var opening = factory.OpenAsync(request);
        try
        {
            if (!opening.Wait(TimeSpan.FromSeconds(15)))
            {
                log($"{name} STUCK: the opening did not end in 15 s (a permission not decided makes coreaudiod wait) result FAIL");
                return null;
            }
        }
        catch (AggregateException exception)
        {
            log($"{name} ERROR {exception.InnerException?.GetType().Name}: {exception.InnerException?.Message} result FAIL");
            return null;
        }

        using var source = opening.Result;
        var openMs = watch.ElapsedMilliseconds;
        var run = new Recording(source.Format.Channels, source.Format.SampleRate, seconds, openMs);
        source.PacketAvailable += run.OnPacket;
        source.Stopped += exception => run.Stopped ??= exception ?? new OperationCanceledException("stopped without an error");
        run.StartTicks = PacketStamper.StopwatchTicks();
        source.Start();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        source.Stop();
        Thread.Sleep(200);
        if (run.Stopped is OperationCanceledException)
        {
            run.Stopped = null;
        }

        return run;
    }

    private static Process Start(Action<string> log, string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new IOException("Could not start " + file);
        log($"started {Path.GetFileName(file)} {string.Join(' ', arguments.Select(Path.GetFileName))}: process {process.Id}");
        return process;
    }

    // The app's own sound: 440 Hz at -6 dBFS on the default output, through an IOProc of its own.
    private static (nint Proc, uint Device) StartSelfTone(Action<string> log)
    {
        var device = CoreAudio.GetUInt(CoreAudio.SystemObject, CoreAudio.DefaultOutputDevice);
        var rate = CoreAudio.GetDouble(device, CoreAudio.NominalSampleRate);
        _step = 2 * Math.PI * 440 / (rate > 0 ? rate : 48000);
        nint proc = 0;
        CoreAudio.Check(CoreAudio.AudioDeviceCreateIOProcID(device, (nint)(delegate* unmanaged[Cdecl]<uint, AudioTimeStamp*, AudioBufferList*, AudioTimeStamp*, AudioBufferList*, AudioTimeStamp*, nint, int>)&OnTone, 0, &proc), "the tone's IOProc");
        CoreAudio.Check(CoreAudio.AudioDeviceStart(device, proc), "the tone");
        log($"own tone: 440 Hz on device {device} at {rate} Hz");
        return (proc, device);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnTone(uint device, AudioTimeStamp* now, AudioBufferList* input, AudioTimeStamp* inputTime, AudioBufferList* output, AudioTimeStamp* outputTime, nint key)
    {
        if (output == null)
        {
            return 0;
        }

        var start = _phase;
        var end = start;
        for (var i = 0; i < (int)output->NumberBuffers; i++)
        {
            var buffer = (&output->First)[i];
            var channels = (int)Math.Max(buffer.NumberChannels, 1);
            var frames = (int)(buffer.DataByteSize / (sizeof(float) * channels));
            var data = (float*)buffer.Data;
            var phase = start;
            for (var f = 0; f < frames && data != null; f++, phase += _step)
            {
                var value = (float)(0.5 * Math.Sin(phase));
                for (var c = 0; c < channels; c++)
                {
                    data[(f * channels) + c] = value;
                }
            }

            end = phase;
        }

        _phase = end % (2 * Math.PI);
        return 0;
    }

    private sealed class Recording(int channels, int rate, double seconds, long openMs)
    {
        private readonly float[] _samples = new float[(int)(rate * channels * (seconds + 2))];
        private readonly Lock _gate = new();
        private readonly List<(long From, long To)> _received = [];
        private int _count;
        private long _packets;
        private long _discontinuities;
        private long _minAgeMs = long.MaxValue;
        private long _maxAgeMs = long.MinValue;

        public Exception? Stopped { get; set; }

        /// <summary>The stopwatch ticks when the capture started: where each packet goes, by its date.</summary>
        public long StartTicks { get; set; }

        public string Facts => string.Create(CultureInfo.InvariantCulture,
            $"rate={rate} channels={channels} opened_ms={openMs} packets={_packets} discontinuities={_discontinuities} stamp_age_ms={(_packets > 0 ? $"{_minAgeMs}..{_maxAgeMs}" : "none")}");

        public void OnPacket(ReadOnlySpan<byte> buffer, PacketFlags flags, long devicePosition, long qpcPosition)
        {
            var samples = MemoryMarshal.Cast<byte, float>(buffer);
            var age = qpcPosition > 0 ? (PacketStamper.StopwatchTicks() - qpcPosition) / 10_000 : long.MinValue;
            lock (_gate)
            {
                _packets++;
                if ((flags & PacketFlags.DataDiscontinuity) != 0)
                {
                    _discontinuities++;
                }

                if (age != long.MinValue)
                {
                    _minAgeMs = Math.Min(_minAgeMs, age);
                    _maxAgeMs = Math.Max(_maxAgeMs, age);
                }

                // By its date, as the Core lays packets on its timeline; one after the other when it has none.
                var frames = samples.Length / channels;
                var at = qpcPosition > 0 && StartTicks > 0
                    ? (long)Math.Round((qpcPosition - StartTicks) * (double)rate / PacketStamper.TicksPerSecond)
                    : _count / channels;
                var skip = (int)Math.Clamp(-at, 0, frames);
                var first = Math.Max(at, 0) * channels;
                var take = (int)Math.Min((long)(frames - skip) * channels, _samples.Length - first);
                if (take > 0)
                {
                    samples.Slice(skip * channels, take).CopyTo(_samples.AsSpan((int)first));
                    _count = Math.Max(_count, (int)first + take);
                    _received.Add((first / channels, (first + take) / channels));
                }
            }
        }

        /// <summary>How many frames of the stretch from <paramref name="from"/> to <paramref name="to"/> seconds came in packets.</summary>
        public long Received(double from, double to)
        {
            lock (_gate)
            {
                var start = (long)(from * rate);
                var end = (long)(to * rate);
                return _received.Sum(r => Math.Max(0, Math.Min(r.To, end) - Math.Max(r.From, start)));
            }
        }

        /// <summary>The measure of the stretch from <paramref name="from"/> to <paramref name="to"/> seconds after the start, placed by date.</summary>
        public SignalMeasure Measure(double from, double to)
        {
            lock (_gate)
            {
                var start = Math.Min(_count, (int)(from * rate) * channels);
                var end = Math.Min(_count, (int)(to * rate) * channels);
                return SignalMeasure.Of(_samples.AsSpan(start, Math.Max(0, end - start)), channels, rate);
            }
        }
    }

    /// <summary>The process list from <c>ps</c>: enough for the proof, which runs before the app's own list is there.</summary>
    private sealed class PsProcessSnapshot : IProcessSnapshot
    {
        public IReadOnlyList<ProcessEntry> Take()
        {
            var info = new ProcessStartInfo("/bin/ps") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var argument in new[] { "-axo", "pid=,ppid=,comm=" })
            {
                info.ArgumentList.Add(argument);
            }

            using var ps = Process.Start(info) ?? throw new IOException("ps did not start");
            var list = new List<ProcessEntry>();
            string? line;
            while ((line = ps.StandardOutput.ReadLine()) is not null)
            {
                var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var pid) && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var parent))
                {
                    list.Add(new ProcessEntry(pid, parent, Path.GetFileName(parts[2])));
                }
            }

            ps.WaitForExit();
            return list;
        }
    }
}
