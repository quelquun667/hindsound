using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The widths of the fields of libpulse, not only where they start: a uint32_t read as a byte keeps every offset, through the
/// padding before the next pointer, and turns PA_INVALID_INDEX into another number.
/// </summary>
public sealed unsafe class PulseFieldWidthTests
{
    private const int MonitorOfSinkOffset = 308;

    [Theory]
    [InlineData(nameof(PaSourceInfo.Index))]
    [InlineData(nameof(PaSourceInfo.OwnerModule))]
    [InlineData(nameof(PaSourceInfo.MonitorOfSink))]
    public void The_indexes_of_the_source_info_are_32_bit(string field) =>
        Assert.Equal(typeof(uint), typeof(PaSourceInfo).GetField(field)!.FieldType);

    [Theory]
    [InlineData(nameof(PaSinkInfo.Index))]
    [InlineData(nameof(PaSinkInfo.OwnerModule))]
    [InlineData(nameof(PaSinkInfo.MonitorSource))]
    public void The_indexes_of_the_sink_info_are_32_bit(string field) =>
        Assert.Equal(typeof(uint), typeof(PaSinkInfo).GetField(field)!.FieldType);

    [Fact]
    public void A_source_with_the_invalid_index_in_monitor_of_sink_is_a_real_input()
    {
        Assert.False(SourceWith(uint.MaxValue).IsMonitor);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    [InlineData(0x100u)]
    public void A_source_that_names_a_sink_is_its_monitor(uint sink)
    {
        Assert.True(SourceWith(sink).IsMonitor);
    }

    // The bytes as libpulse writes them on x86-64, read through the structure the app reads.
    private static PaSourceInfo SourceWith(uint monitorOfSink)
    {
        var bytes = new byte[sizeof(PaSourceInfo)];
        BitConverter.TryWriteBytes(bytes.AsSpan(MonitorOfSinkOffset), monitorOfSink);
        fixed (byte* raw = bytes)
        {
            return *(PaSourceInfo*)raw;
        }
    }
}
