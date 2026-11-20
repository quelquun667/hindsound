using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Linux.Audio;
using Xunit.Abstractions;

namespace Hindsound.Linux.Tests.Audio;

// Temporary probe of reviewer 1, never failing: (1) does the server end a monitor-stream record stream when its playback stream
// ends, with and without DONT_MOVE; (2) the cost of the pacers against the capture of the whole PC.
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed unsafe class PulseAppCaptureOnLinuxTestsR1Probe(ITestOutputHelper output)
{
    [Fact]
    public void R1_probe_end_of_stream_and_cost()
    {
        var folder = Directory.CreateTempSubdirectory("r1probe-").FullName;
        var module = Pactl("load-module", "module-null-sink", "sink_name=r1probe_out");
        try
        {
            var wav = Path.Combine(folder, "t.wav");
            WriteTone(wav, 2);
            var player = Process.Start("paplay", ["--device=r1probe_out", wav])!;
            string? line = null;
            var clock = Stopwatch.StartNew();
            while (line is null && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                var sinkIndex = Pactl("list", "short", "sinks").Split('\n').Select(l => l.Split('\t')).First(p => p.Length > 1 && p[1] == "r1probe_out")[0];
                line = Pactl("list", "short", "sink-inputs").Split('\n').FirstOrDefault(l => l.Split('\t') is { Length: > 1 } p && p[1] == sinkIndex);
                Thread.Sleep(50);
            }

            var index = uint.Parse(line!.Split('\t')[0]);
            using var connection = PulseConnection.Open();
            var streams = new IntPtr[2];
            var flags = new[] { PulseAppRules.RecordFlags, PulseFormats.AdjustLatency };
            connection.Locked(() =>
            {
                for (var i = 0; i < 2; i++)
                {
                    var spec = PulseFormats.SampleSpecFor(AudioFlow.System);
                    var attr = PulseFormats.BufferAttrFor(AudioFlow.System);
                    streams[i] = PulseNative.StreamNew(connection.Context, "r1probe " + i, &spec, IntPtr.Zero);
                    PulseNative.StreamSetMonitorStream(streams[i], index);
                    PulseNative.StreamConnectRecord(streams[i], "r1probe_out.monitor", &attr, flags[i]);
                }
            });

            var states = new List<string>();
            var end = Stopwatch.StartNew();
            while (end.Elapsed < TimeSpan.FromSeconds(6))
            {
                int a = 0, b = 0;
                connection.Locked(() => { a = PulseNative.StreamGetState(streams[0]); b = PulseNative.StreamGetState(streams[1]); });
                var outputs = Pactl("list", "short", "source-outputs").Split('\n').Count(l => l.Length > 0);
                states.Add($"{end.Elapsed.TotalSeconds:0.0}s exited={player.HasExited} dontmove={a} plain={b} source-outputs={outputs}");
                Thread.Sleep(500);
            }

            output.WriteLine("R1END " + Pactl("info").Split('\n').First(l => l.StartsWith("Server Version", StringComparison.Ordinal)) + " | " + string.Join(" | ", states));
            connection.Locked(() =>
            {
                foreach (var s in streams)
                {
                    if (PulseNative.StreamGetState(s) == PulseNative.StreamReady)
                    {
                        PulseNative.StreamDisconnect(s);
                    }

                    PulseNative.StreamUnref(s);
                }
            });
        }
        finally
        {
            Pactl("unload-module", module);
        }

        // Cost: nothing, the whole PC (the default output's monitor), one application that plays nothing (its pacers only).
        output.WriteLine("R1SINKS " + Pactl("list", "short", "sinks").Replace('\n', ' ').Replace('\t', ' '));
        output.WriteLine("R1COST idle " + Measure());
        var monitor = Pactl("get-default-sink") + ".monitor";
        using (var whole = PulseCaptureSource.Open(AudioFlow.System, monitor, "r1 whole", "whole"))
        {
            whole.Start();
            Thread.Sleep(1000);
            output.WriteLine("R1COST whole-pc " + Measure());
        }

        var sleeper = Process.Start("sleep", "60")!;
        using (var app = PulseAppCaptureSource.Open(sleeper.Id, "sleep", new ProcProcessSnapshot(), _ => { }))
        {
            app.Start();
            Thread.Sleep(1000);
            output.WriteLine("R1COST one-app " + Measure() + " held=" + app.HeldStreams);
        }

        sleeper.Kill();
        Directory.Delete(folder, true);
    }

    // CPU ms and context switches per second over 5 s, of the server's processes and of this one.
    private static string Measure()
    {
        var pids = new[] { "pulseaudio", "pipewire", "pipewire-pulse", "wireplumber" }
            .SelectMany(name => Process.GetProcessesByName(name).Select(p => (name: name, Id: p.Id)))
            .Append((name: "host", Id: Environment.ProcessId)).ToList();
        var before = pids.Select(p => Sample(p.Id)).ToList();
        Thread.Sleep(5000);
        var after = pids.Select(p => Sample(p.Id)).ToList();
        return string.Join(", ", pids.Select((p, i) => $"{p.name}: cpu {(after[i].Ticks - before[i].Ticks) * 10 / 5.0:0} ms/s, switches {(after[i].Switches - before[i].Switches) / 5.0:0}/s"));
    }

    private static (long Ticks, long Switches) Sample(int pid)
    {
        long ticks = 0, switches = 0;
        foreach (var task in Directory.GetDirectories($"/proc/{pid}/task"))
        {
            try
            {
                var stat = File.ReadAllText(Path.Combine(task, "stat"));
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                ticks += long.Parse(fields[11]) + long.Parse(fields[12]);
                foreach (var l in File.ReadAllLines(Path.Combine(task, "status")).Where(l => l.Contains("ctxt_switches", StringComparison.Ordinal)))
                {
                    switches += long.Parse(l.Split(':')[1].Trim());
                }
            }
            catch (IOException)
            {
                // A thread gone.
            }
        }

        return (ticks, switches);
    }

    private static void WriteTone(string path, int seconds)
    {
        const int Rate = 48000;
        using var writer = new BinaryWriter(File.Create(path));
        var frames = Rate * seconds;
        writer.Write("RIFF"u8);
        writer.Write(36 + (frames * 4));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(Rate);
        writer.Write(Rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 4);
        for (var i = 0; i < frames; i++)
        {
            var s = (short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * 700 * i / Rate));
            writer.Write(s);
            writer.Write(s);
        }
    }

    private static string Pactl(params string[] arguments)
    {
        var start = new ProcessStartInfo("pactl") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit(20000);
        return text.Trim();
    }
}
