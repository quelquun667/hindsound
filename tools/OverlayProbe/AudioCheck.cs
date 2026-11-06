using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OverlayProbe;

/// Answers one question: does a process-excluding loopback keep Hindsound's own confirmation sound out of the capture?
/// Captures to memory only, for about 2 s, and writes nothing but the measures below to the log.
internal static class AudioCheck
{
    private sealed class Capture(string name)
    {
        public string Name { get; } = name;
        public string Format { get; set; } = "?";
        public int Rate { get; set; }
        public List<float> Samples { get; } = [];
        public int Packets { get; set; }
        public int SilentPackets { get; set; }
        public double MaxGapMs { get; set; }
        public long LastTicks { get; set; }
    }

    public static async Task RunAsync(ProbeLog log)
    {
        var wav = Sounds.ConfirmationWav();
        var pid = (uint)Environment.ProcessId;
        var clock = Stopwatch.StartNew();

        var full = new Capture("system loopback");
        var excluded = new Capture("excluding this process");

        using var fullRecorder = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
        using var excludedRecorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback(pid, ProcessLoopbackMode.ExcludeTargetProcessTree)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
            .BuildAsync();

        Attach(fullRecorder, full, clock);
        Attach(excludedRecorder, excluded, clock);

        fullRecorder.StartRecording();
        excludedRecorder.StartRecording();
        await Task.Delay(700);
        await Task.Run(() => Sounds.Play(wav));
        await Task.Delay(300);
        await Task.Run(() => Sounds.Play(wav));
        await Task.Delay(2500);
        fullRecorder.StopRecording();
        excludedRecorder.StopRecording();

        var fullEnergy = Report(log, full);
        var excludedEnergy = Report(log, excluded);
        var db = 20 * Math.Log10(Math.Max(fullEnergy, 1e-9) / Math.Max(excludedEnergy, 1e-9));
        log.Write($"audio: our own tones are {db:F1} dB lower with the exclusion");

        var fullOther = Tone(full, 660) + Tone(full, 990);
        var excludedOther = Tone(excluded, 660) + Tone(excluded, 990);
        log.Write($"audio: the other app's tones (control): {fullOther:F5} in the system loopback, {excludedOther:F5} with the exclusion");
    }

    private static void Attach(WasapiRecorder recorder, Capture capture, Stopwatch clock)
    {
        var format = recorder.WaveFormat;
        capture.Rate = format.SampleRate;
        capture.Format = $"{format.Encoding} {format.BitsPerSample} bit {format.SampleRate} Hz {format.Channels} ch";
        var channels = format.Channels;
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);

        recorder.DataAvailable += (ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long device, long qpc) =>
        {
            var now = clock.ElapsedTicks;
            if (capture.LastTicks != 0)
            {
                capture.MaxGapMs = Math.Max(capture.MaxGapMs, (now - capture.LastTicks) * 1000.0 / Stopwatch.Frequency);
            }

            capture.LastTicks = now;
            capture.Packets++;
            if (flags.HasFlag(AudioClientBufferFlags.Silent))
            {
                capture.SilentPackets++;
            }

            if (!isFloat)
            {
                return;
            }

            var frameBytes = 4 * channels;
            for (var offset = 0; offset + frameBytes <= data.Length; offset += frameBytes)
            {
                capture.Samples.Add(BitConverter.ToSingle(data.Slice(offset, 4)));
            }
        };
    }

    private static double Report(ProbeLog log, Capture capture)
    {
        var energy = Tone(capture, 880) + Tone(capture, 1320);
        var peak = capture.Samples.Count == 0 ? 0 : capture.Samples.Max(Math.Abs);
        log.Write($"audio: {capture.Name}: {capture.Format}, {capture.Packets} packets, {capture.SilentPackets} flagged silent, "
            + $"longest gap {capture.MaxGapMs:F0} ms, {capture.Samples.Count} samples, peak {peak:F3}, tone energy {energy:F5}");
        return energy;
    }

    // Goertzel over the whole capture: how much of this one frequency it holds.
    private static double Tone(Capture capture, double frequency)
    {
        if (capture.Samples.Count == 0)
        {
            return 0;
        }

        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / capture.Rate);
        double previous = 0, beforePrevious = 0;
        foreach (var sample in capture.Samples)
        {
            var current = sample + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
        return Math.Sqrt(Math.Max(0, power)) / capture.Samples.Count;
    }
}
