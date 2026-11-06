using Hindsound.Audio;

namespace Hindsound.Tests;

public class AudioSnapshotTests
{
    [Fact]
    public void Duration_comes_from_the_system_audio_frames()
    {
        var snapshot = new AudioSnapshot(new float[InternalFormat.SampleRate * InternalFormat.SystemChannels * 2], []);

        Assert.Equal(TimeSpan.FromSeconds(2), snapshot.Duration);
    }

    [Fact]
    public void Duration_comes_from_the_mic_when_there_is_no_system_audio()
    {
        var snapshot = new AudioSnapshot([], new float[InternalFormat.SampleRate * 3]);

        Assert.Equal(TimeSpan.FromSeconds(3), snapshot.Duration);
    }

    [Fact]
    public void An_empty_snapshot_lasts_zero()
    {
        Assert.Equal(TimeSpan.Zero, new AudioSnapshot([], []).Duration);
    }
}
