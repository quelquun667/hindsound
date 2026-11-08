using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

/// <summary>Real tracks with made-up audio, and what the lane should say worked out the slow way.</summary>
internal static class WaveRig
{
    public const int Slice = WaveLane.FramesPerSlice;
    public const int Rate = InternalFormat.SampleRate;

    /// <summary>Audio whose loudness changes from slice to slice, so a slice read in the wrong place shows.</summary>
    public static void Write(SourceTrack track, int frames, int seed)
    {
        var random = new Random(seed);
        var samples = new float[frames * track.Channels];
        var loudness = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            if (i % (Slice / 4) == 0)
            {
                loudness = (float)(0.001 * Math.Pow(900, random.NextDouble()));
            }

            samples[i] = loudness * (float)(random.NextDouble() * 2 - 1);
        }

        track.AppendContinuation(samples);
    }

    public static WaveFacts FactsOf(SourceTrack? system, int bufferFrames, SourceTrack? mic = null, long? end = null, SourceChange[]? changes = null)
    {
        var last = Math.Max(system?.EndFrame ?? 0, mic?.EndFrame ?? 0);
        var oldest = Math.Min(system?.OldestFrame ?? long.MaxValue, mic?.OldestFrame ?? long.MaxValue);
        var stop = end ?? last;
        return new WaveFacts(system, mic, Math.Max(stop - bufferFrames, oldest), stop, bufferFrames, changes ?? []);
    }

    /// <summary>
    /// The fewest bytes a few runs allocate. A real allocation shows in every run. The counter of a thread has been seen
    /// to jump now and then with nothing allocated, in about 1 measure in 80 on a busy machine, and only in one run.
    /// </summary>
    public static long LeastAllocated(Action run)
    {
        var least = long.MaxValue;
        for (var attempt = 0; attempt < 3 && least > 0; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            run();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }

    /// <summary>The level of a slice from a plain copy of the frames still held.</summary>
    public static byte Raw(SourceTrack track, long slice)
    {
        var copy = new float[Slice * track.Channels];
        track.CopyFrames(slice * Slice, copy);
        var peak = 0;
        foreach (var sample in copy)
        {
            peak = Math.Max(peak, Math.Abs((int)SampleCodec.ToShort(sample)));
        }

        return WaveLane.ToLevel(peak);
    }

    public static void ReadAll(WaveLane lane, in WaveFacts facts, long budget = WaveLane.PassSamples)
    {
        var other = new WaveLane();
        for (var pass = 0; pass < 100_000; pass++)
        {
            WaveLane.CatchUp(facts, lane, other, budget);
            if (lane.IsCaughtUp)
            {
                return;
            }
        }

        Assert.Fail("The lane never caught up");
    }

    public static void AssertLikeRaw(WaveLane lane, SourceTrack track, in WaveFacts facts)
    {
        var first = WaveLane.SliceOf(Math.Max(facts.Start, track.OldestFrame));
        var last = WaveLane.SliceOf(Math.Min(facts.End, track.EndFrame) - 1);
        Assert.True(last >= first);
        for (var slice = first; slice <= last; slice++)
        {
            Assert.Equal(Raw(track, slice), lane.LevelOf(slice));
        }
    }
}

public class WaveLaneTests
{
    private const int Slice = WaveRig.Slice;
    private const int Rate = WaveRig.Rate;

    [Theory]
    [InlineData(1)]
    [InlineData(9_600)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    [InlineData(WaveLane.PassSamples)]
    public void It_says_what_the_slow_way_says_whatever_the_budget(long budget)
    {
        var track = new SourceTrack(2, 10 * Rate, 0);
        WaveRig.Write(track, 8 * Rate + 1234, 1);
        var facts = WaveRig.FactsOf(track, 10 * Rate);
        var lane = new WaveLane();

        WaveRig.ReadAll(lane, facts, budget);

        WaveRig.AssertLikeRaw(lane, track, facts);
    }

    [Theory]
    [InlineData(9_600)]
    [InlineData(40_000)]
    [InlineData(1_000_000)]
    public void It_says_the_same_with_audio_coming_in_between_the_passes(long budget)
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        var lane = new WaveLane();
        var other = new WaveLane();
        var random = new Random(4);
        for (var step = 0; step < 300; step++)
        {
            WaveRig.Write(track, random.Next(1, 3000), step);
            WaveLane.CatchUp(WaveRig.FactsOf(track, 10 * Rate), lane, other, budget);
        }

        var facts = WaveRig.FactsOf(track, 10 * Rate);
        WaveRig.ReadAll(lane, facts);

        WaveRig.AssertLikeRaw(lane, track, facts);
    }

