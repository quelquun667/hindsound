using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The line under the wave agrees with the band when the memory stays a hair under its setting for good.</summary>
public class SaveLineFullMemoryTests
{
    private const int Rate = InternalFormat.SampleRate;

    private static WaveFacts Facts(int memorySeconds, double heldSeconds)
    {
        var end = 5000L * Rate;
        return new WaveFacts(null, null, end - (long)(heldSeconds * Rate), end, memorySeconds * Rate, []);
    }

    [Fact]
    public void A_memory_a_hair_short_of_its_setting_is_full_for_a_clip_as_long_as_the_memory()
    {
        Assert.Equal("Save: last 5:00", WaveView.SaveLine(300, Facts(300, 299.4)));
        Assert.Equal((300, 0), WaveView.SaveSeconds(300, Facts(300, 299.4)));
    }

    [Fact]
    public void A_clip_longer_than_the_memory_says_the_memory_as_well()
    {
        Assert.Equal("Save: last 0:30 of 1:00", WaveView.SaveLine(60, Facts(30, 29.2)));
    }

    [Fact]
    public void A_clip_shorter_than_the_memory_is_not_rounded_up()
    {
        Assert.Equal("Save: last 0:29 of 0:30", WaveView.SaveLine(30, Facts(300, 29.4)));
    }

    [Fact]
    public void A_memory_that_is_filling_says_what_it_holds()
    {
        Assert.Equal("Save: last 1:40 of 5:00", WaveView.SaveLine(300, Facts(300, 100.4)));
    }

    [Fact]
    public void Facts_with_no_memory_size_are_left_alone()
    {
        Assert.Equal((10, 0), WaveView.SaveSeconds(30, Facts(0, 10)));
    }
}
