using Hindsound.Audio;
using Hindsound.Mac.Audio.Capture;

namespace Hindsound.Mac.Tests.Audio.Capture;

/// <summary>What the capture of the Mac decides without Core Audio: run on every system.</summary>
public sealed unsafe class MacCaptureLogicTests
{
    [Fact]
    public void ATapIsReadFromTheLastChannelsOfOneBuffer()
    {
        // The Null Audio Device's two channels (zeros), then the tap's two, interleaved in one buffer of four.
        float[] data = [0, 0, 1, 2, 0, 0, 3, 4, 0, 0, 5, 6];
        var destination = new float[16];
        fixed (float* samples = data)
        {
            var list = new AudioBufferList { NumberBuffers = 1, First = new AudioBuffer { NumberChannels = 4, DataByteSize = 48, Data = (nint)samples } };
            Assert.Equal(4, HalBuffers.TotalChannels(&list));
            var frames = HalBuffers.Extract(&list, HalBuffers.TotalChannels(&list) - 2, 2, destination);
            Assert.Equal(3, frames);
        }

        Assert.Equal([1, 2, 3, 4, 5, 6], destination[..6]);
    }

    [Fact]
    public void ATapIsReadFromItsOwnBufferAfterTheMainDevice()
    {
        float[] main = [9, 9, 9, 9];
        float[] tap = [1, 2, 3, 4];
        var destination = new float[8];
        fixed (float* first = main)
        fixed (float* second = tap)
        {
            var buffers = stackalloc AudioBuffer[2];
            buffers[0] = new AudioBuffer { NumberChannels = 2, DataByteSize = 16, Data = (nint)first };
            buffers[1] = new AudioBuffer { NumberChannels = 2, DataByteSize = 16, Data = (nint)second };
            var bytes = stackalloc byte[sizeof(uint) + 4 + (2 * sizeof(AudioBuffer))];
            var list = (AudioBufferList*)bytes;
            list->NumberBuffers = 2;
            (&list->First)[0] = buffers[0];
            (&list->First)[1] = buffers[1];
            Assert.Equal(2, HalBuffers.Extract(list, 2, 2, destination));
        }

        Assert.Equal([1, 2, 3, 4], destination[..4]);
    }

    [Fact]
    public void AMicrophoneIsReadFromItsFirstChannelsAndTheCopyStopsAtTheDestination()
    {
        float[] data = [1, 10, 2, 20, 3, 30];
        var destination = new float[2];
        fixed (float* samples = data)
        {
            var list = new AudioBufferList { NumberBuffers = 1, First = new AudioBuffer { NumberChannels = 2, DataByteSize = 24, Data = (nint)samples } };
            Assert.Equal(2, HalBuffers.Extract(&list, 0, 1, destination));
        }

        Assert.Equal([1, 2], destination);
    }

    [Fact]
    public void AChannelWithNoDataIsSilence()
    {
        var destination = new float[] { 7, 7, 7, 7 };
        var list = new AudioBufferList { NumberBuffers = 1, First = new AudioBuffer { NumberChannels = 2, DataByteSize = 16, Data = 0 } };
        Assert.Equal(2, HalBuffers.Extract(&list, 0, 2, destination));
        Assert.Equal([0, 0, 0, 0], destination);
    }

    [Fact]
    public void ThePositionCountsFromTheFirstSampleTimeAndAJumpIsADiscontinuity()
    {
        var stamper = new PacketStamper(() => 1_000_000, () => 0, 1);
        var time = new AudioTimeStamp { SampleTime = 5000, Flags = 1 };
        Assert.Equal((0L, 0L, PacketFlags.TimestampError), stamper.Stamp(time, 512));
        time.SampleTime = 5512;
        Assert.Equal(512, stamper.Stamp(time, 512).Position);
        time.SampleTime = 7000;
        var (position, _, flags) = stamper.Stamp(time, 512);
        Assert.Equal(2000, position);
        Assert.True(flags.HasFlag(PacketFlags.DataDiscontinuity));
    }

    [Fact]
    public void WithoutASampleTimeThePositionCountsTheFrames()
    {
        var stamper = new PacketStamper(() => 1, () => 0, 1);
        var time = new AudioTimeStamp();
        stamper.Stamp(time, 100);
        Assert.Equal(100, stamper.Stamp(time, 100).Position);
    }