    [Fact]
    public void One_pass_stays_within_its_budget_but_for_one_slice()
    {
        var system = new SourceTrack(2, 20 * Rate, 0);
        var mic = new SourceTrack(1, 20 * Rate, 0);
        WaveRig.Write(system, 20 * Rate, 1);
        WaveRig.Write(mic, 20 * Rate, 2);
        var facts = WaveRig.FactsOf(system, 20 * Rate, mic);
        var systemLane = new WaveLane();
        var micLane = new WaveLane();
        var known = 0L;

        for (var pass = 0; pass < 10; pass++)
        {
            const long Budget = 100_000;
            WaveLane.CatchUp(facts, systemLane, micLane, Budget);

            var now = Known(systemLane, 2) + Known(micLane, 1);
            Assert.InRange(now - known, 1, Budget + Slice * 2);
            known = now;
        }

        static long Known(WaveLane lane, int channels)
        {
            var count = 0L;
            for (var slice = 0; slice < 200; slice++)
            {
                count += lane.LevelOf(slice) == WaveLane.Empty ? 0 : Slice * channels;
            }

            return count;
        }
    }

    [Fact]
    public void A_pass_spends_what_it_reads_from_the_budget_it_was_given()
    {
        var track = new SourceTrack(2, 20 * Rate, 0);
        WaveRig.Write(track, 5 * Rate, 1);
        var lane = new WaveLane();
        lane.Align(track, WaveRig.FactsOf(track, 20 * Rate));

        var budget = 50_000L;
        lane.ReadPast(ref budget);

        // 6 slices of 9 600 samples use it up, and the sixth goes past it by less than one slice.
        Assert.Equal(50_000 - 6 * 9_600, budget);
        Assert.True(lane.LevelOf(44) != WaveLane.Empty);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(43));
    }

    [Fact]
    public void Memory_of_5_00_with_the_mic_takes_2_passes_and_30_00_takes_9_at_this_budget()
    {
        // The same ratio as the real thing, on a fraction of its memory.
        const int Scale = 100;
        Assert.Equal(2, PassesFor(5 * 60 * Rate / Scale, WaveLane.PassSamples / Scale));
        Assert.Equal(9, PassesFor(30 * 60 * Rate / Scale, WaveLane.PassSamples / Scale));

        static int PassesFor(int frames, long budget)
        {
            var system = new SourceTrack(2, frames, 0);
            var mic = new SourceTrack(1, frames, 0);
            system.AppendContinuation(new float[frames * 2]);
            mic.AppendContinuation(new float[frames]);
            var facts = WaveRig.FactsOf(system, frames, mic);
            var systemLane = new WaveLane();
            var micLane = new WaveLane();
            var passes = 0;
            do
            {
                WaveLane.CatchUp(facts, systemLane, micLane, budget);
                passes++;
            }
            while (!systemLane.IsCaughtUp || !micLane.IsCaughtUp);

            return passes;
        }
    }

    [Fact]
    public void The_past_fills_from_the_most_recent_slice_to_the_oldest()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(track, 10 * Rate, 1);
        var facts = WaveRig.FactsOf(track, 10 * Rate);
        var lane = new WaveLane();
        var other = new WaveLane();
        var oldestSeen = 100L;

        for (var pass = 0; pass == 0 || (pass < 100 && !lane.IsCaughtUp); pass++)
        {
            WaveLane.CatchUp(facts, lane, other, 2 * 2 * Slice);

            var oldest = 100L;
            for (var slice = 99; slice >= 0 && lane.LevelOf(slice) != WaveLane.Empty; slice--)
            {
                oldest = slice;
            }

            Assert.True(oldest < oldestSeen, "Nothing new, or a hole on the right");
            for (var slice = 0; slice < oldest; slice++)
            {
                Assert.Equal(WaveLane.Empty, lane.LevelOf(slice));
            }

            oldestSeen = oldest;
        }

        Assert.Equal(0, oldestSeen);
    }

    [Fact]
    public void Both_lanes_fill_at_the_same_pace()
    {
        var system = new SourceTrack(2, 10 * Rate, 0);
        var mic = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(system, 10 * Rate, 1);
        WaveRig.Write(mic, 10 * Rate, 2);
        var systemLane = new WaveLane();
        var micLane = new WaveLane();

        WaveLane.CatchUp(WaveRig.FactsOf(system, 10 * Rate, mic), systemLane, micLane, 200_000);

        // The same share of samples is twice the time on the mono one, and neither waits for the other.
        Assert.NotEqual(WaveLane.Empty, systemLane.LevelOf(90));
        Assert.NotEqual(WaveLane.Empty, micLane.LevelOf(90));
        Assert.Equal(WaveLane.Empty, systemLane.LevelOf(50));
        Assert.Equal(WaveLane.Empty, micLane.LevelOf(50));
    }

    [Fact]
    public void A_budget_used_up_by_the_new_audio_of_the_first_lane_comes_back_at_the_next_pass()
    {
        var system = new SourceTrack(2, 10 * Rate, 0);
        var mic = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(system, Rate, 1);
        WaveRig.Write(mic, Rate, 2);
        var systemLane = new WaveLane();
        var micLane = new WaveLane();
        WaveRig.ReadAll(systemLane, WaveRig.FactsOf(system, 10 * Rate, mic));
        var facts = WaveRig.FactsOf(system, 10 * Rate, mic);
        WaveLane.CatchUp(facts, systemLane, micLane);
        WaveRig.Write(system, 2 * Rate, 3);
        WaveRig.Write(mic, 2 * Rate, 4);

        facts = WaveRig.FactsOf(system, 10 * Rate, mic);
        WaveLane.CatchUp(facts, systemLane, micLane, 1);
        Assert.False(micLane.IsCaughtUp);
        WaveLane.CatchUp(facts, systemLane, micLane);

        Assert.True(systemLane.IsCaughtUp);
        Assert.True(micLane.IsCaughtUp);
        WaveRig.AssertLikeRaw(micLane, mic, facts);
    }

    [Fact]
    public void Held_silence_is_level_zero_however_long_it_was()
    {
        var track = new SourceTrack(2, 2 * Rate, 0);
        track.PadTo(500 * Rate);
        var facts = WaveRig.FactsOf(track, 2 * Rate);
        var lane = new WaveLane();

        WaveRig.ReadAll(lane, facts);

        var last = WaveLane.SliceOf(facts.End - 1);
        for (var slice = last - 19; slice <= last; slice++)
        {
            Assert.Equal(0, lane.LevelOf(slice));
        }
    }

    [Fact]
    public void A_slice_in_progress_grows_and_a_full_one_never_changes()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        var lane = new WaveLane();
        var other = new WaveLane();
        track.AppendContinuation(Filled(Slice / 2, 0.01f));
        WaveLane.CatchUp(WaveRig.FactsOf(track, 10 * Rate), lane, other);
        var quiet = lane.LevelOf(0);

        track.AppendContinuation(Filled(Slice / 2, 0.5f));
        WaveLane.CatchUp(WaveRig.FactsOf(track, 10 * Rate), lane, other);
        var loud = lane.LevelOf(0);
        Assert.True(loud > quiet);
        Assert.Equal(WaveRig.Raw(track, 0), loud);

        track.AppendContinuation(Filled(Slice * 3, 0.001f));
        WaveLane.CatchUp(WaveRig.FactsOf(track, 10 * Rate), lane, other);
        Assert.Equal(loud, lane.LevelOf(0));

        static float[] Filled(int count, float value)
        {
            var samples = new float[count];
            Array.Fill(samples, value);
            return samples;
        }
    }

    [Fact]
    public void Levels_follow_the_scale_of_a_clips_bars()
    {
        Assert.Equal(0, WaveLane.ToLevel(0));
        Assert.Equal(0, WaveLane.ToLevel(1));
        Assert.Equal(WaveLane.FullLevel, WaveLane.ToLevel(32767));
        Assert.Equal(WaveLane.FullLevel, WaveLane.ToLevel(32768));

        // -30 dBFS is half way on a scale from -60 dBFS to 0.
        Assert.InRange(WaveLane.ToLevel(1036), 126, 128);
    }

    [Fact]
    public void Another_track_starts_over_and_nothing_shows_before_its_start()
    {
        var first = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(first, 5 * Rate, 1);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(first, 10 * Rate));
        Assert.NotEqual(WaveLane.Empty, lane.LevelOf(10));

        var second = new SourceTrack(1, 10 * Rate, 8 * Rate);
        WaveRig.Write(second, Rate, 2);
        var facts = WaveRig.FactsOf(second, 10 * Rate);
        WaveRig.ReadAll(lane, facts);

        Assert.Equal(WaveLane.Empty, lane.LevelOf(10));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(79));
        WaveRig.AssertLikeRaw(lane, second, facts);
    }

    [Fact]
    public void Another_track_over_the_same_frames_is_read_again()
    {
        // The mic turned off and on again between two passes: the frames overlap, and the audio is another.
        var first = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(first, 5 * Rate, 1);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(first, 10 * Rate));

        var second = new SourceTrack(1, 10 * Rate, 3 * Rate);
        WaveRig.Write(second, 2 * Rate, 2);
        var facts = WaveRig.FactsOf(second, 10 * Rate);
        WaveRig.ReadAll(lane, facts);

        WaveRig.AssertLikeRaw(lane, second, facts);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(29));
    }

    [Fact]
    public void A_new_start_with_frames_counted_from_zero_again_is_read_again()
    {
        var first = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(first, 8 * Rate, 1);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(first, 10 * Rate));

        var second = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(second, Rate, 2);
        var facts = WaveRig.FactsOf(second, 10 * Rate);
        WaveRig.ReadAll(lane, facts);

        WaveRig.AssertLikeRaw(lane, second, facts);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(30));
    }

    [Fact]
    public void No_track_forgets_everything()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(track, 5 * Rate, 1);
        var facts = WaveRig.FactsOf(track, 10 * Rate);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, facts);

        lane.Align(null, facts);

        Assert.False(lane.HasTrack);
        Assert.True(lane.IsCaughtUp);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(10));

        // The same track coming back has to be read again.
        lane.Align(track, facts);
        Assert.False(lane.IsCaughtUp);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(10));
    }

    [Fact]
    public void A_shorter_memory_moves_the_oldest_on_with_no_ghost_bars()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(track, 10 * Rate, 1);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 10 * Rate));

        track.Resize(4 * Rate);
        var facts = WaveRig.FactsOf(track, 4 * Rate);
        WaveRig.ReadAll(lane, facts);

        Assert.Equal(60, WaveLane.SliceOf(facts.Start));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(59));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(0));
        WaveRig.AssertLikeRaw(lane, track, facts);
    }

    [Fact]
    public void A_longer_memory_leaves_the_left_empty_and_going_back_says_nothing_false()
    {
        var track = new SourceTrack(1, 4 * Rate, 0);
        WaveRig.Write(track, 10 * Rate, 1);
        var heldFrom = track.OldestFrame;
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 4 * Rate));

        track.Resize(20 * Rate);
        var longer = WaveRig.FactsOf(track, 20 * Rate);
        WaveRig.ReadAll(lane, longer);

        Assert.Equal(heldFrom, longer.Start);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(WaveLane.SliceOf(heldFrom) - 1));
        WaveRig.AssertLikeRaw(lane, track, longer);

        track.Resize(4 * Rate);
        var back = WaveRig.FactsOf(track, 4 * Rate);
        WaveRig.ReadAll(lane, back);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(WaveLane.SliceOf(back.Start) - 1));
        WaveRig.AssertLikeRaw(lane, track, back);
    }

    [Fact]
    public void Away_for_longer_than_the_memory_it_forgets_and_starts_again_from_the_newest()
    {
        var track = new SourceTrack(1, 5 * Rate, 0);
        WaveRig.Write(track, 5 * Rate, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 5 * Rate));

        WaveRig.Write(track, 12 * Rate, 2);
        var facts = WaveRig.FactsOf(track, 5 * Rate);
        WaveLane.CatchUp(facts, lane, other, 2 * Slice);

        // The newest first, and nothing from before the absence.
        var newest = WaveLane.SliceOf(facts.End - 1);
        Assert.Equal(WaveRig.Raw(track, newest), lane.LevelOf(newest));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(newest - 40));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(10));

        WaveRig.ReadAll(lane, facts);
        Assert.Equal(WaveLane.Empty, lane.LevelOf(10));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(WaveLane.SliceOf(facts.Start) - 1));
        WaveRig.AssertLikeRaw(lane, track, facts);
    }

    [Fact]
    public void Away_for_less_than_the_memory_it_keeps_what_it_knows_and_reads_the_rest()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(track, 4 * Rate, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 10 * Rate));

        WaveRig.Write(track, 3 * Rate, 2);
        var facts = WaveRig.FactsOf(track, 10 * Rate);

        // Enough for the new audio only: what was read before does not need reading again.
        WaveLane.CatchUp(facts, lane, other, 3 * Rate + Slice);

        Assert.True(lane.IsCaughtUp);
        WaveRig.AssertLikeRaw(lane, track, facts);
    }

    [Fact]
    public void A_slice_touched_by_the_writer_is_not_kept_and_not_read_again()
    {
        var track = new SourceTrack(1, 5 * Rate, 0);
        WaveRig.Write(track, 8 * Rate + Slice / 2, 1);
        var touched = WaveLane.SliceOf(track.OldestFrame);
        Assert.NotEqual(0, track.OldestFrame % Slice);

        // A window reaching all the way to the oldest frame, half way through a slice.
        var facts = WaveRig.FactsOf(track, 6 * Rate + Slice);
        Assert.Equal(track.OldestFrame, facts.Start);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, facts);

        Assert.Equal(WaveLane.Empty, lane.LevelOf(touched));
        Assert.Equal(WaveRig.Raw(track, touched + 1), lane.LevelOf(touched + 1));
        Assert.True(lane.IsCaughtUp);
    }

    [Fact]
    public void A_source_that_delivers_nothing_is_silence_up_to_the_end_of_the_window()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(track, 2 * Rate + Slice / 2, 1);
        var lane = new WaveLane();

        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 10 * Rate, end: 5 * Rate));

        Assert.NotEqual(WaveLane.Empty, lane.LevelOf(20));
        Assert.Equal(WaveRig.Raw(track, 20), lane.LevelOf(20));
        Assert.Equal(0, lane.LevelOf(21));
        Assert.Equal(0, lane.LevelOf(49));

        // Past the window is the future, not silence.
        Assert.Equal(WaveLane.Empty, lane.LevelOf(50));
    }

    [Fact]
    public void A_source_silent_for_longer_than_the_memory_is_all_silence_and_picks_up_again()
    {
        var track = new SourceTrack(1, 3 * Rate, 0);
        WaveRig.Write(track, Rate, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveLane.CatchUp(WaveRig.FactsOf(track, 3 * Rate), lane, other);

        var facts = WaveRig.FactsOf(track, 3 * Rate, end: 20 * Rate);
        WaveLane.CatchUp(facts, lane, other);
        WaveLane.CatchUp(WaveRig.FactsOf(track, 3 * Rate, end: 21 * Rate), lane, other);

        Assert.True(lane.IsCaughtUp);
        Assert.Equal(0, lane.LevelOf(205));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(5));

        // The next packet comes with the zeros in front of it.
        track.PadTo(21 * Rate);
        WaveRig.Write(track, Rate, 2);
        var after = WaveRig.FactsOf(track, 3 * Rate);
        WaveRig.ReadAll(lane, after);
        WaveRig.AssertLikeRaw(lane, track, after);
    }

    [Fact]
    public void An_end_that_steps_back_a_little_breaks_nothing()
    {
        var track = new SourceTrack(1, 2 * Rate, 0);
        WaveRig.Write(track, 6 * Rate, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 2 * Rate));

        // A source can stall and come back: the window then ends up to 200 ms earlier than a moment before.
        var earlier = WaveRig.FactsOf(track, 2 * Rate, end: track.EndFrame - Rate / 5);
        WaveLane.CatchUp(earlier, lane, other);
        Assert.True(lane.IsCaughtUp);

        var again = WaveRig.FactsOf(track, 2 * Rate);
        WaveLane.CatchUp(again, lane, other);
        WaveRig.AssertLikeRaw(lane, track, again);
    }

    [Fact]
    public void A_memory_of_one_second_works()
    {
        var track = new SourceTrack(2, Rate, 0);
        WaveRig.Write(track, 3 * Rate + 77, 1);
        var facts = WaveRig.FactsOf(track, Rate);
        var lane = new WaveLane();

        WaveRig.ReadAll(lane, facts);

        WaveRig.AssertLikeRaw(lane, track, facts);
    }

    [Fact]
    public void A_track_that_starts_half_way_through_a_slice_shows_that_slice()
    {
        var track = new SourceTrack(1, 10 * Rate, Slice * 3 + 100);
        WaveRig.Write(track, Slice * 2, 1);
        var facts = WaveRig.FactsOf(track, 10 * Rate);
        var lane = new WaveLane();

        WaveRig.ReadAll(lane, facts);

        Assert.Equal(WaveRig.Raw(track, 3), lane.LevelOf(3));
        Assert.Equal(WaveRig.Raw(track, 4), lane.LevelOf(4));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(2));
    }
}

