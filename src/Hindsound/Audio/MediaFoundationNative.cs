using System.Runtime.InteropServices;

namespace Hindsound.Audio;

/// <summary>
/// The Media Foundation calls the clip decoder makes. The COM objects are called through their vtables, by slot number, so
/// nothing is marshalled by reflection and the trimmed exe keeps them working. The slots follow mfidl.h and
/// mfreadwrite.h (IUnknown takes slots 0 to 2).
/// </summary>
internal static unsafe partial class MediaFoundationNative
{
    /// <summary>MF_VERSION of the Windows 7 SDK and later: (MF_SDK_VERSION 2 &lt;&lt; 16) | MF_API_VERSION 0x70.</summary>
    public const uint MF_VERSION = 0x00020070;
    public const uint MFSTARTUP_LITE = 1;

    public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;
    public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
    public const uint MF_SOURCE_READER_MEDIASOURCE = 0xFFFFFFFF;

    public const uint MF_SOURCE_READERF_ERROR = 0x1;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;

    /// <summary>MF_E_OUT_OF_RANGE: a seek past the end.</summary>
    public const int MF_E_OUT_OF_RANGE = unchecked((int)0xC00D36E5);

    public const ushort VT_I8 = 20;
    public const ushort VT_UI8 = 21;

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid MF_PD_DURATION = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    public static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFAudioFormat_Float = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// A PROPVARIANT holding a 64-bit integer: the type, three reserved words, then the value. 24 bytes, the size of a
    /// PROPVARIANT on x64; on x86 the system reads and writes only the first 16.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant64
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public long Value;
        public long Padding;
    }

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMediaType(nint* mediaType);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSourceReaderFromURL(string url, nint attributes, nint* reader);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PropVariant64* value);

    private static void** Slots(nint unknown) => *(void***)unknown;

    public static void Release(nint unknown)
    {
        if (unknown != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slots(unknown)[2])(unknown);
    }

    // ---------------------------------------------------------------- IMFAttributes (an IMFMediaType or an IMFSample is one)

    public static int GetUInt32(nint attributes, Guid key, uint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, Guid*, uint*, int>)Slots(attributes)[7])(attributes, &key, value);

    public static int SetGuid(nint attributes, Guid key, Guid value) =>
        ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slots(attributes)[24])(attributes, &key, &value);

    // ---------------------------------------------------------------- IMFSourceReader

    public static int SetStreamSelection(nint reader, uint stream, bool selected) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, int, int>)Slots(reader)[4])(reader, stream, selected ? 1 : 0);

    public static int GetCurrentMediaType(nint reader, uint stream, nint* mediaType) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slots(reader)[6])(reader, stream, mediaType);

    public static int SetCurrentMediaType(nint reader, uint stream, nint mediaType) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint*, nint, int>)Slots(reader)[7])(reader, stream, null, mediaType);

    public static int SetCurrentPosition(nint reader, PropVariant64* position)
    {
        var format = Guid.Empty; // GUID_NULL: 100-nanosecond units
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, PropVariant64*, int>)Slots(reader)[8])(reader, &format, position);
    }

    public static int ReadSample(nint reader, uint stream, uint* streamFlags, long* time, nint* sample)
    {
        uint actual;
        return ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Slots(reader)[9])(
            reader, stream, 0, &actual, streamFlags, time, sample);
    }

    public static int GetPresentationAttribute(nint reader, uint stream, Guid key, PropVariant64* value) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, Guid*, PropVariant64*, int>)Slots(reader)[12])(reader, stream, &key, value);

    // ---------------------------------------------------------------- IMFSample

    public static int GetSampleTime(nint sample, long* time) =>
        ((delegate* unmanaged[Stdcall]<nint, long*, int>)Slots(sample)[35])(sample, time);

    public static int ConvertToContiguousBuffer(nint sample, nint* buffer) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slots(sample)[41])(sample, buffer);

    // ---------------------------------------------------------------- IMFMediaBuffer

    public static int Lock(nint buffer, byte** data, uint* length)
    {
        uint max;
        return ((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slots(buffer)[3])(buffer, data, &max, length);
    }

    public static int Unlock(nint buffer) => ((delegate* unmanaged[Stdcall]<nint, int>)Slots(buffer)[4])(buffer);
}
