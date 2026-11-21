using Hindsound.Audio;
using Hindsound.Mac.Audio.Devices;

namespace Hindsound.Mac.Tests.Audio.Devices;

public sealed class MacAudioListsTests
{
    private static readonly HalDevice[] Devices =
    [
        new("BlackHole2ch_UID", "BlackHole 2ch", Inputs: 1, Outputs: 1),
        new("BuiltInSpeakerDevice", "MacBook Pro Speakers", Inputs: 0, Outputs: 1),
        new("BuiltInMicrophoneDevice", "MacBook Pro Microphone", Inputs: 1, Outputs: 0),
        new("NoName_UID", "  ", Inputs: 0, Outputs: 2),
        new(null, "No id", Inputs: 1, Outputs: 1),
        new("", "Empty id", Inputs: 1, Outputs: 1),
        new("Silent_UID", "No streams", Inputs: 0, Outputs: 0),
    ];

    [Fact]
    public void The_outputs_are_the_devices_with_output_streams_by_their_uid()
    {
        Assert.Equal(
            [
                new AudioDeviceInfo("BlackHole2ch_UID", "BlackHole 2ch"),
                new AudioDeviceInfo("BuiltInSpeakerDevice", "MacBook Pro Speakers"),
                new AudioDeviceInfo("NoName_UID", "NoName_UID"),
            ],
            MacAudioLists.Devices(Devices, AudioFlow.System));
    }

    [Fact]
    public void The_microphones_are_the_devices_with_input_streams()
    {
        Assert.Equal(
            [new AudioDeviceInfo("BlackHole2ch_UID", "BlackHole 2ch"), new AudioDeviceInfo("BuiltInMicrophoneDevice", "MacBook Pro Microphone")],
            MacAudioLists.Devices(Devices, AudioFlow.Mic));
    }

    [Fact]
    public void The_apps_are_those_that_play_now_one_line_per_application_without_this_one()
    {
        ProcessEntry[] processes =
        [
            new(500, 1, "Safari"),
            new(520, 500, "Safari"),
            new(521, 500, "Safari"),
            new(600, 1, "Music"),
            new(700, 410, "afplay"),
            new(800, 1, "Hindsound"),
        ];
        HalProcess[] clients =
        [
            new(520, PlaysOutput: true, "com.apple.WebKit.GPU"),
            new(521, PlaysOutput: true, "com.apple.WebKit.WebContent"),
            new(600, PlaysOutput: false, "com.apple.Music"),
            new(700, PlaysOutput: true, null),
            new(800, PlaysOutput: true, "io.github.quelquun667.hindsound"),
            new(999, PlaysOutput: true, "gone"),
        ];

        Assert.Equal(
            [new AudioAppInfo("Safari", "Safari"), new AudioAppInfo("afplay", "afplay")],
            MacAudioLists.Apps(clients, processes, ownProcessId: 800));
    }

    [Theory]
    [InlineData("dev#", 0x64657623u)]
    [InlineData("dIn ", 0x64496E20u)]
    [InlineData("dOut", 0x644F7574u)]
    [InlineData("glob", 0x676C6F62u)]
    [InlineData("prs#", 0x70727323u)]
    [InlineData("piro", 0x7069726Fu)]
    public void A_four_character_code_has_its_first_character_highest(string text, uint expected) =>
        Assert.Equal(expected, HalCodes.Code(text));

    [Theory]
    [InlineData("dev")]
    [InlineData("devs#")]
    [InlineData("dév#")]
    public void A_code_is_four_ascii_characters(string text) =>
        Assert.Throws<ArgumentException>(() => HalCodes.Code(text));

    [Fact]
    public void The_selectors_of_the_hal_are_the_ones_of_AudioHardware_h()
    {
        Assert.Equal(HalCodes.Code("dev#"), HalCodes.Devices);
        Assert.Equal(HalCodes.Code("dIn "), HalCodes.DefaultInput);
        Assert.Equal(HalCodes.Code("dOut"), HalCodes.DefaultOutput);
        Assert.Equal(HalCodes.Code("prs#"), HalCodes.ProcessObjects);
        Assert.Equal(HalCodes.Code("ppid"), HalCodes.ProcessId);
        Assert.Equal(HalCodes.Code("piro"), HalCodes.ProcessRunsOutput);
        Assert.Equal(HalCodes.Code("uid "), HalCodes.DeviceUid);
        Assert.Equal(HalCodes.Code("stm#"), HalCodes.Streams);
    }
}
