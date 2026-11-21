using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Audio.Capture;

/// <summary>
/// The calls of Core Audio's HAL the capture makes, from C#: properties of the system, the devices, the taps and the
/// processes, the IOProcs, the process taps and the private aggregate devices. Plain C functions, called by P/Invoke; the
/// two Objective-C pieces (CATapDescription and the NSDictionary of an aggregate) are in <see cref="MacTapParts"/>.
/// Reading a property never asks for a permission; creating an IOProc on a tap or an input does, and blocks until the
/// person answers when the permission is not decided yet: that is done off the interface's thread only.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class CoreAudio
{
    public const string Framework = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    public const uint SystemObject = 1;
    public const uint Unknown = 0;

    public static readonly uint ScopeGlobal = FourCC("glob");
    public static readonly uint ScopeInput = FourCC("inpt");
    public static readonly uint ScopeOutput = FourCC("outp");

    public static readonly uint Devices = FourCC("dev#");
    public static readonly uint DefaultInputDevice = FourCC("dIn ");
    public static readonly uint DefaultOutputDevice = FourCC("dOut");
    public static readonly uint TranslatePidToProcessObject = FourCC("id2p");
    public static readonly uint ProcessObjectList = FourCC("prs#");
    public static readonly uint ProcessPid = FourCC("ppid");
    public static readonly uint DeviceUid = FourCC("uid ");
    public static readonly uint Name = FourCC("lnam");
    public static readonly uint StreamConfiguration = FourCC("slay");
    public static readonly uint NominalSampleRate = FourCC("nsrt");
    public static readonly uint DeviceIsAlive = FourCC("livn");
    public static readonly uint TapUid = FourCC("tuid");
    public static readonly uint TapDescription = FourCC("tdsc");
    public static readonly uint TapFormat = FourCC("tfmt");

    // AudioTimeStamp.mFlags.
    public const uint SampleTimeValid = 1;
    public const uint HostTimeValid = 2;

    private static readonly nint Handle = NativeLibrary.Load(Framework);

    /// <summary>A four-character code as Core Audio writes them, 'dev#' for instance.</summary>
    public static uint FourCC(string code) => code.Length == 4
        ? (uint)((code[0] << 24) | (code[1] << 16) | (code[2] << 8) | code[3])
        : throw new ArgumentException("A four-character code has four characters.", nameof(code));

    /// <summary>Makes sure the framework is loaded, so that CATapDescription is known to the Objective-C runtime.</summary>
    public static void Load() => GC.KeepAlive(Handle);

    public static uint GetUInt(uint objectId, uint selector, uint scope = 0) =>
        TryGetUInt(objectId, selector, out var value, scope) ? value : Unknown;

    public static bool TryGetUInt(uint objectId, uint selector, out uint value, uint scope = 0)
    {
        var address = new PropertyAddress(selector, scope == 0 ? ScopeGlobal : scope);
        uint result = 0;
        var size = (uint)sizeof(uint);
        var status = AudioObjectGetPropertyData(objectId, &address, 0, null, &size, &result);
        value = result;
        return status == 0;
    }

    public static double GetDouble(uint objectId, uint selector)
    {
        var address = new PropertyAddress(selector, ScopeGlobal);
        double value = 0;
        var size = (uint)sizeof(double);
        return AudioObjectGetPropertyData(objectId, &address, 0, null, &size, &value) == 0 ? value : 0;
    }

    /// <summary>A CFString property as text, or null.</summary>
    /// <summary>
    /// The channel count of a tap's format (<c>kAudioTapPropertyFormat</c>, an AudioStreamBasicDescription of 40 bytes whose
    /// mChannelsPerFrame is at byte 28), or <paramref name="otherwise"/> when the tap does not say.
    /// </summary>
    public static int TapChannels(uint tap, int otherwise)
    {
        var address = new PropertyAddress(TapFormat, ScopeGlobal);
        var format = stackalloc byte[40];
        var size = 40u;
        if (AudioObjectGetPropertyData(tap, &address, 0, null, &size, format) != 0 || size < 32)
        {
            return otherwise;
        }

        var channels = *(uint*)(format + 28);
        return channels is > 0 and <= 64 ? (int)channels : otherwise;
    }

    public static string? GetString(uint objectId, uint selector)
    {
        var address = new PropertyAddress(selector, ScopeGlobal);
        nint text = 0;
        var size = (uint)sizeof(nint);
        if (AudioObjectGetPropertyData(objectId, &address, 0, null, &size, &text) != 0 || text == 0)
        {
            return null;
        }

        try
        {
            return ObjC.String(text);
        }
        finally
        {
            CFRelease(text);
        }
    }

    /// <summary>A property holding a list of object ids.</summary>
    public static uint[] GetObjects(uint objectId, uint selector)
    {
        var address = new PropertyAddress(selector, ScopeGlobal);
        uint size = 0;
        if (AudioObjectGetPropertyDataSize(objectId, &address, 0, null, &size) != 0 || size == 0)
        {
            return [];
        }

        var ids = new uint[size / sizeof(uint)];
        fixed (uint* data = ids)
        {
            if (AudioObjectGetPropertyData(objectId, &address, 0, null, &size, data) != 0)
            {
                return [];
            }
        }

        return ids[..(int)(size / sizeof(uint))];
    }

    /// <summary>The channels of each stream of a device on one side, in order: empty when it has none there.</summary>
    public static int[] StreamChannels(uint device, uint scope)
    {
        var address = new PropertyAddress(StreamConfiguration, scope);
        uint size = 0;
        if (AudioObjectGetPropertyDataSize(device, &address, 0, null, &size) != 0 || size < sizeof(uint))
        {
            return [];
        }

        var bytes = new byte[size];
        fixed (byte* data = bytes)
        {
            if (AudioObjectGetPropertyData(device, &address, 0, null, &size, data) != 0)
            {
                return [];
            }

            var list = (AudioBufferList*)data;
            var channels = new int[list->NumberBuffers];
            for (var i = 0; i < channels.Length; i++)
            {
                channels[i] = (int)(&list->First)[i].NumberChannels;
            }

            return channels;
        }
    }

    /// <summary>The device with this UID, or <see cref="Unknown"/>.</summary>
    public static uint DeviceOf(string uid)
    {
        foreach (var device in GetObjects(SystemObject, Devices))
        {
            if (GetString(device, DeviceUid) == uid)
            {
                return device;
            }
        }

        return Unknown;
    }

    /// <summary>The audio object of a process, or <see cref="Unknown"/> when the HAL has none for it.</summary>
    public static uint ProcessObjectOf(int pid)
    {
        var address = new PropertyAddress(TranslatePidToProcessObject, ScopeGlobal);
        uint result = 0;
        var size = (uint)sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, &address, (uint)sizeof(int), &pid, &size, &result) == 0 ? result : Unknown;
    }

    /// <summary>The audio objects of the HAL's client processes, with their process ids.</summary>
    public static IReadOnlyList<(uint Object, int Pid)> ProcessObjects()
    {
        var list = new List<(uint, int)>();
        foreach (var process in GetObjects(SystemObject, ProcessObjectList))
        {
            if (TryGetUInt(process, ProcessPid, out var pid))
            {
                list.Add((process, (int)pid));
            }
        }

        return list;
    }

    public static string Check(int status, string what) => status == 0
        ? what
        : throw new IOException($"Core Audio refused {what}: {StatusText(status)}");

    /// <summary>A status as Core Audio's codes read: four characters when they are printable, the number otherwise.</summary>
    public static string StatusText(int status)
    {
        var bytes = new[] { (byte)(status >> 24), (byte)(status >> 16), (byte)(status >> 8), (byte)status };
        return bytes.All(b => b is >= 32 and < 127)
            ? $"'{(char)bytes[0]}{(char)bytes[1]}{(char)bytes[2]}{(char)bytes[3]}' ({status})"
            : status.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PropertyAddress(uint selector, uint scope)
    {
        public readonly uint Selector = selector;
        public readonly uint Scope = scope;
        public readonly uint Element = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

    /// <summary>Nanoseconds per tick of mach_absolute_time, the clock of the host times.</summary>
    public static double NanosecondsPerHostTick { get; } = ReadTimebase();

    public static ulong HostTimeNow() => mach_absolute_time();

    private static double ReadTimebase()
    {
        TimebaseInfo info;
        return mach_timebase_info(&info) == 0 && info.Denom != 0 ? (double)info.Numer / info.Denom : 1;
    }

    [LibraryImport(Framework)]
    public static partial int AudioObjectGetPropertyDataSize(uint objectId, PropertyAddress* address, uint qualifierSize, void* qualifier, uint* size);

    [LibraryImport(Framework)]
    public static partial int AudioObjectGetPropertyData(uint objectId, PropertyAddress* address, uint qualifierSize, void* qualifier, uint* size, void* data);

    [LibraryImport(Framework)]
    public static partial int AudioObjectSetPropertyData(uint objectId, PropertyAddress* address, uint qualifierSize, void* qualifier, uint size, void* data);

    [LibraryImport(Framework)]
    public static partial int AudioObjectAddPropertyListener(uint objectId, PropertyAddress* address, nint listener, nint clientData);

    [LibraryImport(Framework)]
    public static partial int AudioObjectRemovePropertyListener(uint objectId, PropertyAddress* address, nint listener, nint clientData);

    [LibraryImport(Framework)]
    public static partial int AudioDeviceCreateIOProcID(uint device, nint proc, nint clientData, nint* procId);

    [LibraryImport(Framework)]
    public static partial int AudioDeviceDestroyIOProcID(uint device, nint procId);

    [LibraryImport(Framework)]
    public static partial int AudioDeviceStart(uint device, nint procId);

    [LibraryImport(Framework)]
    public static partial int AudioDeviceStop(uint device, nint procId);

    [LibraryImport(Framework)]
    public static partial int AudioHardwareCreateProcessTap(nint description, uint* tap);

    [LibraryImport(Framework)]
    public static partial int AudioHardwareDestroyProcessTap(uint tap);

    [LibraryImport(Framework)]
    public static partial int AudioHardwareCreateAggregateDevice(nint description, uint* device);

    [LibraryImport(Framework)]
    public static partial int AudioHardwareDestroyAggregateDevice(uint device);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRelease(nint value);

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial ulong mach_absolute_time();

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial int mach_timebase_info(TimebaseInfo* info);
}
