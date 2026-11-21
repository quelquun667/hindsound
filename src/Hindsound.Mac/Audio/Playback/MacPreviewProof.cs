using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Unix.Library;

namespace Hindsound.Mac.Audio.Playback;

/// <summary>Where the proof marked its steps, in frames of what the ear heard.</summary>
internal readonly record struct PreviewMarks(long Play, long Stop, long Resume, long End);

/// <summary>
/// <c>--prove-preview folder</c>, for the CI: the app encodes a 1 kHz tone of two seconds in MP3 in the folder, plays it with
/// its own player (<see cref="MacPreviewPlayer"/> on an <see cref="AudioQueueOutput"/>) and hears it at the same time on the
/// default input (BlackHole 2ch, whose input gives back what its output plays). It plays 0.8 s, stops, waits, plays again to
/// the end, and says in the log the level, the peak and the dominant frequency of what was heard while it played, the exact
/// zeros after the stop and after the end, how long the sound lasted, and the states the player gave. 0 when all of it holds,
/// 3 otherwise. The microphone right of the bundle must be written in TCC before: the input of BlackHole is opened.
/// </summary>
internal static class MacPreviewProof
{
    public const string Option = "--prove-preview";

    public const double ToneHz = 1000;
    public const double ToneSeconds = 2.0;

    /// <summary>A peak at -6 dBFS: an RMS level of -9 dBFS.</summary>
    public const float Amplitude = 0.5f;

    private const int ExitFailed = 3;
    private const string TrackName = "proof";

    /// <summary>The folder after <see cref="Option"/>, or null when it was not asked.</summary>
    public static string? Folder(string[] args)
    {
        var at = Array.IndexOf(args, Option);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>The tone, stereo, at the engine's rate.</summary>
    public static float[] Tone()
    {
        var frames = (int)(InternalFormat.SampleRate * ToneSeconds);
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)(Amplitude * Math.Sin(2 * Math.PI * ToneHz * i / InternalFormat.SampleRate));
            samples[2 * i] = value;
            samples[(2 * i) + 1] = value;
        }

