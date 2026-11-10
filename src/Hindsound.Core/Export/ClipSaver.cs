using System.IO;
using System.Security;
using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// Turns a snapshot into file(s): gains, mix or two tracks, normalization, encoding, and a name that never
/// overwrites. If the output folder cannot be written, the clip goes to a rescue folder instead of being lost.
/// If the MP3 encoder fails, a WAV is written instead.
/// </summary>
/// <remarks>
/// The clip is never copied whole. A first pass over the snapshot measures peak and loudness piece by piece; a second
/// pass renders each piece again (mix, gain, limiter, 16-bit conversion) straight into the encoder. Memory use is a
/// few small buffers, not a function of the length of the clip.
/// </remarks>
public sealed class ClipSaver
{
    /// <summary>Frames rendered at a time: 100 ms at 48 kHz.</summary>
    private const int PieceFrames = InternalFormat.SampleRate / 10;

    private readonly string _rescueFolder;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, int, int, IAudioFileWriter> _openMp3;

    /// <param name="rescueFolder">Where clips go when the output folder is not writable. Default: <see cref="DefaultRescueFolder"/>.</param>
    /// <param name="clock">The time used in file names. Default: the local time.</param>
    /// <param name="openMp3">Opens an MP3 file from its path, channels and bitrate. Default: LAME. For tests of the fallback.</param>
    public ClipSaver(string? rescueFolder = null, Func<DateTime>? clock = null, Func<string, int, int, IAudioFileWriter>? openMp3 = null)
    {
        _rescueFolder = rescueFolder ?? DefaultRescueFolder;
        _clock = clock ?? (() => DateTime.Now);
        _openMp3 = openMp3 ?? Encoders.OpenMp3;
    }

    /// <summary><c>%LOCALAPPDATA%\Hindsound\Rescued</c></summary>
    public static string DefaultRescueFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hindsound", "Rescued");

    /// <summary>
    /// Saves the snapshot on a worker thread, so the caller (the hotkey handler) is never blocked.
    /// The snapshot is only read; the caller may drop it afterwards.
    /// </summary>
    /// <param name="appName">Fills <c>{app}</c> in the file name: the active window when the hotkey was pressed.</param>
    public Task<ClipResult> SaveAsync(AudioSnapshot snapshot, ExportOptions options, string? appName = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(snapshot, options, appName, cancellationToken), cancellationToken);

    /// <summary>The same as <see cref="SaveAsync"/>, on the calling thread.</summary>
    public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var frames = Math.Max(snapshot.System.Length / InternalFormat.SystemChannels, snapshot.Mic.Length / InternalFormat.MicChannels);
        if (frames == 0) throw new ArgumentException("The snapshot holds no audio.", nameof(snapshot));

        // One pass over the raw snapshot: the bars and the silence flag.
        var peaks = ClipPeaks.Compute(snapshot);

        var tracks = Plan(snapshot, options, frames, cancellationToken);
        var baseName = FileNameTemplate.Render(options.FileNameTemplate, appName, _clock());
        var (paths, rescued, fellBack) = WriteTracks(tracks, options, baseName, cancellationToken);

