using Hindsound.Audio;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>The silence of the PC's sound while the preview plays, and which capture reads it.</summary>
public sealed class PulsePlaybackSilenceTests
{
    private static readonly PulseServerState Server = new(
        "speakers",
        "mic",
        [new PulseSink("speakers", "Speakers", "speakers.monitor")],
        [new PulseSource("speakers.monitor", "Monitor of Speakers", IsMonitor: true), new PulseSource("mic", "Microphone", IsMonitor: false)]);

    private long _now = 5_000;

    [Fact]
    public void The_capture_is_silent_from_the_hold_until_the_tail_after_its_release()
    {
        var silence = new PulsePlaybackSilence(() => _now);
        Assert.False(silence.IsSilent);

        var hold = silence.Silence(TimeSpan.FromMinutes(1));
        Assert.True(silence.IsSilent);
        _now += 30_000;
        Assert.True(silence.IsSilent);

        hold.Release();
        _now += PulsePlaybackSilence.TailMs - 1;
        Assert.True(silence.IsSilent);
        _now += 1;
        Assert.False(silence.IsSilent);
    }

    [Fact]
    public void Two_holds_keep_the_silence_until_the_last_is_released_and_a_second_release_adds_nothing()
    {
        var silence = new PulsePlaybackSilence(() => _now);
        var first = silence.Silence(TimeSpan.FromMinutes(1));
        var second = silence.Silence(TimeSpan.FromMinutes(1));

        first.Release();
        _now += PulsePlaybackSilence.TailMs;
        Assert.True(silence.IsSilent);

        second.Release();
        _now += PulsePlaybackSilence.TailMs;
        first.Release();
        second.Release();
        Assert.False(silence.IsSilent);
    }

    [Fact]
    public void A_hold_never_released_lifts_at_its_deadline_and_releasing_it_later_adds_no_tail()
    {
        var silence = new PulsePlaybackSilence(() => _now);
        var hold = silence.Silence(TimeSpan.FromSeconds(2));

        _now += 2_000;
        Assert.False(silence.IsSilent);
        hold.Release();
        Assert.False(silence.IsSilent);
    }

    [Fact]
    public void While_silenced_a_packet_says_it_is_silent_and_keeps_the_other_flags()
    {
        Assert.Equal(PacketFlags.TimestampError, PulseFormats.FlagsOf(hole: false, silenced: false));
        Assert.Equal(PacketFlags.TimestampError | PacketFlags.Silent, PulseFormats.FlagsOf(hole: false, silenced: true));
        Assert.Equal(PacketFlags.TimestampError | PacketFlags.Silent, PulseFormats.FlagsOf(hole: true, silenced: true));
        Assert.Equal(PacketFlags.TimestampError | PacketFlags.Silent, PulseFormats.FlagsOf(hole: true, silenced: false));
    }

    [Fact]
    public void Only_the_pc_s_sound_asked_to_leave_the_app_out_reads_the_silence_and_the_microphone_never_does()
    {
        var silence = new PulsePlaybackSilence();

        Assert.Same(silence, PulseCaptureRules.SilenceFor(new CaptureRequest(AudioFlow.System, "speakers.monitor", ExcludeOwnProcess: true), silence));
        Assert.Null(PulseCaptureRules.SilenceFor(new CaptureRequest(AudioFlow.System, "speakers.monitor", ExcludeOwnProcess: false), silence));
        Assert.Null(PulseCaptureRules.SilenceFor(new CaptureRequest(AudioFlow.Mic, "mic", ExcludeOwnProcess: true), silence));
        Assert.Null(PulseCaptureRules.SilenceFor(new CaptureRequest(AudioFlow.Mic, "mic", ExcludeOwnProcess: false), silence));
    }

    [Fact]
    public void With_the_silence_leaving_the_app_s_sounds_out_of_the_pc_s_sound_is_accepted_and_nothing_else_more()
    {
        Assert.Null(PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "speakers.monitor", true), canSilence: true));
        Assert.IsType<PlatformNotSupportedException>(PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "speakers.monitor", true), canSilence: false));
        Assert.IsType<PlatformNotSupportedException>(PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.Mic, "mic", true), canSilence: true));
        Assert.IsType<PlatformNotSupportedException>(
            PulseCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "speakers.monitor", true, TargetProcessId: 42, TargetName: "Game"), canSilence: true));
    }

    [Fact]
    public void A_factory_with_the_silence_does_not_refuse_the_exclusion_and_one_without_still_does()
    {
        var request = new CaptureRequest(AudioFlow.System, "speakers.monitor", ExcludeOwnProcess: true);
        var with = new PulseCaptureFactory(_ => { }, () => Server, new PulsePlaybackSilence());
        var without = new PulseCaptureFactory(_ => { }, () => Server);

        var opening = with.OpenAsync(request);
        var refused = without.OpenAsync(request);

        Assert.True(refused.IsFaulted);
        Assert.Contains("cannot leave", refused.Exception!.InnerException!.Message);
        if (!OperatingSystem.IsLinux())
        {
            // Here it goes as far as the server, which only Linux reaches.
            Assert.True(opening.IsFaulted);
            Assert.Contains("Linux only", opening.Exception!.InnerException!.Message);
        }
        else
        {
            Assert.ThrowsAny<Exception>(() => opening.GetAwaiter().GetResult());
        }
    }
}
