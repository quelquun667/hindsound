using System.IO;
using System.Security;
using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// Turns a snapshot into file(s): gains, mix or two tracks, normalization, encoding, and a name that never
/// overwrites. If the output folder cannot be written, the clip goes to a rescue folder instead of being lost.
/// </summary>
public sealed class ClipSaver
{
    private readonly string _rescueFolder;
    private readonly Func<DateTime> _clock;

    /// <param name="rescueFolder">Where clips go when the output folder is not writable. Default: <see cref="DefaultRescueFolder"/>.</param>
    /// <param name="clock">The time used in file names. Default: the local time.</param>
    public ClipSaver(string? rescueFolder = null, Func<DateTime>? clock = null)
    {
        _rescueFolder = rescueFolder ?? DefaultRescueFolder;
        _clock = clock ?? (() => DateTime.Now);
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

        var frames = FrameCount(snapshot);
        if (frames == 0) throw new ArgumentException("The snapshot holds no audio.", nameof(snapshot));

        // One pass over the raw snapshot: the bars and the silence flag.
        var peaks = ClipPeaks.Compute(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var tracks = Render(snapshot, options, frames);
        cancellationToken.ThrowIfCancellationRequested();

        var baseName = FileNameTemplate.Render(options.FileNameTemplate, appName, _clock());
        var (paths, rescued) = WriteTracks(tracks, options, baseName);

        var size = paths.Sum(p => new FileInfo(p).Length);
        return new ClipResult(paths, TimeSpan.FromSeconds((double)frames / InternalFormat.SampleRate), size, peaks.IsSilent, peaks.Bars, rescued);
    }

    // ---------------------------------------------------------------- rendering

    private sealed record Track(string Suffix, float[] Samples, int Channels);

    private static int FrameCount(AudioSnapshot snapshot) =>
        Math.Max(snapshot.System.Length / InternalFormat.SystemChannels, snapshot.Mic.Length / InternalFormat.MicChannels);

    private static List<Track> Render(AudioSnapshot snapshot, ExportOptions options, int frames)
    {
        var systemGain = Normalizer.DbToLinear(options.SystemGainDb);
        var micGain = Normalizer.DbToLinear(options.MicGainDb);

        // The sources end at the same instant: a shorter one is padded with silence at the start.
        var systemOffset = frames - snapshot.System.Length / InternalFormat.SystemChannels;
        var micOffset = frames - snapshot.Mic.Length / InternalFormat.MicChannels;

        // Off still keeps the file under 0 dBFS; the other modes use the ceiling.
        var ceilingDb = options.Normalization == NormalizationMode.Off ? 0 : options.PeakCeilingDb;

        if (options.TrackMode == TrackMode.Mix)
        {
            var mix = new float[frames * 2];
            AddSystem(mix, snapshot.System, systemOffset, systemGain);
            AddMic(mix, snapshot.Mic, micOffset, micGain);
            Normalizer.Normalize(mix, 2, options.Normalization, options.TargetLufs, ceilingDb);
            return [new Track(string.Empty, mix, 2)];
        }

        var tracks = new List<Track>();
        if (snapshot.System.Length > 0)
        {
            var system = new float[frames * 2];
            AddSystem(system, snapshot.System, systemOffset, systemGain);
            tracks.Add(new Track("_system", system, 2));
        }

        if (snapshot.Mic.Length > 0)
        {
            var mic = new float[frames];
            for (var i = 0; i < snapshot.Mic.Length; i++) mic[micOffset + i] = snapshot.Mic[i] * micGain;
            tracks.Add(new Track("_mic", mic, 1));
        }

        if (options.LinkedTrackGain && tracks.Count == 2)
        {
            var gainDb = LinkedGainDb(tracks[0].Samples, tracks[1].Samples, options);
            foreach (var track in tracks) Normalizer.Apply(track.Samples, track.Channels, gainDb, ceilingDb);
        }
        else
        {
            foreach (var track in tracks)
                Normalizer.Normalize(track.Samples, track.Channels, options.Normalization, options.TargetLufs, ceilingDb);
        }

        return tracks;
    }

    private static void AddSystem(float[] stereo, float[] system, int frameOffset, float gain)
    {
        var start = frameOffset * 2;
        for (var i = 0; i < system.Length; i++) stereo[start + i] += system[i] * gain;
    }

    private static void AddMic(float[] stereo, float[] mic, int frameOffset, float gain)
    {
        for (var i = 0; i < mic.Length; i++)
        {
            var value = mic[i] * gain;
            var index = (frameOffset + i) * 2;
            stereo[index] += value;
            stereo[index + 1] += value;
        }
    }

    /// <summary>
    /// One gain for both tracks. Peak: the louder of the two peaks reaches the ceiling. Loudness: the loudness of
    /// the two tracks mixed together (the mic centred), measured piece by piece so that no mix is held in memory.
    /// </summary>
    private static double LinkedGainDb(float[] system, float[] mic, ExportOptions options)
    {
        switch (options.Normalization)
        {
            case NormalizationMode.Peak:
                return Normalizer.PeakGainDb(Math.Max(Limiter.Peak(system), Limiter.Peak(mic)), options.PeakCeilingDb);

            case NormalizationMode.Loudness:
                const int pieceFrames = 4800;
                var meter = new LoudnessMeter(2);
                var piece = new float[pieceFrames * 2];
                for (var start = 0; start < mic.Length; start += pieceFrames)
                {
                    var count = Math.Min(pieceFrames, mic.Length - start);
                    for (var i = 0; i < count; i++)
                    {
                        piece[2 * i] = system[2 * (start + i)] + mic[start + i];
                        piece[2 * i + 1] = system[2 * (start + i) + 1] + mic[start + i];
                    }

                    meter.Add(piece.AsSpan(0, count * 2));
                }

                return Normalizer.LoudnessGainDb(meter.Finish(), options.TargetLufs);

            default:
                return 0;
        }
    }

    // ---------------------------------------------------------------- writing

    private (IReadOnlyList<string> Paths, bool Rescued) WriteTracks(List<Track> tracks, ExportOptions options, string baseName)
    {
        var folder = ExpandFolder(options.OutputFolder);
        try
        {
            return (WriteInto(folder, tracks, options, baseName), false);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            return (WriteInto(_rescueFolder, tracks, options, baseName), true);
        }
    }

    /// <summary>Writes every track into the folder, or none: a failure removes what was written.</summary>
    private static string[] WriteInto(string folder, List<Track> tracks, ExportOptions options, string baseName)
    {
        Directory.CreateDirectory(folder);
        var extension = options.Format == ExportFormat.Mp3 ? ".mp3" : ".wav";
        var paths = FileNameTemplate.ReserveUniquePaths(folder, baseName, tracks.Select(t => t.Suffix).ToArray(), extension);

        var created = new List<string>();
        try
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                created.Add(paths[i]);
                if (options.Format == ExportFormat.Mp3)
                    Encoders.WriteMp3(paths[i], tracks[i].Samples, tracks[i].Channels, options.Mp3BitrateKbps);
                else
                    Encoders.WriteWav(paths[i], tracks[i].Samples, tracks[i].Channels);
            }
        }
        catch
        {
            foreach (var path in created)
            {
                try { File.Delete(path); } catch (Exception) { /* best effort */ }
            }

            throw;
        }

        return paths;
    }

    private static string ExpandFolder(string folder) =>
        string.IsNullOrWhiteSpace(folder) ? ExportOptions.DefaultOutputFolder() : Environment.ExpandEnvironmentVariables(folder.Trim());

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