    [Fact]
    public void TheStampIsTheEnginesNowLessThePacketsAgeOnTheHostClock()
    {
        // 24 MHz host ticks (125/3 ns each): 240 000 ticks are 10 ms, 100 000 engine ticks.
        var stamper = new PacketStamper(() => 50_000_000, () => 1_240_000, 125.0 / 3);
        var time = new AudioTimeStamp { HostTime = 1_000_000, Flags = 2 };
        var (_, qpc, flags) = stamper.Stamp(time, 512);
        Assert.Equal(50_000_000 - 100_000, qpc);
        Assert.Equal(PacketFlags.None, flags);
    }

    [Fact]
    public void ExactZerosFromTheStartAreNoticedOnceAtTheLimit()
    {
        var watch = new ExactZeroWatch(1000);
        var zeros = new float[1200];
        Assert.False(watch.Feed(zeros.AsSpan(0, 1200), 600));
        Assert.True(watch.Feed(zeros.AsSpan(0, 1000), 500));
        Assert.False(watch.Feed(zeros, 600));
    }

    [Fact]
    public void ASingleSampleThatIsNotZeroMeansThePermissionWorks()
    {
        var watch = new ExactZeroWatch(1000);
        var samples = new float[800];
        samples[799] = 1e-9f;
        Assert.False(watch.Feed(samples, 400));
        Assert.False(watch.Feed(new float[2000], 1000));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(7, -1)]
    [InlineData(-3, -1)]
    public void ThePreflightReadsAsAState(int preflight, int state) => Assert.Equal((PermissionState)state, MacCaptureRules.StateOf(preflight));

