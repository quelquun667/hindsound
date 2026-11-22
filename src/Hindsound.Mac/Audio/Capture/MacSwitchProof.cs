using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Mac.Audio.Devices;

namespace Hindsound.Mac.Audio.Capture;

/// <summary>
/// <c>--prove-switch folder</c>, for the CI: the default output changed under a running capture of the sound of the Mac, as
/// the app runs it with its output setting on the default one. The real engine, the real factory and the real watch of the
/// devices: BlackHole 2ch is made the default output, the capture starts on it and hears <c>afplay</c> play
/// <c>folder/tone.wav</c> (1 kHz, 2 s); then the Null Audio Device is made the default output, the watch sees it, the engine
/// opens the capture again on it (its line in the log, the name in the status), and <c>afplay</c> plays the same tone there:
/// the capture must hear it. The default output is set back to BlackHole 2ch at the end. The permission of audio capture is
/// written in TCC before: launched through <c>open</c>, TCC asks about the bundle. Writes <c>folder/switch.result</c>; 0 when
/// every check passes, 3 otherwise.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacSwitchProof
{
    public const string Option = "--prove-switch";

    private const int ExitFailed = 3;
    private const string FirstUid = "BlackHole2ch_UID";

    /// <summary>The folder after <see cref="Option"/>, or null when it was not asked.</summary>
    public static string? Folder(string[] args)
    {
        var at = Array.IndexOf(args, Option);
        return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : null;
    }

    public static int Run(string folder, Action<string> say)
    {
        void Log(string line) => say("switch proof: " + line);

        var pass = true;
        void Check(string what, bool good, string detail)
        {
            Log($"{what} {(good ? "ok" : "FAILED")} {detail}");
            pass &= good;
        }

        // The lines of the factory, as the journal of the app gets them: which capture opened, on which output.
        var opened = new ConcurrentQueue<string>();
        void FactoryLog(string line)
        {
            Log(line);
            if (line.StartsWith("capture: ", StringComparison.Ordinal) && line.Contains(" opened in ", StringComparison.Ordinal))
            {
                opened.Enqueue(line);
            }
        }

        var tone = Path.Combine(folder, "tone.wav");
        var first = CoreAudio.DeviceOf(FirstUid);
        var second = CoreAudio.DeviceOf(CoreAudioNames.NullDeviceUid);
        if (first == CoreAudio.Unknown || second == CoreAudio.Unknown)
        {
            Log($"result FAIL: BlackHole 2ch {(first == CoreAudio.Unknown ? "absent" : "present")}, Null Audio Device {(second == CoreAudio.Unknown ? "absent" : "present")}");
            File.WriteAllText(Path.Combine(folder, "switch.result"), "FAIL\n");
            return ExitFailed;
        }

        var firstName = CoreAudio.GetString(first, CoreAudio.Name);
        var secondName = CoreAudio.GetString(second, CoreAudio.Name);
        AudioEngine? engine = null;
        try
        {
            Check("default.first", SetDefaultOutput(first), $"default output {DefaultName()}");
            var processes = new MacProcessSnapshot();
            var watcher = new MacDeviceWatcher(Log);
            engine = new AudioEngine(new AudioEngineOptions(TimeSpan.FromSeconds(60), MicEnabled: false), new MacCaptureFactory(FactoryLog, processes), watcher, processes: processes);
            string? lastName = null;
            engine.StatusChanged += status =>
            {
                if (status.SystemDeviceName != lastName)
                {
                    lastName = status.SystemDeviceName;
                    Log($"status: {status.Activity}, the sound of the Mac on {lastName ?? "nothing"}, own sounds left out {status.OwnSoundsExcluded}");
                }
            };

            Check("engine.started", engine.StartAsync().Wait(TimeSpan.FromSeconds(30)), string.Empty);
            var on = WaitFor(() => engine.Status is { SystemCapturing: true } s && s.SystemDeviceName == firstName, 15);
            Check("capture.on_first", on >= 0, $"on {engine.Status.SystemDeviceName ?? "nothing"} after {on} ms");

            var before = PlayAndMeasure(Log, engine, tone, "first");
            Check("sound.on_first", before.Tone1000, before.ToString());

            var openedBefore = opened.Count;
            var watch = Stopwatch.StartNew();
            Check("default.second", SetDefaultOutput(second), $"default output {firstName} -> {DefaultName()}");
            var followed = WaitFor(() => opened.Count > openedBefore && engine.Status is { SystemCapturing: true } s && s.SystemDeviceName == secondName, 15);
            var line = opened.LastOrDefault() ?? "none";
            Check("capture.reopened_on_second", followed >= 0 && opened.Count > openedBefore && line.Contains(CoreAudioNames.NullDeviceUid, StringComparison.Ordinal),
                $"after {watch.ElapsedMilliseconds} ms, status on {engine.Status.SystemDeviceName ?? "nothing"}, last opening: {line}");

            Check("default.still_second", DefaultName() == secondName, $"default output {DefaultName()}");
            var after = PlayAndMeasure(Log, engine, tone, "second");
            Check("sound.on_second", after.Tone1000, after.ToString());
        }
        catch (Exception exception)
        {
            Check("error", false, exception.ToString());
        }
        finally
        {
            try
            {
                engine?.StopAsync().Wait(TimeSpan.FromSeconds(15));
            }
            catch (AggregateException exception)
            {
                Log("the engine did not stop cleanly: " + exception.InnerException?.Message);
            }

            engine?.Dispose();
            SetDefaultOutput(first);
            Log($"default output at the end: {DefaultName()}");
        }

        // Launched through Launch Services, the exit code does not reach the CI: the file does.
        Log("result " + (pass ? "pass" : "FAIL"));
        File.WriteAllText(Path.Combine(folder, "switch.result"), pass ? "pass\n" : "FAIL\n");
        return pass ? 0 : ExitFailed;
    }

    // afplay on the default output, then the last 2.5 s of the engine's buffer, of which the stretch the tone filled.
    private static SignalMeasure PlayAndMeasure(Action<string> log, AudioEngine engine, string tone, string where)
    {
        using var player = Process.Start("/usr/bin/afplay", [tone]) ?? throw new IOException("afplay did not start");
        log($"afplay on the {where} output: process {player.Id}");
        if (!player.WaitForExit(15_000))
        {
            player.Kill();
            throw new TimeoutException("afplay did not end in 15 s");
        }

        var snapshot = engine.Snapshot(TimeSpan.FromSeconds(2.5));
        var channels = InternalFormat.SystemChannels;
        var frames = snapshot.System.Length / channels;
        var from = Math.Min(frames, (int)(0.5 * InternalFormat.SampleRate));
        var to = Math.Min(frames, (int)(2.0 * InternalFormat.SampleRate));
        log($"buffer on the {where} output: {frames} frames in the last 2.5 s");
        return SignalMeasure.Of(snapshot.System.AsSpan(from * channels, Math.Max(0, to - from) * channels), channels, InternalFormat.SampleRate);
    }

    // Milliseconds until the condition held, or -1 after the limit.
    private static long WaitFor(Func<bool> condition, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (condition())
            {
                return watch.ElapsedMilliseconds;
            }

            Thread.Sleep(100);
        }

        return condition() ? watch.ElapsedMilliseconds : -1;
    }

    private static string DefaultName() =>
        CoreAudio.GetString(CoreAudio.GetUInt(CoreAudio.SystemObject, CoreAudio.DefaultOutputDevice), CoreAudio.Name) ?? "none";

    // kAudioHardwarePropertyDefaultOutputDevice, as SwitchAudioSource sets it.
    private static bool SetDefaultOutput(uint device)
    {
        var address = new CoreAudio.PropertyAddress(CoreAudio.DefaultOutputDevice, CoreAudio.ScopeGlobal);
        return CoreAudio.AudioObjectSetPropertyData(CoreAudio.SystemObject, &address, 0, null, sizeof(uint), &device) == 0;
    }
}
