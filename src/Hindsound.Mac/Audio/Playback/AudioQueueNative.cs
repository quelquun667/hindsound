using System.Runtime.InteropServices;

namespace Hindsound.Mac.Audio.Playback;

/// <summary>The format of a queue: linear PCM, 32-bit floats, interleaved. As <c>AudioStreamBasicDescription</c> of Core Audio.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioStreamBasicDescription
{
    public double SampleRate;
    public uint FormatId;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;

    /// <summary>Interleaved floats, <paramref name="channels"/> to a frame.</summary>
    public static AudioStreamBasicDescription Float(int rate, int channels) => new()
    {
        SampleRate = rate,
        FormatId = AudioQueueNative.LinearPcm,
        FormatFlags = AudioQueueNative.FloatPacked,
        BytesPerPacket = (uint)(sizeof(float) * channels),
        FramesPerPacket = 1,
        BytesPerFrame = (uint)(sizeof(float) * channels),
        ChannelsPerFrame = (uint)channels,
        BitsPerChannel = 32,
    };
}

/// <summary>One buffer of a queue, as <c>AudioQueueBuffer</c> of Core Audio.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioQueueBuffer
{
    public uint AudioDataBytesCapacity;
    public void* AudioData;
    public uint AudioDataByteSize;
    public void* UserData;
    public uint PacketDescriptionCapacity;
    public void* PacketDescriptions;
    public uint PacketDescriptionCount;
}

/// <summary>
/// The AudioQueue calls of AudioToolbox the preview needs. A queue with no device set plays on the default output and follows
/// it when it changes; it runs inside the app's process, so a tap that leaves the app out leaves its sound out too.
/// </summary>
internal static unsafe partial class AudioQueueNative
{
    public const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary><c>kAudioFormatLinearPCM</c>: 'lpcm'.</summary>
    public const uint LinearPcm = 0x6C70636D;

    /// <summary><c>kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked</c>.</summary>
    public const uint FloatPacked = 0x1 | 0x8;

    /// <summary><c>kAudioQueueProperty_IsRunning</c>: 'aqrn'.</summary>
    public const uint IsRunningProperty = 0x6171726E;

    /// <summary>The functions looked for in AudioToolbox, for the test that checks they are all there.</summary>
    public static readonly string[] Functions =
    [
        "AudioQueueNewOutput", "AudioQueueNewInput", "AudioQueueAllocateBuffer", "AudioQueueEnqueueBuffer", "AudioQueueStart",
        "AudioQueuePause", "AudioQueueStop", "AudioQueueDispose", "AudioQueueAddPropertyListener", "AudioQueueGetProperty",
    ];

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueNewOutput(AudioStreamBasicDescription* format, delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, void> callback,
        IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, IntPtr* queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueNewInput(AudioStreamBasicDescription* format,
        delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, IntPtr, uint, IntPtr, void> callback,
        IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, IntPtr* queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueAllocateBuffer(IntPtr queue, uint bytes, AudioQueueBuffer** buffer);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueEnqueueBuffer(IntPtr queue, AudioQueueBuffer* buffer, uint packetDescriptions, IntPtr descriptions);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueuePause(IntPtr queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueStop(IntPtr queue, byte immediate);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueDispose(IntPtr queue, byte immediate);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueAddPropertyListener(IntPtr queue, uint property, delegate* unmanaged<IntPtr, IntPtr, uint, void> listener, IntPtr userData);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueGetProperty(IntPtr queue, uint property, void* data, uint* size);

    /// <summary>Throws with the status of Core Audio when a call failed: an <see cref="IOException"/>, its message the call and the code.</summary>
    public static void Check(int status, string call)
    {
        if (status != 0)
        {
            throw new IOException($"{call} failed with status {status} ({FourCharacters(status)})");
        }
    }

    /// <summary>A status of Core Audio is often four characters; otherwise the number alone.</summary>
    internal static string FourCharacters(int status)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((status >> (24 - (8 * i))) & 0xFF);
            if (c is < ' ' or > '~')
            {
                return status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            text[i] = c;
        }

        return "'" + new string(text) + "'";
    }
}
