using System.Runtime.CompilerServices;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The structures of libpulse sit where a C compiler puts them on x86-64: the pointers on 8 bytes, the enums and the
/// 32-bit integers on 4, and each array of PA_CHANNELS_MAX (32) entries after a byte and its padding. A field read at the
/// wrong place would give the app another string, or a crash, only on Linux.
/// </summary>
public sealed unsafe class PulseLayoutTests
{
    private static int OffsetOf<TStruct, TField>(ref TStruct structure, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref structure), ref Unsafe.As<TField, byte>(ref field));

    [Fact]
    public void The_small_structures_have_the_sizes_of_their_c_declarations()
    {
        Assert.Equal(12, sizeof(PaSampleSpec));
        Assert.Equal(20, sizeof(PaBufferAttr));
        Assert.Equal(132, sizeof(PaChannelMap));
        Assert.Equal(132, sizeof(PaCVolume));
    }

    [Fact]
    public void The_server_info_gives_the_default_names_after_four_pointers_and_a_sample_spec()
    {
        var info = default(PaServerInfo);
        Assert.Equal(32, OffsetOf(ref info, ref info.SampleSpec));
        Assert.Equal(48, OffsetOf(ref info, ref info.DefaultSinkName));
        Assert.Equal(56, OffsetOf(ref info, ref info.DefaultSourceName));
    }

    [Fact]
    public void The_sink_info_gives_its_name_its_description_and_its_monitor_where_libpulse_writes_them()
    {
        var info = default(PaSinkInfo);
        Assert.Equal(0, OffsetOf(ref info, ref info.Name));
        Assert.Equal(16, OffsetOf(ref info, ref info.Description));
        Assert.Equal(36, OffsetOf(ref info, ref info.ChannelMap));
        Assert.Equal(168, OffsetOf(ref info, ref info.OwnerModule));
        Assert.Equal(172, OffsetOf(ref info, ref info.Volume));
        Assert.Equal(308, OffsetOf(ref info, ref info.MonitorSource));
        Assert.Equal(312, OffsetOf(ref info, ref info.MonitorSourceName));
    }

    [Fact]
    public void The_source_info_has_the_same_head_as_the_sink_info()
    {
        var info = default(PaSourceInfo);
        Assert.Equal(16, OffsetOf(ref info, ref info.Description));
        Assert.Equal(308, OffsetOf(ref info, ref info.MonitorOfSink));
        Assert.Equal(312, OffsetOf(ref info, ref info.MonitorOfSinkName));
    }
}
