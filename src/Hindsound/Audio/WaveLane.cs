using Hindsound.Export;

namespace Hindsound.Audio;

/// <summary>
/// The summary of one source's past audio for the live wave: one byte per 100 ms. It is held by whoever draws, only
/// while the wave is on screen, and reads the track without any lock, so the audio threads never know about it.
/// <para>
/// Slices sit on the absolute timeline (frame / 4800), not on the edge of the window, and a track only ever appends,
/// so a slice read once never changes. Two cursors read the rest: one follows the new audio forward, the other walks
/// the past from the most recent slice to the oldest, a little at each pass, so the wave fills from right to left.
/// </para>
/// </summary>
public sealed class WaveLane
{
    /// <summary>Frames in one slice: 100 ms.</summary>
    public const int FramesPerSlice = InternalFormat.SampleRate / 10;

    /// <summary>The level of a slice at the top of the scale. A level of 0 is silence that is held.</summary>
    public const byte FullLevel = 254;

    /// <summary>Nothing to show here: not held, not read yet, or touched by the writer. Not the same as silence.</summary>
    public const byte Empty = 255;

    /// <summary>Samples one pass may read over both lanes: about 8 ms. 5:00 with the microphone takes 2 passes, 30:00 takes 9.</summary>
    public const long PassSamples = 32_000_000;

    private SourceTrack? _track;
    private byte[] _levels = [];
    private int _bufferFrames;

    // The next frame the forward cursor reads. The slices it has reached are the top of what is known.
    private long _nextFrame;

    // The oldest slice known. Slices between it and the forward cursor are all known, which is all there is to remember.
    private long _lowSlice;

    // Below this slice the track has nothing left: found out once, not read again at each pass.
    private long _goneBelowSlice;

    private long _wantedLowSlice;
    private long _readUpTo;
    private long _trackEnd;
    private long _windowEnd;

    /// <summary>A track is being followed.</summary>
    public bool HasTrack => _track is not null;

    /// <summary>Nothing is left to read for now. Another pass would change nothing until new audio comes.</summary>
    public bool IsCaughtUp => _track is null || (_nextFrame >= _readUpTo && _lowSlice <= PastStopSlice);

    private long HighSlice => (_nextFrame + FramesPerSlice - 1) / FramesPerSlice;

    // The slices kept share a ring, so the past never walks far enough to land on the slot of a newer slice.
    private long PastStopSlice => Math.Max(Math.Max(_wantedLowSlice, _goneBelowSlice), HighSlice - _levels.Length);

    /// <summary>The slice a frame falls in.</summary>
    public static long SliceOf(long frame) => frame / FramesPerSlice;

    /// <summary>
    /// One pass for both lanes: the new audio first, so the right edge stays live, and then the past, shared so that
    /// both lanes fill at the same pace. Allocates nothing once the lanes are following their tracks.
    /// </summary>
    /// <param name="facts">The engine's facts, read once for the pass.</param>
    /// <param name="system">The lane of the PC sound.</param>
    /// <param name="mic">The lane of the microphone.</param>
    /// <param name="budget">Samples this pass may read. One slice more at most: a slice is always read whole.</param>
    public static void CatchUp(in WaveFacts facts, WaveLane system, WaveLane mic, long budget = PassSamples)
    {
        system.Align(facts.System, facts);
        mic.Align(facts.Mic, facts);
        system.ReadNew(ref budget);
        mic.ReadNew(ref budget);

        var systemShare = budget / 2;
        var micShare = budget - systemShare;
        system.ReadPast(ref systemShare);
        micShare += systemShare;
        mic.ReadPast(ref micShare);
        system.ReadPast(ref micShare);
    }

    /// <summary>
    /// The level of a slice, 0 to <see cref="FullLevel"/>, or <see cref="Empty"/>. Past the end of the track but inside
    /// the window it is silence: a loopback delivers nothing while the PC is mute, and an application that closed
    /// delivers nothing either, yet time goes on and the zeros will be there when the next packet comes.
    /// </summary>
    public byte LevelOf(long slice)
    {
        if (_track is null || slice < _lowSlice)
        {
            return Empty;
        }

        if (slice < HighSlice)
        {
            return _levels[slice % _levels.Length];
        }

        var start = slice * FramesPerSlice;
        return start >= _trackEnd && start < _windowEnd ? (byte)0 : Empty;
    }

