using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The preview on a real sound server, the one the CI starts: a tone the preview plays on an output of the test's own is
/// heard by a plain capture of its monitor, and is silence in the capture that reads the preview's silence, which hears
/// again once the preview has ended. The test plays on its own output and never moves the default, which other tests move.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class PulsePreviewOnLinuxTests
{
    private const int Rate = InternalFormat.SampleRate;
    private const double PreviewHz = 1000;
    private const double AfterHz = 2500;
    private const string Sink = "hindsound_preview_out";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task A_tone_the_preview_plays_is_silence_in_the_capture_of_the_pc_s_sound_which_hears_again_after()
    {
        var module = Pactl("load-module", "module-null-sink", "sink_name=" + Sink);
        var folder = Directory.CreateTempSubdirectory("hindsound-preview-").FullName;
        var log = new List<string>();
        try
        {
            var silence = new PulsePlaybackSilence();
            var gated = new Recorder();
            var plain = new Recorder();
            using var gatedSource = PulseCaptureSource.Open(AudioFlow.System, Sink + ".monitor", "Test gated", null, silence);
            using var plainSource = PulseCaptureSource.Open(AudioFlow.System, Sink + ".monitor", "Test plain", null);
            gatedSource.PacketAvailable += gated.Take;
            plainSource.PacketAvailable += plain.Take;
            gatedSource.Start();
            plainSource.Start();
            await Task.Delay(300);

            string streams;
            using (var player = new PulsePreviewPlayer(new ToneDecoder(PreviewHz, seconds: 1.5), silence, () => PulsePlaybackOutput.Connect(Sink), Write))
            {
                Assert.Equal(0, player.Open("/nonexistent/tone.wav", "hindsound_preview_1"));
                Assert.Equal(0, player.Play("hindsound_preview_1"));
                Assert.True(silence.IsSilent);
                streams = Pactl("list", "sink-inputs");

                using var never = new ManualResetEvent(false);
                var clock = Stopwatch.StartNew();
                PreviewTrackState state;
                do
                {
                    Assert.True(clock.Elapsed < Guard, "the preview did not end. Log: " + string.Join(" | ", log));
                    player.Wait(never, ClipPreview.PollMs);
                    Assert.Equal(0, player.StateOf("hindsound_preview_1", out state));
                }
                while (state != PreviewTrackState.Stopped);

                Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(5));
                Assert.Equal(0, player.Close("hindsound_preview_1"));
            }

            await Task.Delay(PulsePlaybackSilence.TailMs + 400);
            Assert.False(silence.IsSilent);

            var after = Path.Combine(folder, "after.wav");
            WriteTone(after, AfterHz, seconds: 1);
            Run("paplay", "--device=" + Sink, after);
            await Task.Delay(300);
            gatedSource.Stop();
            plainSource.Stop();

            Assert.Contains("hindsound.stream = \"preview\"", streams);
            Assert.Contains("application.id = \"io.github.", streams);
            Assert.Contains("application.name = \"Hindsound\"", streams);

            Assert.True(Peak(plain.Samples(), PreviewHz) > 0.1, "the plain capture did not hear the preview: it did not play");
            Assert.True(gated.SilentPackets > 0, "no packet of the gated capture was silenced");
            Assert.True(Peak(gated.Samples(), PreviewHz) < 0.01, $"the preview is in the gated capture at {Peak(gated.Samples(), PreviewHz):0.000}");
            Assert.True(Peak(gated.Samples(), AfterHz) > 0.1, "the gated capture does not hear again after the preview");
            Assert.True(gated.Continuous, "the gated capture lost or added frames: " + gated.Gap);
        }
        finally
        {
            Pactl("unload-module", module);
            Directory.Delete(folder, recursive: true);
        }

        void Write(string line)
        {
            lock (log)
            {
                log.Add(line);
            }
        }
    }

    [Fact]
    public async Task With_the_preview_s_silence_the_engine_says_the_app_s_sounds_are_left_out_of_the_default_output()
    {
        var log = new List<string>();
        using var engine = new AudioEngine(
            new AudioEngineOptions(TimeSpan.FromSeconds(10), MicEnabled: false),
            new PulseCaptureFactory(line => { lock (log) { log.Add(line); } }, new PulsePlaybackSilence()),
            new PulseDeviceWatcher(_ => { }),
            processes: new ProcProcessSnapshot());
        await engine.StartAsync();
        var clock = Stopwatch.StartNew();
        while (!engine.Status.SystemCapturing)
        {
            Assert.True(clock.Elapsed < Guard, "the capture of the PC's sound did not start. Log: " + string.Join(" | ", log));
            await Task.Delay(100);
        }

        Assert.True(engine.Status.OwnSoundsExcluded);
        await engine.StopAsync();
    }

    // The largest amplitude of one frequency over blocks of 100 ms, on the first channel.
    private static double Peak(float[] interleaved, double hz)
    {
        const int Block = Rate / 10;
        var frames = interleaved.Length / 2;
        var best = 0.0;
        for (var start = 0; start + Block <= frames; start += Block / 2)
        {
            var coefficient = 2 * Math.Cos(2 * Math.PI * hz / Rate);
            double previous = 0, beforePrevious = 0;
            for (var i = 0; i < Block; i++)
            {
                var current = interleaved[(start + i) * 2] + coefficient * previous - beforePrevious;
                beforePrevious = previous;
                previous = current;
            }

            var power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
            best = Math.Max(best, 2 * Math.Sqrt(Math.Max(power, 0)) / Block);
        }

        return best;
    }

    private static void WriteTone(string path, double hz, int seconds)
    {
        var frames = Rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = frames * 2 * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(Rate);
        writer.Write(Rate * 2 * sizeof(short));
        writer.Write((short)(2 * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * hz * i / Rate));
            writer.Write(sample);
            writer.Write(sample);
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

    /// <summary>What a capture delivered, as the engine reads it: a silent packet is zeros of its length.</summary>
    private sealed class Recorder
    {
        private readonly List<float> _samples = [];
        private long _next = -1;

        public int SilentPackets { get; private set; }

        public bool Continuous => Gap is null;

        public string? Gap { get; private set; }

        public void Take(ReadOnlySpan<byte> packet, PacketFlags flags, long position, long qpcPosition)
        {
            lock (_samples)
            {
                var frames = packet.Length / (2 * sizeof(float));
                if (_next >= 0 && position != _next)
                {
                    Gap ??= $"a packet at frame {position}, after one that ended at {_next}";
                }

                _next = position + frames;
                if ((flags & PacketFlags.Silent) != 0)
                {
                    SilentPackets++;
                    _samples.AddRange(new float[frames * 2]);
                }
                else
                {
                    _samples.AddRange(MemoryMarshal.Cast<byte, float>(packet).ToArray());
                }
            }
        }

        public float[] Samples()
        {
            lock (_samples)
            {
                return [.. _samples];
            }
        }
    }

    /// <summary>A stereo tone of a known length, in place of the file a clip would have.</summary>
    private sealed class ToneDecoder(double hz, double seconds) : IClipDecoder
    {
        public IDecodedStream Open(string path) => new Stream(hz, (int)(seconds * Rate));

        private sealed class Stream(double hz, int frames) : IDecodedStream
        {
            private int _frame;

            public int Channels => 2;

            public TimeSpan Duration => TimeSpan.FromSeconds((double)frames / Rate);

            public void Seek(TimeSpan at) => _frame = (int)(at.TotalSeconds * Rate);

            public int Read(Span<float> interleaved)
            {
                var count = Math.Min(interleaved.Length / 2, frames - _frame);
                for (var i = 0; i < count; i++, _frame++)
                {
                    interleaved[2 * i] = interleaved[(2 * i) + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * hz * _frame / Rate));
                }

                return count * 2;
            }

            public void Dispose()
            {
            }
        }
    }
}
