using System.Runtime.InteropServices;

namespace Hindsound.Linux.Audio;

// The structures of libpulse that cross the boundary, as its headers declare them (pulse/sample.h, channelmap.h, volume.h,
// def.h, introspect.h). Sequential layout gives the offsets a C compiler gives on x86-64, which PulseLayoutTests pins.
// The info structures are declared only up to the last field read: libpulse hands them by pointer and owns them, and has
// only ever added fields at their end.

/// <summary>pa_sample_spec.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PaSampleSpec
{
    public int Format;
    public uint Rate;
    public byte Channels;
}

/// <summary>pa_buffer_attr. (uint)-1 in a field asks the server for its default.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PaBufferAttr
{
    public uint MaxLength;
    public uint TargetLength;
    public uint PreBuffer;
    public uint MinRequest;
    public uint FragmentSize;
}

/// <summary>pa_channel_map, PA_CHANNELS_MAX positions.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PaChannelMap
{
    public byte Channels;
    public fixed int Map[32];
}

/// <summary>pa_cvolume, PA_CHANNELS_MAX volumes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PaCVolume
{
    public byte Channels;
    public fixed uint Values[32];
}

/// <summary>The start of pa_server_info, up to default_source_name.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PaServerInfo
{
    public IntPtr UserName;
    public IntPtr HostName;
    public IntPtr ServerVersion;
    public IntPtr ServerName;
    public PaSampleSpec SampleSpec;
    public IntPtr DefaultSinkName;
    public IntPtr DefaultSourceName;
}

/// <summary>The start of pa_sink_info, up to monitor_source_name.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PaSinkInfo
{
    public IntPtr Name;
    public uint Index;
    public IntPtr Description;
    public PaSampleSpec SampleSpec;
    public PaChannelMap ChannelMap;
    public uint OwnerModule;
    public PaCVolume Volume;
    public int Mute;
    public uint MonitorSource;
    public IntPtr MonitorSourceName;
}

/// <summary>The start of pa_source_info, up to monitor_of_sink_name.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PaSourceInfo
{
    public IntPtr Name;
    public uint Index;
    public IntPtr Description;
    public PaSampleSpec SampleSpec;
    public PaChannelMap ChannelMap;
    public uint OwnerModule;
    public PaCVolume Volume;
    public int Mute;
    public uint MonitorOfSink;
    public IntPtr MonitorOfSinkName;

    /// <summary>Whether the source carries what a sink plays: PA_INVALID_INDEX in monitor_of_sink means a real input.</summary>
    public readonly bool IsMonitor => MonitorOfSink != uint.MaxValue;
}