        var size = paths.Sum(p => new FileInfo(p).Length);
        return new ClipResult(paths, TimeSpan.FromSeconds((double)frames / InternalFormat.SampleRate), size, peaks.IsSilent, peaks.Bars, rescued, fellBack);
    }

    // ---------------------------------------------------------------- measuring

    /// <summary>One output track, with the gain and limiter decision found by the measuring pass.</summary>
    private sealed record Track(string Suffix, TrackSource Source, double GainDb, bool Limit, float CeilingLinear);

    private static List<Track> Plan(AudioSnapshot snapshot, ExportOptions options, int frames, CancellationToken cancellationToken)
    {
        var systemGain = Normalizer.DbToLinear(options.SystemGainDb);
        var micGain = Normalizer.DbToLinear(options.MicGainDb);

        // Off still keeps the file under 0 dBFS; the other modes use the ceiling.
        var ceilingDb = options.Normalization == NormalizationMode.Off ? 0 : options.PeakCeilingDb;
        var ceilingLinear = Normalizer.DbToLinear(ceilingDb);

        var suffixes = new List<string>();
        var sources = new List<TrackSource>();
        if (options.TrackMode == TrackMode.Mix)
        {
            suffixes.Add(string.Empty);
            sources.Add(new MixSource(snapshot, frames, systemGain, micGain));
        }
        else
        {
            if (snapshot.System.Length > 0)
            {
                suffixes.Add("_system");
                sources.Add(new SystemSource(snapshot, frames, systemGain));
            }

            if (snapshot.Mic.Length > 0)
            {
                suffixes.Add("_mic");
                sources.Add(new MicSource(snapshot, frames, micGain));
            }
        }

        var needLoudness = options.Normalization == NormalizationMode.Loudness;
        var tracks = new List<Track>();

        if (options.TrackMode == TrackMode.MultiTrack && options.LinkedTrackGain && sources.Count == 2)
        {
            // One gain for both tracks. Peak: the louder of the two peaks reaches the ceiling. Loudness: the loudness
            // of the two tracks mixed together (the mic centred).
            var (peakA, peakB, lufs) = MeasureLinked(sources[0], sources[1], needLoudness, cancellationToken);
            var gainDb = options.Normalization switch
            {
                NormalizationMode.Peak => Normalizer.PeakGainDb(Math.Max(peakA, peakB), options.PeakCeilingDb),
                NormalizationMode.Loudness => Normalizer.LoudnessGainDb(lufs, options.TargetLufs),
                _ => 0,
            };
            tracks.Add(MakeTrack(suffixes[0], sources[0], peakA, gainDb, ceilingLinear));
            tracks.Add(MakeTrack(suffixes[1], sources[1], peakB, gainDb, ceilingLinear));
            return tracks;
        }

        for (var i = 0; i < sources.Count; i++)
        {
            var (peak, lufs) = Measure(sources[i], needLoudness, cancellationToken);
            var gainDb = options.Normalization switch
            {
                NormalizationMode.Peak => Normalizer.PeakGainDb(peak, options.PeakCeilingDb),
                NormalizationMode.Loudness => Normalizer.LoudnessGainDb(lufs, options.TargetLufs),
                _ => 0,
            };
            tracks.Add(MakeTrack(suffixes[i], sources[i], peak, gainDb, ceilingLinear));
        }

        return tracks;
    }

    private static Track MakeTrack(string suffix, TrackSource source, float peak, double gainDb, float ceilingLinear)
    {
        var peakAfterGain = peak * (gainDb == 0 ? 1f : Normalizer.DbToLinear(gainDb));
        return new Track(suffix, source, gainDb, Limiter.Needed(peakAfterGain, ceilingLinear), ceilingLinear);
    }

    /// <summary>Peak of the track, and its loudness if asked.</summary>
    private static (float Peak, double Lufs) Measure(TrackSource source, bool loudness, CancellationToken cancellationToken)
    {
        var piece = new float[PieceFrames * source.Channels];
        var meter = loudness ? new LoudnessMeter(source.Channels) : null;
        var peak = 0f;
        for (var start = 0; start < source.Frames; start += PieceFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(PieceFrames, source.Frames - start);
            var samples = piece.AsSpan(0, count * source.Channels);
            source.Fill(start, count, samples);
            peak = Math.Max(peak, Limiter.Peak(samples));
            meter?.Add(samples);
        }

        return (peak, meter?.Finish() ?? double.NegativeInfinity);
    }

    /// <summary>Peak of each of the two tracks, and the loudness of their sum (stereo system plus centred mic).</summary>
    private static (float PeakSystem, float PeakMic, double Lufs) MeasureLinked(TrackSource system, TrackSource mic, bool loudness, CancellationToken cancellationToken)
    {
        var systemPiece = new float[PieceFrames * 2];
        var micPiece = new float[PieceFrames];
        var mixPiece = new float[PieceFrames * 2];
        var meter = loudness ? new LoudnessMeter(2) : null;
        float peakSystem = 0f, peakMic = 0f;
        for (var start = 0; start < system.Frames; start += PieceFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(PieceFrames, system.Frames - start);
            var systemSamples = systemPiece.AsSpan(0, count * 2);
            var micSamples = micPiece.AsSpan(0, count);
            system.Fill(start, count, systemSamples);
            mic.Fill(start, count, micSamples);
            peakSystem = Math.Max(peakSystem, Limiter.Peak(systemSamples));
            peakMic = Math.Max(peakMic, Limiter.Peak(micSamples));

            if (meter is null) continue;
            for (var i = 0; i < count; i++)
            {
                mixPiece[2 * i] = systemSamples[2 * i] + micSamples[i];
                mixPiece[2 * i + 1] = systemSamples[2 * i + 1] + micSamples[i];
            }

            meter.Add(mixPiece.AsSpan(0, count * 2));
        }

        return (peakSystem, peakMic, meter?.Finish() ?? double.NegativeInfinity);
    }

    // ---------------------------------------------------------------- writing

    private (IReadOnlyList<string> Paths, bool Rescued, bool FellBack) WriteTracks(List<Track> tracks, ExportOptions options, string baseName, CancellationToken cancellationToken)
    {
        try
        {
            var folder = ExpandFolder(options.OutputFolder);
            var (paths, fellBack) = WriteWithFallback(folder, tracks, options, baseName, cancellationToken);
            return (paths, false, fellBack);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            var (paths, fellBack) = WriteWithFallback(_rescueFolder, tracks, options, baseName, cancellationToken);
            return (paths, true, fellBack);
        }
    }

    /// <summary>If the MP3 encoder fails for any reason, the same clip is written as WAV in the same folder.</summary>
    private (string[] Paths, bool FellBack) WriteWithFallback(string folder, List<Track> tracks, ExportOptions options, string baseName, CancellationToken cancellationToken)
    {
        if (options.Format == ExportFormat.Mp3)
        {
            try
            {
                return (WriteInto(folder, tracks, ExportFormat.Mp3, options, baseName, cancellationToken), false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // WriteInto removed whatever it had written. A folder that cannot be written fails again below
                // and goes to the rescue folder.
            }

            return (WriteInto(folder, tracks, ExportFormat.Wav, options, baseName, cancellationToken), true);
        }

        return (WriteInto(folder, tracks, ExportFormat.Wav, options, baseName, cancellationToken), false);
    }

    /// <summary>Writes every track into the folder, or none: a failure removes the files this call reserved.</summary>
    private string[] WriteInto(string folder, List<Track> tracks, ExportFormat format, ExportOptions options, string baseName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var extension = format == ExportFormat.Mp3 ? ".mp3" : ".wav";
        // The paths are created here, empty, and belong to this call alone: cleaning up on failure removes only them,
        // never a file another save wrote.
        var paths = FileNameTemplate.ReserveUniquePaths(folder, baseName, tracks.Select(t => t.Suffix).ToArray(), extension);

        try
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                using var writer = format == ExportFormat.Mp3
                    ? _openMp3(paths[i], tracks[i].Source.Channels, options.Mp3BitrateKbps)
                    : Encoders.OpenWav(paths[i], tracks[i].Source.Channels);
                Render(tracks[i], writer, cancellationToken);
            }
        }
        catch
        {
            foreach (var path in paths)
            {
                try { File.Delete(path); } catch (Exception) { /* best effort */ }
            }

            throw;
        }

        return paths;
    }

    /// <summary>Renders a track piece by piece: gain, limiter or clamp, then to the encoder.</summary>
    private static void Render(Track track, IAudioFileWriter writer, CancellationToken cancellationToken)
    {
        var source = track.Source;
        var piece = new float[PieceFrames * source.Channels];
        var limiter = track.Limit ? new StreamingLimiter(source.Channels, track.CeilingLinear) : null;

        for (var start = 0; start < source.Frames; start += PieceFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(PieceFrames, source.Frames - start);
            var samples = piece.AsSpan(0, count * source.Channels);
            source.Fill(start, count, samples);
            Normalizer.ApplyGain(samples, track.GainDb);

            if (limiter is null)
            {
                Limiter.Clamp(samples, track.CeilingLinear);
                writer.Write(samples);
            }
            else
            {
                var written = limiter.Process(samples);
                writer.Write(samples[..(written * source.Channels)]);
            }
        }

        if (limiter is not null)
        {
            // The look-ahead is shorter than a piece, so the tail fits in the same buffer.
            var written = limiter.Flush(piece);
            writer.Write(piece.AsSpan(0, written * source.Channels));
        }
    }

    /// <summary>
    /// The full path of the output folder. A folder that is still relative after the expansion (an unknown
    /// <c>%VARIABLE%</c> is left as typed, "clips" or "C:clips" depend on the current directory) is invalid: the
    /// clip would land where nobody looks. The ArgumentException sends the clip to the rescue folder.
    /// </summary>
    internal static string ExpandFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return Path.GetFullPath(ExportOptions.DefaultOutputFolder());

        var expanded = Environment.ExpandEnvironmentVariables(folder.Trim());
        if (!Path.IsPathFullyQualified(expanded))
            throw new ArgumentException($"The output folder '{expanded}' is not a full path.", nameof(folder));

        return Path.GetFullPath(expanded);
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException
            or ArgumentException; // an invalid path, for example; the options were validated before

    private static void Validate(ExportOptions options)
    {
        if (options.Format == ExportFormat.Mp3 && options.Mp3BitrateKbps is not (128 or 192 or 320))
            throw new ArgumentException("The MP3 bitrate must be 128, 192 or 320 kbps.", nameof(options));
        if (double.IsNaN(options.SystemGainDb) || double.IsNaN(options.MicGainDb))
            throw new ArgumentException("A gain is not a number.", nameof(options));
    }
}