        return samples;
    }

    /// <summary>
    /// What was heard, against the marks: the lines for the log and whether it holds. While it plays, an RMS level from -15 to
    /// -3 dBFS and a dominant frequency within 15 Hz of the tone; after the stop and after the end, a peak under -60 dBFS;
    /// and as much sound as the tone, to 0.15 s, since a stop that goes on from what was heard neither loses nor repeats any.
    /// </summary>
    public static (bool Pass, List<string> Lines) Judge(float[] heard, int channels, int rate, PreviewMarks marks)
    {
        var mono = ToneMeasure.Channel(heard, channels, 0);
        var lines = new List<string>();
        var pass = true;
        long At(double seconds) => (long)(seconds * rate);

        ReadOnlySpan<float> Window(long from, long to)
        {
            from = Math.Clamp(from, 0, mono.Length);
            to = Math.Clamp(to, from, mono.Length);
            return mono.AsSpan((int)from, (int)(to - from));
        }

        bool Playing(string name, ReadOnlySpan<float> window)
        {
            var level = ToneMeasure.LevelDbfs(window);
            var dominant = ToneMeasure.Dominant(window, rate);
            var ok = level is >= -15 and <= -3 && Math.Abs(dominant - ToneHz) <= 15;
            lines.Add($"{name}: {window.Length / (double)rate:0.00} s, level {ToneMeasure.Db(level)} dBFS rms, peak {ToneMeasure.Db(ToneMeasure.PeakDbfs(window))} dBFS, "
                + $"dominant {dominant:0} Hz{(ok ? string.Empty : " (expected about -9 dBFS and " + ToneHz + " Hz)")}");
            return ok;
        }

        bool Silent(string name, ReadOnlySpan<float> window)
        {
            var peak = ToneMeasure.PeakDbfs(window);
            var ok = window.Length >= rate / 10 && peak < -60;
            lines.Add($"{name}: {window.Length / (double)rate:0.00} s, peak {ToneMeasure.Db(peak)} dBFS, exact zeros {ToneMeasure.ExactZeros(window)}/{window.Length}"
                + (ok ? string.Empty : " (expected silence)"));
            return ok;
        }

        var before = Window(0, marks.Play);
        lines.Add($"before the play: {before.Length / (double)rate:0.00} s, peak {ToneMeasure.Db(ToneMeasure.PeakDbfs(before))} dBFS, exact zeros {ToneMeasure.ExactZeros(before)}/{before.Length}");
        pass &= Playing("played", Window(marks.Play + At(0.15), marks.Stop - At(0.05)));
        pass &= Silent("after the stop", Window(marks.Stop + At(0.15), marks.Resume - At(0.05)));
        pass &= Playing("played again", Window(marks.Resume + At(0.15), Math.Min(marks.End - At(0.05), marks.Resume + At(0.6))));
        pass &= Silent("after the end", Window(marks.End + At(0.15), mono.Length));

        var audible = ToneMeasure.AudibleSeconds(mono, rate, -40);
        var whole = Math.Abs(audible - ToneSeconds) <= 0.15;
        lines.Add($"sound heard: {audible:0.00} s for a tone of {ToneSeconds:0.00} s{(whole ? string.Empty : " (expected the tone once, whole)")}");
        pass &= whole;
        return (pass, lines);
    }

    [SupportedOSPlatform("macos")]
    public static int Run(string folder, Action<string> log)
    {
        void Say(string line) => log("preview-proof " + line);

        try
        {
            var full = Path.GetFullPath(folder);
            Directory.CreateDirectory(full);
            var saved = new ClipSaver(Path.Combine(full, "rescued")).Save(
                new AudioSnapshot(Tone(), []),
                new ExportOptions { Format = ExportFormat.Mp3, TrackMode = TrackMode.Mix, OutputFolder = full });
            if (saved.FellBackToWav || saved.Rescued || saved.Paths.Count != 1)
            {
                Say($"the tone was not written as one MP3 (fell back to WAV {saved.FellBackToWav}, rescued {saved.Rescued})");
                Say("result fail");
                return ExitFailed;
            }

            var clip = saved.Paths[0];
            var decoder = new UnixClipDecoder();
            using (var decoded = decoder.Open(clip))
            {
                Say($"clip {Path.GetFileName(clip)}: {saved.SizeBytes} bytes, {decoded.Channels} channels, {decoded.Duration.TotalSeconds:0.000} s announced");
            }

            var pass = true;
            using var ear = AudioQueueRecorder.Start();
            Thread.Sleep(300);
            Say("listening on the default input");

            using var player = new MacPreviewPlayer(decoder, new AudioQueueOutput(), Say);
            pass &= Expect("open", player.Open(clip, TrackName));

            var play = ear.Frames;
            pass &= Expect("play", player.Play(TrackName));
            Thread.Sleep(800);
            pass &= ExpectState("while it plays", player, PreviewTrackState.Playing);

            var stop = ear.Frames;
            pass &= Expect("stop", player.Stop(TrackName));
            pass &= ExpectState("after the stop", player, PreviewTrackState.Stopped);
            Thread.Sleep(700);

            var resume = ear.Frames;
            pass &= Expect("play again", player.Play(TrackName));
            var clock = Stopwatch.StartNew();
            PreviewTrackState state;
            while (player.StateOf(TrackName, out state) == 0 && state == PreviewTrackState.Playing && clock.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(10);
            }

            var end = ear.Frames;
            var ended = state == PreviewTrackState.Stopped && clock.ElapsedMilliseconds < 5000;
            Say($"the player said the end {clock.ElapsedMilliseconds} ms after playing again, state {state}{(ended ? string.Empty : " (expected Stopped)")}");
            pass &= ended;
            Thread.Sleep(500);
            pass &= Expect("close", player.Close(TrackName));

            var heard = ear.Heard();
            ear.Dispose();
            Say($"heard {heard.Length / AudioQueueRecorder.Channels / (double)AudioQueueRecorder.Rate:0.00} s on the input");
            var (heardRight, lines) = Judge(heard, AudioQueueRecorder.Channels, AudioQueueRecorder.Rate, new PreviewMarks(play, stop, resume, end));
            lines.ForEach(Say);
            pass &= heardRight;
            Say("result " + (pass ? "pass" : "fail"));
            return pass ? 0 : ExitFailed;

            bool Expect(string verb, int code)
            {
                Say($"{verb}: code {code}");
                return code == 0;
            }

            bool ExpectState(string when, MacPreviewPlayer of, PreviewTrackState expected)
            {
                var code = of.StateOf(TrackName, out var now);
                Say($"state {when}: {now}, code {code}");
                return code == 0 && now == expected;
            }
        }
        catch (Exception exception)
        {
            Say($"failed: {exception.GetType().Name}: {exception.Message}");
            Say("result fail");
            return ExitFailed;
        }
    }
}
