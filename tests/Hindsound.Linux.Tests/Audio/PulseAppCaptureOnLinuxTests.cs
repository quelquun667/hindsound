using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Linux.Audio;
using Xunit.Abstractions;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The capture of one application on a real sound server: the PulseAudio the CI starts, and PipeWire in a job of its own. The
/// "application" is a copy of /bin/sh under a name of its own, which plays a tone with paplay, its child; another process plays
/// another tone at the same time. The tones play on an output of the tests' own, so the other tests that listen to the default
/// output never hear them.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class PulseAppCaptureOnLinuxTests : IDisposable
{
    private const int Rate = 48000;
    private const int Channels = 2;
    private const double AppHz = 700;
    private const double OtherHz = 2300;
    private const string Sink = "hindsound_app_out";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    // PulseAudio plays what an output had rendered ahead (up to two seconds) before the short delay its pacers ask for takes
    // effect, so the first seconds after the capture is opened may miss a sound. That is the server's: the tests let the
    // opening pass before they measure. PipeWire has no such gap.
    private static readonly TimeSpan OpeningGap = TimeSpan.FromMilliseconds(2500);

    private readonly ITestOutputHelper _output;
    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-app-").FullName;
    private readonly string _module;
    private readonly List<Process> _processes = [];

    public PulseAppCaptureOnLinuxTests(ITestOutputHelper output)
    {
        _output = output;
        _module = Pactl("load-module", "module-null-sink", "sink_name=" + Sink);
    }

    public void Dispose()
    {
        foreach (var process in _processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // It had ended.
            }

            process.Dispose();
        }

        Pactl("unload-module", _module);
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Only_the_chosen_application_is_recorded_while_another_process_plays_at_the_same_time()
    {
        var app = StartApp("hsappone", $"{Play(Tone("app.wav", AppHz, 8))}; true");
        Start("paplay", "--device=" + Sink, Tone("other.wav", OtherHz, 8));
        WaitFor(() => StreamsOn(Sink) >= 2, "the two tones playing");

        var log = new Log();
        var recorder = new Recorder();
        using (var source = PulseAppCaptureSource.Open(app.Id, "hsappone", new ProcProcessSnapshot(), log.Write))
        {
            source.PacketAvailable += recorder.OnPacket;
            source.Start();
            Thread.Sleep(OpeningGap);
            Thread.Sleep(2000);
            Assert.Equal(1, source.RecordedStreams);
            source.Stop();
        }

        var samples = recorder.Samples();
        var (app700, other) = Strongest(samples);
        _output.WriteLine($"one app: {AppHz} Hz peaks at {app700:0.0000}, {OtherHz} Hz at {other:0.000000}, log: {log}");
        Assert.True(app700 > 0.1, $"the application's tone peaks at {app700:0.000}, played at 0.5. Log: {log}");
        Assert.True(other < app700 / 100, $"the other process's tone is heard at {other:0.00000}, the application's at {app700:0.000}");
    }

    [Fact]
    public void The_application_is_followed_when_it_plays_again_time_goes_on_in_between_and_its_streams_are_let_go()
    {
        var tone = Tone("app.wav", AppHz, 1);
        var other = Start("paplay", "--device=" + Sink, Tone("other.wav", OtherHz, 8));
        WaitFor(() => StreamsOn(Sink) >= 1, "the other process's tone playing");

        var log = new Log();
        var recorder = new Recorder();
        var app = StartApp("hsapptwo", $"sleep 0.5; {Play(tone)}; sleep 1.5; {Play(tone)}; true");
        using var source = PulseAppCaptureSource.Open(app.Id, "hsapptwo", new ProcProcessSnapshot(), log.Write);
        source.PacketAvailable += recorder.OnPacket;
        var clock = Stopwatch.StartNew();
        source.Start();
        var most = 0;
        while (!app.HasExited)
        {
            Assert.True(clock.Elapsed < Guard, "the application did not end. Log: " + log);
            most = Math.Max(most, source.RecordedStreams);
            Thread.Sleep(20);
        }

        // Its streams are let go once they end, on both servers: PulseAudio ends the record stream, PipeWire does not. The
        // server is asked too, for a recording this capture would no longer know of.
        WaitFor(() => source.HeldStreams == 0 && source.RecordedStreams == 0, "the streams of the application let go");
        WaitFor(() => RecordingsOnServer(PulseAppCaptureSource.StreamName) == 0, "the recordings of the application gone from the server");
        Thread.Sleep(500);
        source.Stop();
        var elapsed = clock.Elapsed;
        Assert.False(other.HasExited, "the other process stopped playing too early to mean anything");

        var samples = recorder.Samples();
        var blocks = Blocks(samples, AppHz);
        var others = Blocks(samples, OtherHz);
        var pattern = string.Concat(blocks.Select(level => level > 0.1 ? '#' : level < 0.01 ? '.' : '-'));
        _output.WriteLine($"followed: {pattern} ({samples.Length / Channels} frames in {elapsed.TotalSeconds:0.00} s, at most {most} stream at once)");

        // Tone, then a second and more of silence, then the tone again: each tone of a second heard over 8 blocks of 100 ms at
        // least, one at each end being shared with the silence.
        Assert.Matches(@"#{8,}[-.]*\.{10,}[-.]*#{8,}", pattern);
        Assert.All(others, level => Assert.True(level < 0.01, $"the other process is heard at {level:0.0000}: {pattern}"));
        Assert.Equal(1, most);

        // The time of the capture went on through the silence: as many frames as the clock says, within a tenth of a second.
        var frames = samples.Length / Channels;
        Assert.InRange(frames, (elapsed.TotalSeconds - 0.1) * Rate, (elapsed.TotalSeconds + 0.05) * Rate);
        Assert.Equal(0, recorder.Discontinuities);
    }

    [Fact]
    public void The_application_is_recorded_again_when_the_output_it_plays_on_goes_away_while_it_plays()
    {
        // An output of its own, unloaded while the application plays on it, as a headset unplugged: the server moves the stream
        // to another output, and the capture records it there, once.
        const string Gone = "hindsound_app_gone";
        var module = Pactl("load-module", "module-null-sink", "sink_name=" + Gone);
        var unloaded = false;
        try
        {
            var app = StartApp("hsappsix", $"paplay --device={Gone} '{Tone("long.wav", AppHz, 14)}'; true");
            WaitFor(() => StreamsOn(Gone) >= 1, "the tone playing");
            var log = new Log();
            var recorder = new Recorder();
            using var source = PulseAppCaptureSource.Open(app.Id, "hsappsix", new ProcProcessSnapshot(), log.Write);
            source.PacketAvailable += recorder.OnPacket;
            source.Start();
            Thread.Sleep(OpeningGap);
            WaitFor(() => source.RecordedStreams == 1, "the application recorded");

            Pactl("unload-module", module);
            unloaded = true;

            // Long enough for two tries again (one second, then two), then two seconds measured.
            Thread.Sleep(4000);
            var from = recorder.Frames;
            Thread.Sleep(2000);
            var levels = Blocks(recorder.Samples()[(from * Channels)..], AppHz);
            var (held, recorded, onServer) = (source.HeldStreams, source.RecordedStreams, RecordingsOnServer(PulseAppCaptureSource.StreamName));
            var playing = !app.HasExited;
            source.Stop();

            _output.WriteLine($"output gone: held={held} recorded={recorded} onServer={onServer} level={levels.DefaultIfEmpty().Min():0.000} to {levels.DefaultIfEmpty().Max():0.000}, log: {log}");
            Assert.True(playing, "the application stopped playing too early to mean anything");
            Assert.True(levels.Length >= 15, $"only {levels.Length} blocks measured");

            // Each block at the tone's level, played at 0.5: never cut, and never two recordings of it added.
            Assert.All(levels, level => Assert.InRange(level, 0.4, 0.6));
            Assert.Equal(1, held);
            Assert.Equal(1, recorded);
            Assert.Equal(1, onServer);
        }
        finally
        {
            if (!unloaded)
            {
                Pactl("unload-module", module);
            }
        }
    }

    [Fact]
    public void The_first_sound_of_a_stream_that_starts_arrives_quickly()
    {
        var log = new Log();
        var recorder = new Recorder();
        var app = StartApp("hsappthree", $"sleep {(1 + OpeningGap.TotalSeconds).ToString(CultureInfo.InvariantCulture)}; {Play(Tone("app.wav", AppHz, 2))}; true");
        var started = Stopwatch.StartNew();
        using var source = PulseAppCaptureSource.Open(app.Id, "hsappthree", new ProcProcessSnapshot(), log.Write);
        source.PacketAvailable += recorder.OnPacket;
        source.Start();
        TimeSpan? recorded = null;
        Thread.Sleep(OpeningGap);
        while (recorder.FirstTone is null)
        {
            Assert.True(started.Elapsed < Guard, "the tone never came. Log: " + log);
            if (recorded is null && source.RecordedStreams > 0)
            {
                recorded = started.Elapsed;
            }

            Thread.Sleep(2);
        }

        source.Stop();

        // The player starts a second after the opening has passed; its tone is in the mix less than half a second after that.
        var late = recorder.FirstToneSince(started) - TimeSpan.FromSeconds(1) - OpeningGap;
        _output.WriteLine($"first sound: stream recorded {recorded?.TotalMilliseconds:0} ms, tone heard {late.TotalMilliseconds:0} ms after the player started");
        Assert.True(late < TimeSpan.FromMilliseconds(500), $"the first sound came {late.TotalMilliseconds:0} ms after the player started (stream recorded at {recorded?.TotalMilliseconds:0} ms). Log: {log}");
    }

    [Fact]
    public async Task The_engine_records_the_application_chosen_by_its_name()
    {
        StartApp("hsappfour", $"{Play(Tone("app.wav", AppHz, 8))}; true");
        Start("paplay", "--device=" + Sink, Tone("other.wav", OtherHz, 8));
        WaitFor(() => StreamsOn(Sink) >= 2, "the two tones playing");

        var log = new Log();
        using var engine = new AudioEngine(
            new AudioEngineOptions(TimeSpan.FromSeconds(10), MicEnabled: false, SourceApp: "hsappfour"),
            new PulseCaptureFactory(log.Write),
            new PulseDeviceWatcher(log.Write),
            processes: new ProcProcessSnapshot());
        await engine.StartAsync();
        WaitFor(() => engine.Status.SystemCapturing, "the capture of the application");
        Thread.Sleep(OpeningGap);
        Thread.Sleep(2000);
        var snapshot = engine.Snapshot(TimeSpan.FromSeconds(1.5));
        await engine.StopAsync();

        var (app700, other) = Strongest(snapshot.System);
        _output.WriteLine($"engine: {AppHz} Hz at {app700:0.0000}, {OtherHz} Hz at {other:0.000000}");
        Assert.True(app700 > 0.1, $"the application's tone peaks at {app700:0.000}. Log: {log}");
        Assert.True(other < app700 / 100, $"the other process's tone is heard at {other:0.00000}");
    }

    [Fact]
    public void The_list_offers_an_application_while_it_plays_and_not_once_it_has_ended()
    {
        // The player itself, under a name of its own, so that another test's player cannot be taken for it.
        var player = Path.Combine(_folder, "hsplayfive");
        File.Copy("/usr/bin/paplay", player);
        File.SetUnixFileMode(player, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var app = Start(player, "--device=" + Sink, Tone("app.wav", AppHz, 2));
        WaitFor(() => StreamsOn(Sink) >= 1, "the tone playing");
        var choices = new PulseAudioChoices(new Log().Write);

        Assert.Contains(choices.Apps(), entry => entry.ExeName == "hsplayfive");

        Assert.True(app.WaitForExit(Guard));
        WaitFor(() => StreamsOn(Sink) == 0, "the tone ended");
        Assert.DoesNotContain(choices.Apps(), entry => entry.ExeName == "hsplayfive");
    }

    // The strongest level of each tone over blocks of 50 ms: the application's, and the other one's.
    private static (double App, double Other) Strongest(float[] samples) => (Blocks(samples, AppHz).DefaultIfEmpty().Max(), Blocks(samples, OtherHz).DefaultIfEmpty().Max());

    // The level of a tone in each block of 100 ms, on the first channel (Goertzel).
    private static double[] Blocks(float[] samples, double hz)
    {
        const int Block = Rate / 10;
        var frames = samples.Length / Channels;
        var levels = new List<double>();
        for (var start = 0; start + Block <= frames; start += Block)
        {
            levels.Add(Level(samples, start, Block, hz));
        }

        return [.. levels];
    }

    private static double Level(ReadOnlySpan<float> samples, int startFrame, int frames, double hz)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * hz / Rate);
        double previous = 0, beforePrevious = 0;
        for (var i = 0; i < frames; i++)
        {
            var current = samples[(startFrame + i) * Channels] + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
        return 2 * Math.Sqrt(Math.Max(power, 0)) / frames;
    }

    private static string Play(string wav) => $"paplay --device={Sink} '{wav}'";

    // A copy of /bin/sh named as the application: the name the settings would keep, at most 15 bytes like any name in /proc.
    private Process StartApp(string name, string script)
    {
        var path = Path.Combine(_folder, name);
        File.Copy("/bin/sh", path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return Start(path, "-c", script);
    }

    private Process Start(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start)!;
        _processes.Add(process);
        return process;
    }

    private string Tone(string name, double hz, int seconds)
    {
        var path = Path.Combine(_folder, name);
        if (File.Exists(path))
        {
            return path;
        }

        var frames = Rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = frames * Channels * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(Rate);
        writer.Write(Rate * Channels * sizeof(short));
        writer.Write((short)(Channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * hz * i / Rate));
            for (var channel = 0; channel < Channels; channel++)
            {
                writer.Write(sample);
            }
        }

        return path;
    }

    // The playback streams on the tests' output, by the index pactl gives for it.
    private static int StreamsOn(string sink)
    {
        var sinks = Pactl("list", "short", "sinks").Split('\n').Select(line => line.Split('\t')).Where(parts => parts.Length > 1);
        var index = sinks.FirstOrDefault(parts => parts[1] == sink)?[0];
        return index is null
            ? 0
            : Pactl("list", "short", "sink-inputs").Split('\n').Select(line => line.Split('\t')).Count(parts => parts.Length > 1 && parts[1] == index);
    }

    // The recordings on the server named so, by their media.name: those of every client, this capture's among them.
    private static int RecordingsOnServer(string name)
    {
        var line = $"media.name = \"{name}\"";
        return Pactl("list", "source-outputs").Split('\n').Count(entry => entry.Trim() == line);
    }

    private static void WaitFor(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Guard, what + ": not within " + Guard);
            Thread.Sleep(50);
        }
    }

    private static string Pactl(params string[] arguments)
    {
        var start = new ProcessStartInfo("pactl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
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
            Assert.Fail("pactl " + string.Join(' ', arguments) + " did not end");
        }

        Assert.True(process.ExitCode == 0, "pactl " + string.Join(' ', arguments) + " failed: " + error.Result);
        return output.Result.Trim();
    }

    /// <summary>What the capture gives, packet by packet, and when the application's tone first came.</summary>
    private sealed class Recorder
    {
        private readonly List<float> _samples = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _next;

        public TimeSpan? FirstTone { get; private set; }

        public int Discontinuities { get; private set; }

        public int Frames
        {
            get
            {
                lock (_samples)
                {
                    return _samples.Count / Channels;
                }
            }
        }

        public void OnPacket(ReadOnlySpan<byte> buffer, PacketFlags flags, long devicePosition, long qpcPosition)
        {
            var samples = MemoryMarshal.Cast<byte, float>(buffer);
            lock (_samples)
            {
                if (devicePosition != _next)
                {
                    Discontinuities++;
                }

                _next = devicePosition + (samples.Length / Channels);
                _samples.AddRange(samples);
                if (FirstTone is null && samples.Length / Channels >= Rate / 200 && Level(samples, 0, samples.Length / Channels, AppHz) > 0.1)
                {
                    FirstTone = _clock.Elapsed;
                }
            }
        }

        // When the tone came, on the clock of the caller.
        public TimeSpan FirstToneSince(Stopwatch caller) => FirstTone!.Value + (caller.Elapsed - _clock.Elapsed);

        public float[] Samples()
        {
            lock (_samples)
            {
                return [.. _samples];
            }
        }
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
                return _lines.Count == 0 ? "(empty)" : string.Join(" | ", _lines);
            }
        }
    }
}