[Collection(nameof(TimingSensitive))]
public class WaveLaneBesideTheWriterTests
{
    private const int Rate = WaveRig.Rate;

    [Fact]
    public void A_pass_allocates_nothing_once_the_lanes_follow_their_tracks()
    {
        var system = new SourceTrack(2, 2 * Rate, 0);
        var mic = new SourceTrack(1, 2 * Rate, 0);
        WaveRig.Write(system, 2 * Rate, 1);
        WaveRig.Write(mic, 2 * Rate, 2);
        var packetSystem = new float[1920];
        var packetMic = new float[960];
        var systemLane = new WaveLane();
        var micLane = new WaveLane();
        WaveLane.CatchUp(WaveRig.FactsOf(system, 2 * Rate, mic), systemLane, micLane);
        Tick(0);
        Action ticks = () =>
        {
            for (var i = 1; i <= 500; i++)
            {
                Tick(i);
            }
        };

        Assert.Equal(0, WaveRig.LeastAllocated(ticks));

        // Once before measuring: the shared empty list is made the first time anything asks for it.
        void Tick(int i)
        {
            system.AppendContinuation(packetSystem);
            mic.AppendContinuation(packetMic);
            var facts = new WaveFacts(system, mic, system.EndFrame - 2 * Rate, system.EndFrame, 2 * Rate, []);
            WaveLane.CatchUp(facts, systemLane, micLane);
            _ = systemLane.LevelOf(i);
        }
    }

    [Fact]
    public void With_a_writer_at_full_speed_it_ends_and_holds_only_real_levels()
    {
        const float Loudness = 0.25f;
        var track = new SourceTrack(1, Rate / 2, 0);
        var stop = false;
        var writer = new Thread(() =>
        {
            var packet = new float[4800];
            Array.Fill(packet, Loudness);
            while (!Volatile.Read(ref stop))
            {
                track.AppendContinuation(packet);
            }
        });

        var level = WaveLane.ToLevel(SampleCodec.ToShort(Loudness));
        var lane = new WaveLane();
        var other = new WaveLane();
        writer.Start();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed.TotalSeconds < 1)
            {
                var facts = WaveRig.FactsOf(track, Rate / 2);
                WaveLane.CatchUp(facts, lane, other, 100_000);
                for (var slice = WaveLane.SliceOf(facts.End); slice > WaveLane.SliceOf(facts.End) - 4; slice--)
                {
                    var seen = lane.LevelOf(slice);
                    Assert.True(seen == level || seen == WaveLane.Empty || seen == 0, $"Level {seen}");
                }
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }
    }
}
