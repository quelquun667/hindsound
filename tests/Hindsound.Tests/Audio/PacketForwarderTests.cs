using Hindsound.Audio;
using NAudio.CoreAudioApi;

namespace Hindsound.Tests.Audio;

/// <summary>The forwarder of the WASAPI source, without a device: what it passes on, and when it listens to the recorder.</summary>
public sealed class PacketForwarderTests
{
    private sealed class Counting
    {
        public int Subscribed { get; private set; }

        public int Unsubscribed { get; private set; }

        public PacketForwarder Forwarder { get; }

        public Counting() => Forwarder = new PacketForwarder(() => Subscribed++, () => Unsubscribed++);
    }

    [Theory]
    [InlineData(AudioClientBufferFlags.None, PacketFlags.None)]
    [InlineData(AudioClientBufferFlags.Silent, PacketFlags.Silent)]
    [InlineData(AudioClientBufferFlags.DataDiscontinuity, PacketFlags.DataDiscontinuity)]
    [InlineData(AudioClientBufferFlags.TimestampError, PacketFlags.TimestampError)]
    [InlineData(AudioClientBufferFlags.Silent | AudioClientBufferFlags.DataDiscontinuity, PacketFlags.Silent | PacketFlags.DataDiscontinuity)]
    public void The_flags_arrive_as_they_were_sent(AudioClientBufferFlags sent, PacketFlags expected)
    {
        var counting = new Counting();
        PacketFlags? heard = null;
        long device = 0, qpc = 0;
        var length = 0;
        counting.Forwarder.Add((buffer, flags, devicePosition, qpcPosition) =>
        {
            heard = flags;
            device = devicePosition;
            qpc = qpcPosition;
            length = buffer.Length;
        });

        counting.Forwarder.Forward(new byte[12], sent, 34, 56);

        Assert.Equal(expected, heard);
        Assert.Equal((34L, 56L, 12), (device, qpc, length));
    }

    [Fact]
    public void It_listens_to_the_recorder_from_the_first_handler_and_not_before()
    {
        var counting = new Counting();
        counting.Forwarder.Forward(new byte[4], AudioClientBufferFlags.None, 0, 0);
        Assert.Equal(0, counting.Subscribed);

        counting.Forwarder.Add(static (_, _, _, _) => { });
        counting.Forwarder.Add(static (_, _, _, _) => { });

        Assert.Equal(1, counting.Subscribed);
    }

    [Fact]
    public void It_stops_listening_after_the_last_handler_only()
    {
        var counting = new Counting();
        var heard = 0;
        CapturePacketHandler first = (_, _, _, _) => heard++;
        CapturePacketHandler second = (_, _, _, _) => heard += 10;
        counting.Forwarder.Add(first);
        counting.Forwarder.Add(second);

        counting.Forwarder.Remove(first);
        Assert.Equal(0, counting.Unsubscribed);
        counting.Forwarder.Forward(new byte[4], AudioClientBufferFlags.None, 0, 0);
        Assert.Equal(10, heard);

        counting.Forwarder.Remove(second);
        Assert.Equal(1, counting.Unsubscribed);
        counting.Forwarder.Forward(new byte[4], AudioClientBufferFlags.None, 0, 0);
        Assert.Equal(10, heard);
    }
}
