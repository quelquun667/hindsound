using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The capture on a real sound server, the one the CI starts with null outputs: a tone of 1 kHz played on an output, or fed
/// to a microphone, is found in the memory of the engine. The tests make their own outputs and inputs and remove them after;
/// they run one after the other, as they change the server's defaults.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class PulseCaptureOnLinuxTests
{
    private const int Rate = 48000;
    private const double ToneHz = 1000;
    private const double OtherHz = 3700;
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task A_tone_played_on_the_default_output_reaches_the_memory_of_the_engine()
    {
        var previous = Pactl("get-default-sink");
        var module = Pactl("load-module", "module-null-sink", "sink_name=hindsound_tone_out");
        var folder = Directory.CreateTempSubdirectory("hindsound-tone-").FullName;
        try
        {
            Pactl("set-default-sink", "hindsound_tone_out");
            var log = new Log();
            using var engine = new AudioEngine(
                new AudioEngineOptions(TimeSpan.FromSeconds(10), MicEnabled: false),
                new PulseCaptureFactory(log.Write),
                new PulseDeviceWatcher(log.Write),
                processes: new ProcProcessSnapshot());
            await engine.StartAsync();
            await WaitForAsync(() => engine.Status.SystemCapturing, "the capture of the PC's sound", log);

            var status = engine.Status;
            Assert.False(status.OwnSoundsExcluded);
            Assert.NotNull(status.SystemDeviceName);

            var tone = Path.Combine(folder, "tone.wav");
            WriteTone(tone, channels: 2, seconds: 2);
            Run("paplay", "--device=hindsound_tone_out", tone);

            var snapshot = engine.Snapshot(TimeSpan.FromSeconds(5));
            await engine.StopAsync();
            AssertTone(snapshot.System, channels: 2);
        }
        finally
        {
            Pactl("set-default-sink", previous);
            Pactl("unload-module", module);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_tone_fed_to_a_microphone_reaches_the_memory_of_the_engine()
    {
        // A microphone the test can speak into: an input made from the monitor of an output of its own.
        var feed = Pactl("load-module", "module-null-sink", "sink_name=hindsound_mic_feed");
        var mic = Pactl("load-module", "module-remap-source", "master=hindsound_mic_feed.monitor", "source_name=hindsound_test_mic");
        var folder = Directory.CreateTempSubdirectory("hindsound-mic-").FullName;
        try
        {
            var log = new Log();
            using var engine = new AudioEngine(
                new AudioEngineOptions(TimeSpan.FromSeconds(10), MicEnabled: true, MicDeviceId: "hindsound_test_mic"),
                new PulseCaptureFactory(log.Write),
                new PulseDeviceWatcher(log.Write),
                processes: new ProcProcessSnapshot());
            await engine.StartAsync();
            await WaitForAsync(() => engine.Status.MicCapturing, "the capture of the microphone", log);
            Assert.NotNull(engine.Status.MicDeviceName);

            var tone = Path.Combine(folder, "tone.wav");
            WriteTone(tone, channels: 1, seconds: 2);
            Run("paplay", "--device=hindsound_mic_feed", tone);

            var snapshot = engine.Snapshot(TimeSpan.FromSeconds(5));
            await engine.StopAsync();
            AssertTone(snapshot.Mic, channels: 1);
        }
        finally
        {
            Pactl("unload-module", mic);
            Pactl("unload-module", feed);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_new_output_and_a_new_default_are_told_and_the_capture_follows_the_default()
    {
        var log = new Log();
        using var changed = new SemaphoreSlim(0);
        using var watcher = new PulseDeviceWatcher(log.Write);
        watcher.DevicesChanged += () => changed.Release();
        watcher.Start();
        var previous = Pactl("get-default-sink");
        var module = Pactl("load-module", "module-null-sink", "sink_name=hindsound_watch_out");
        try
        {
            Assert.True(await changed.WaitAsync(Guard), "no change told for a new output. Log: " + log);
            await Task.Delay(500);
            while (changed.CurrentCount > 0)
            {
                await changed.WaitAsync();
            }

            Pactl("set-default-sink", "hindsound_watch_out");
            Assert.True(await changed.WaitAsync(Guard), "no change told for a new default output. Log: " + log);

            var factory = new PulseCaptureFactory(log.Write);
            Assert.Equal("hindsound_watch_out.monitor", factory.ResolveDevice(AudioFlow.System, null));
            Assert.Contains(new PulseAudioChoices(log.Write).Devices(AudioFlow.System), device => device.Id == "hindsound_watch_out");
            Assert.DoesNotContain(new PulseAudioChoices(log.Write).Devices(AudioFlow.Mic), device => device.Id == "hindsound_watch_out.monitor");
        }
        finally
        {
            Pactl("set-default-sink", previous);
            Pactl("unload-module", module);
        }
    }

    [Fact]
    public void The_process_list_has_this_process_and_its_parent()
    {
        var entries = new ProcProcessSnapshot().Take();
        var self = Assert.Single(entries, entry => entry.Id == Environment.ProcessId);

        Assert.True(self.ParentId > 0);
        Assert.Contains(entries, entry => entry.Id == self.ParentId);
    }

    private static async Task WaitForAsync(Func<bool> condition, string what, Log log)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Guard, $"{what} did not start. Log: {log}");
            await Task.Delay(100);
        }
    }

    // The tone is in some 100 ms of the window, strong, and far stronger than another frequency: not noise, not silence.
    private static void AssertTone(float[] samples, int channels)
    {
        const int Block = Rate / 10;
        var frames = samples.Length / channels;
        Assert.True(frames >= Block, $"only {frames} frames in the memory");

        var best = (Tone: 0.0, Other: 0.0);
        for (var start = 0; start + Block <= frames; start += Block / 2)
        {
            var tone = Amplitude(samples, channels, start, Block, ToneHz);
            if (tone > best.Tone)
            {
                best = (tone, Amplitude(samples, channels, start, Block, OtherHz));
            }
        }

        Assert.True(best.Tone > 0.1, $"the tone peaks at {best.Tone:0.000}, played at 0.5");
        Assert.True(best.Tone > 10 * best.Other, $"the tone at {best.Tone:0.000} is not far above {OtherHz} Hz at {best.Other:0.000}");
    }

    // Goertzel on the first channel: the amplitude of one frequency over a block.
    private static double Amplitude(float[] samples, int channels, int startFrame, int frames, double hz)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * hz / Rate);
        double previous = 0, beforePrevious = 0;
        for (var i = 0; i < frames; i++)
        {
            var current = samples[(startFrame + i) * channels] + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
        return 2 * Math.Sqrt(Math.Max(power, 0)) / frames;
    }

    private static void WriteTone(string path, int channels, int seconds)
    {
        var frames = Rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = frames * channels * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(Rate);
        writer.Write(Rate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * ToneHz * i / Rate));
            for (var channel = 0; channel < channels; channel++)
            {
                writer.Write(sample);
            }
        }
    }

    private static string Pactl(params string[] arguments) => Run("pactl", arguments);

    private static string Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Guard))
        {
            process.Kill();
            Assert.Fail($"{program} {string.Join(' ', arguments)} did not end");
        }

        Assert.True(process.ExitCode == 0, $"{program} {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Result.Trim();
    }

    private sealed class Log
    {
        private readonly List<string> _lines = [];

        public void Write(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }

        public override string ToString()
        {
            lock (_lines)
            {
                return string.Join(" | ", _lines);
            }
        }
    }
}