    [Fact]
    public void EachSideHasItsServiceAndItsText()
    {
        Assert.Equal("kTCCServiceAudioCapture", MacCaptureRules.ServiceOf(AudioFlow.System));
        Assert.Equal("kTCCServiceMicrophone", MacCaptureRules.ServiceOf(AudioFlow.Mic));
        Assert.Contains("Screen & System Audio Recording", MacCaptureRules.DenialText(AudioFlow.System), StringComparison.Ordinal);
        Assert.Contains("Microphone", MacCaptureRules.DenialText(AudioFlow.Mic), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMacTextsSayTheMacAndNeverThePc()
    {
        var texts = new[]
        {
            MacCaptureRules.DenialText(AudioFlow.System),
            MacCaptureRules.DenialText(AudioFlow.Mic),
            MacCaptureRules.PendingText(PermissionState.Undecided, AudioFlow.System, false)!,
            MacCaptureRules.PendingText(PermissionState.Undecided, AudioFlow.Mic, false)!,
            MacCaptureRules.ZerosText(PermissionState.Granted, AudioFlow.System),
            MacCaptureRules.ZerosText(PermissionState.Granted, AudioFlow.Mic),
        };

        Assert.All(texts, text => Assert.DoesNotContain("PC", text, StringComparison.Ordinal));
        Assert.StartsWith("macOS refuses this app the sound of the Mac: ", MacCaptureRules.DenialText(AudioFlow.System), StringComparison.Ordinal);
        Assert.Contains("the sound of the Mac opened", MacCaptureRules.ZerosText(PermissionState.Unknown, AudioFlow.System), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUndecidedPermissionIsNamedOnceWithWhereToGiveIt()
    {
        var system = MacCaptureRules.PendingText(PermissionState.Undecided, AudioFlow.System, false);
        var mic = MacCaptureRules.PendingText(PermissionState.Undecided, AudioFlow.Mic, false);

        Assert.NotNull(system);
        Assert.Contains("has not given this app the sound of the Mac yet", system, StringComparison.Ordinal);
        Assert.Contains("System Settings > Privacy & Security > Screen & System Audio Recording", system, StringComparison.Ordinal);
        Assert.NotNull(mic);
        Assert.Contains("has not given this app the microphone yet", mic, StringComparison.Ordinal);
        Assert.Contains("System Settings > Privacy & Security > Microphone", mic, StringComparison.Ordinal);
        Assert.Null(MacCaptureRules.PendingText(PermissionState.Undecided, AudioFlow.System, true));
        Assert.Null(MacCaptureRules.PendingText(PermissionState.Undecided, AudioFlow.Mic, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void OnlyAnUndecidedPermissionHasThePendingText(int preflight)
    {
        var state = MacCaptureRules.StateOf(preflight);
        Assert.Null(MacCaptureRules.PendingText(state, AudioFlow.System, false));
        Assert.Null(MacCaptureRules.PendingText(state, AudioFlow.Mic, false));
    }

    [Fact]
    public void WhatCannotBeOpenedIsRefused()
    {
        Assert.Null(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "out", true)));
        Assert.Null(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "out", false)));
        Assert.Null(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.Mic, "in", false)));
        Assert.Null(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "out", false, 42, "app")));
        Assert.IsType<PlatformNotSupportedException>(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.Mic, "in", true)));
        Assert.IsType<PlatformNotSupportedException>(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.Mic, "in", false, 42)));
        Assert.IsType<PlatformNotSupportedException>(MacCaptureRules.Refuse(new CaptureRequest(AudioFlow.System, "out", true, 42)));
    }

    [Fact]
    public void TheNullAudioDeviceIsTheMainDeviceWhenThereIsOne()
    {
        Assert.Equal("NullAudioDevice_UID", MacCaptureRules.MainDeviceOf(true, "out", false));
        Assert.Equal("out", MacCaptureRules.MainDeviceOf(false, "out", false));
        Assert.Equal("out", MacCaptureRules.MainDeviceOf(true, "out", true));
        Assert.Null(MacCaptureRules.MainDeviceOf(false, null, false));
    }

    [Fact]
    public void TheFamilyIsTheProcessAndAllItsDescendants()
    {
        ProcessEntry[] processes =
        [
            new(1, 1, "launchd"), new(10, 1, "Discord"), new(11, 10, "Discord Helper"), new(12, 11, "Discord Helper (Renderer)"),
            new(20, 1, "Other"), new(21, 20, "Other Helper"), new(30, 30, "self parent"),
        ];
        Assert.Equal([10, 11, 12], MacCaptureRules.FamilyOf(processes, 10).Order());
        Assert.Equal([99], MacCaptureRules.FamilyOf(processes, 99));
        Assert.Equal([30], MacCaptureRules.FamilyOf(processes, 30));
    }

    [Fact]
    public void AFamilyWithReusedIdsInACircleEnds()
    {
        ProcessEntry[] processes = [new(5, 6, "a"), new(6, 5, "b")];
        Assert.Equal([5, 6], MacCaptureRules.FamilyOf(processes, 5).Order());
    }

    [Fact]
    public void TheTapTakesTheAudioObjectsOfTheFamilyOnly()
    {
        (uint, int)[] audio = [(300, 12), (100, 10), (200, 20)];
        Assert.Equal([100u, 300u], MacCaptureRules.ObjectsOf(audio, new HashSet<int> { 10, 11, 12 }));
        Assert.Empty(MacCaptureRules.ObjectsOf(audio, new HashSet<int> { 99 }));
    }

    [Fact]
    public void AToneIsMeasuredAsTheProbeMeasuredIt()
    {
        const int rate = 48_000;
        var samples = new float[rate * 2];
        for (var i = 0; i < rate; i++)
        {
            var value = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / rate));
            samples[2 * i] = value;
            samples[(2 * i) + 1] = value;
        }

        var measure = SignalMeasure.Of(samples, 2, rate);
        Assert.Equal("SIGNAL", measure.Verdict);
        Assert.Equal(1000, measure.DominantHz);
        Assert.InRange(measure.A1000Db, -6.2, -5.8);
        Assert.True(measure.A440Db < -40);
        Assert.True(measure.Tone1000);
        Assert.False(measure.ExactZero);
    }

    [Fact]
    public void ExactZerosAndNothingAreToldApart()
    {
        Assert.True(SignalMeasure.Of(new float[4096], 2, 48_000).ExactZero);
        Assert.Equal("SILENCE_EXACT_ZERO", SignalMeasure.Of(new float[4096], 2, 48_000).Verdict);
        Assert.Equal("NO_DATA", SignalMeasure.Of([], 2, 48_000).Verdict);
        Assert.False(SignalMeasure.Of([], 2, 48_000).ExactZero);
        var faint = new float[4096];
        faint[100] = 1e-6f;
        Assert.Equal("SILENCE_NEAR_ZERO", SignalMeasure.Of(faint, 2, 48_000).Verdict);
    }

    [Fact]
    public void TheProofOptionsAreRead()
    {
        Assert.Equal(("/tmp/p", false), MacCaptureProofOptions.Read(["--prove-capture", "/tmp/p"]));
        Assert.Equal(("/tmp/p", true), MacCaptureProofOptions.Read(["--denied", "--prove-capture", "/tmp/p"]));
        Assert.Null(MacCaptureProofOptions.Read(["--prove-capture"]));
        Assert.Null(MacCaptureProofOptions.Read(["--prove-capture", "--denied"]));
        Assert.Null(MacCaptureProofOptions.Read(["--prove-loop"]));
    }
}
