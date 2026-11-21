using System.Runtime.Versioning;
using Hindsound.Audio;
using Hindsound.Mac.Audio.Capture;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Tests.Audio.Capture;

/// <summary>
/// The parts of the capture that touch macOS but open nothing: properties of the HAL, the tap's description and the
/// aggregate's dictionary, read back. These run in the Test step, before the permissions are written in TCC: opening a tap
/// or an input there would make coreaudiod wait for a prompt nobody answers. The capture itself is proven by
/// <c>--prove-capture</c>, with real sound.
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class MacCaptureNativeTests
{
    private static readonly MacCaptureFactory Factory = new(_ => { }, new NoProcesses());

    [Fact]
    public void FourCharacterCodesAreThoseOfTheHeaders()
    {
        Assert.Equal(0x64657623u, CoreAudio.FourCC("dev#"));
        Assert.Equal(0x676C6F62u, CoreAudio.ScopeGlobal);
        Assert.Equal(0x74647363u, CoreAudio.TapDescription);
    }

    [Fact]
    public void TheDefaultDevicesResolveToTheirUids()
    {
        foreach (var (flow, selector, scope) in new[]
        {
            (AudioFlow.System, CoreAudio.DefaultOutputDevice, CoreAudio.ScopeOutput),
            (AudioFlow.Mic, CoreAudio.DefaultInputDevice, CoreAudio.ScopeInput),
        })
        {
            var device = CoreAudio.GetUInt(CoreAudio.SystemObject, selector);
            var uid = Factory.ResolveDevice(flow, null);
            Console.WriteLine($"{flow}: device {device}, uid {uid}, name {CoreAudio.GetString(device, CoreAudio.Name)}, "
                + $"rate {CoreAudio.GetDouble(device, CoreAudio.NominalSampleRate)}, streams {string.Join('+', CoreAudio.StreamChannels(device, scope))}");
            if (device == CoreAudio.Unknown)
            {
                Assert.Null(uid);
                continue;
            }

            Assert.Equal(CoreAudio.GetString(device, CoreAudio.DeviceUid), uid);
            Assert.Equal(device, CoreAudio.DeviceOf(uid!));
            Assert.Equal(uid, Factory.ResolveDevice(flow, uid));
        }
    }

    [Fact]
    public void AnUnknownDeviceResolvesToNothing()
    {
        Assert.Null(Factory.ResolveDevice(AudioFlow.System, "no such device"));
        Assert.Null(Factory.ResolveDevice(AudioFlow.Mic, "no such device"));
    }

    [Fact]
    public void TheProcessObjectsCarryTheirPids()
    {
        var processes = CoreAudio.ProcessObjects();
        Console.WriteLine($"{processes.Count} audio processes; this one: {CoreAudio.ProcessObjectOf(Environment.ProcessId)}");
        Assert.All(processes, process => Assert.True(process.Pid > 0));
        Assert.Equal(CoreAudio.Unknown, CoreAudio.ProcessObjectOf(int.MaxValue));
    }

    [Fact]
    public void AGlobalTapLeavesItsProcessesOut()
    {
        var description = MacTapParts.Description(global: true, [7, 9]);
        try
        {
            Assert.Equal([7u, 9u], MacTapParts.ProcessesOf(description));
            Assert.True(MacTapParts.IsExclusive(description));
            Assert.True(MacTapParts.IsMixdown(description));
            Assert.Null(MacTapParts.DeviceUidOf(description));
            Assert.False(string.IsNullOrEmpty(MacTapParts.UuidOf(description)));
        }
        finally
        {
            ObjC.Release(description);
        }
    }

    [Fact]
    public void AnApplicationsTapTakesOnlyItsProcessesAndFollowsThem()
    {
        var description = MacTapParts.Description(global: false, []);
        try
        {
            Assert.Empty(MacTapParts.ProcessesOf(description));
            Assert.False(MacTapParts.IsExclusive(description));
            MacTapParts.SetProcesses(description, [3, 4, 5]);
            Assert.Equal([3u, 4u, 5u], MacTapParts.ProcessesOf(description));
        }
        finally
        {
            ObjC.Release(description);
        }
    }

    [Fact]
    public void AnOutputChosenByNameIsTappedAloneAndMixedToStereo()
    {
        var description = MacTapParts.Description(global: true, [], "Some_Output_UID");
        try
        {
            Assert.Equal("Some_Output_UID", MacTapParts.DeviceUidOf(description));
            Assert.True(MacTapParts.IsExclusive(description));
            Assert.True(MacTapParts.IsMixdown(description));
        }
        finally
        {
            ObjC.Release(description);
        }
    }

    [Fact]
    public void TheAggregateIsPrivateWithTheTapAndItsMainDevice()
    {
        var dictionary = MacTapParts.Aggregate("TAP-UID", "NullAudioDevice_UID");
        try
        {
            using var pool = new ObjC.Pool();
            Assert.Equal("NullAudioDevice_UID", ObjC.String(MacTapParts.ValueOf(dictionary, MacTapParts.MainSubDeviceKey)));
            Assert.True(ObjC.SendBool(MacTapParts.ValueOf(dictionary, MacTapParts.PrivateKey), "boolValue"));
            Assert.True(ObjC.SendBool(MacTapParts.ValueOf(dictionary, MacTapParts.TapAutoStartKey), "boolValue"));
            var taps = MacTapParts.ValueOf(dictionary, MacTapParts.TapListKey);
            Assert.Equal(1u, ObjC.SendUInt(taps, "count"));
            var tap = ObjC.Send(taps, "firstObject");
            Assert.Equal("TAP-UID", ObjC.String(MacTapParts.ValueOf(tap, MacTapParts.UidKey)));
            Assert.Equal(1u, ObjC.SendUInt(MacTapParts.ValueOf(dictionary, MacTapParts.SubDeviceListKey), "count"));
        }
        finally
        {
            ObjC.Release(dictionary);
        }

        var alone = MacTapParts.Aggregate("TAP-UID", null);
        try
        {
            Assert.Equal((nint)0, MacTapParts.ValueOf(alone, MacTapParts.MainSubDeviceKey));
        }
        finally
        {
            ObjC.Release(alone);
        }
    }

    [Fact]
    public void ThePermissionIsReadWithoutAsking()
    {
        var system = MacPermission.Preflight(AudioFlow.System);
        var mic = MacPermission.Preflight(AudioFlow.Mic);
        Console.WriteLine($"preflight: audio capture {system}, microphone {mic}");
        Assert.True(Enum.IsDefined(system) && Enum.IsDefined(mic));
    }

    private sealed class NoProcesses : IProcessSnapshot
    {
        public IReadOnlyList<ProcessEntry> Take() => [];
    }
}