    /// <summary>Takes a peak in stored units to the scale of the levels, the dB scale of a clip's bars.</summary>
    internal static byte ToLevel(int peak)
    {
        return (byte)MathF.Round(ClipPeaks.ToBar(peak / 32767f) * FullLevel);
    }

    /// <summary>
    /// Follows this track over this window. Another track, or another duration, starts over: the frames are not those
    /// of the same audio any more, or the slices do not fit. A cursor the window has left behind starts over too: the
    /// audio it would read is gone, and the slices it knows are too old to show.
    /// </summary>
    internal void Align(SourceTrack? track, in WaveFacts facts)
    {
        if (track is null)
        {
            _track = null;
            return;
        }

        var restart = !ReferenceEquals(track, _track) || facts.BufferFrames != _bufferFrames;
        if (restart)
        {
            _track = track;
            _bufferFrames = facts.BufferFrames;
            var slices = facts.BufferFrames / FramesPerSlice + 2;
            if (_levels.Length != slices)
            {
                _levels = new byte[slices];
            }
        }

        _trackEnd = track.EndFrame;
        _windowEnd = facts.End;
        _readUpTo = Math.Min(_trackEnd, facts.End);
        _wantedLowSlice = SliceOf(Math.Max(facts.Start, track.OldestFrame));
        if (restart || SliceOf(_nextFrame) < _wantedLowSlice)
        {
            StartFromTheEnd();
        }

        _lowSlice = Math.Max(_lowSlice, _wantedLowSlice);
    }

    // At the start of the slice in progress, or at the start of the window when the track stopped before it.
    private void StartFromTheEnd()
    {
        _nextFrame = Math.Max(SliceOf(_readUpTo), _wantedLowSlice) * FramesPerSlice;
        _lowSlice = SliceOf(_nextFrame);
        _goneBelowSlice = 0;
    }

    /// <summary>Reads forward, to the end of what is there. The slice in progress grows from one pass to the next.</summary>
    internal void ReadNew(ref long budget)
    {
        if (_track is not { } track)
        {
            return;
        }

        while (_nextFrame < _readUpTo && budget > 0)
        {
            var slice = SliceOf(_nextFrame);
            var stop = Math.Min((slice + 1) * FramesPerSlice, _readUpTo);
            var frames = (int)(stop - _nextFrame);
            var at = slice % _levels.Length;
            if (_nextFrame % FramesPerSlice == 0)
            {
                _levels[at] = 0;
            }

            var peak = track.PeakOfFrames(_nextFrame, frames);
            if (peak == SourceTrack.PeakGone)
            {
                _levels[at] = Empty;
            }
            else if (_levels[at] != Empty)
            {
                _levels[at] = Math.Max(_levels[at], ToLevel(peak));
            }

            budget -= frames * track.Channels;
            _nextFrame = stop;
        }

        _lowSlice = Math.Max(_lowSlice, HighSlice - _levels.Length);
    }

    /// <summary>Reads the past, from the most recent slice to the oldest, whole slices only.</summary>
    internal void ReadPast(ref long budget)
    {
        if (_track is not { } track)
        {
            return;
        }

        var stop = PastStopSlice;
        while (_lowSlice > stop && budget > 0)
        {
            var slice = _lowSlice - 1;
            var peak = track.PeakOfFrames(slice * FramesPerSlice, FramesPerSlice);
            budget -= FramesPerSlice * track.Channels;
            if (peak == SourceTrack.PeakGone)
            {
                // A slice the writer touched is not kept: nothing else ever reads it again, so a wrong value would stay.
                // Older slices are gone too, since the writer overwrites from the oldest on.
                _goneBelowSlice = _lowSlice;
                return;
            }

            _levels[slice % _levels.Length] = ToLevel(peak);
            _lowSlice = slice;
        }
    }
}
