using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Audio.Devices;

/// <summary>A property of an object of the HAL: its selector, its scope and its element (AudioObjectPropertyAddress).</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct AudioPropertyAddress(uint selector, uint scope, uint element = 0)
{
    public readonly uint Selector = selector;
    public readonly uint Scope = scope;
    public readonly uint Element = element;
}

/// <summary>The four-character codes of the HAL that this layer reads, as AudioHardware.h spells them.</summary>
internal static class HalCodes
{
    /// <summary>kAudioObjectSystemObject: the HAL itself.</summary>
    public const uint SystemObject = 1;

    public static readonly uint ScopeGlobal = Code("glob");
    public static readonly uint ScopeInput = Code("inpt");
    public static readonly uint ScopeOutput = Code("outp");

    /// <summary>kAudioHardwarePropertyDevices: the list of the devices.</summary>
    public static readonly uint Devices = Code("dev#");

    /// <summary>kAudioHardwarePropertyDefaultInputDevice.</summary>
    public static readonly uint DefaultInput = Code("dIn ");

    /// <summary>kAudioHardwarePropertyDefaultOutputDevice.</summary>
    public static readonly uint DefaultOutput = Code("dOut");

    /// <summary>kAudioHardwarePropertyProcessObjectList (macOS 14): one object per process that is a client of the HAL.</summary>
    public static readonly uint ProcessObjects = Code("prs#");

    /// <summary>kAudioProcessPropertyPID.</summary>
    public static readonly uint ProcessId = Code("ppid");

    /// <summary>kAudioProcessPropertyBundleID.</summary>
    public static readonly uint ProcessBundleId = Code("pbid");

    /// <summary>kAudioProcessPropertyIsRunningOutput: the process plays sound now.</summary>
    public static readonly uint ProcessRunsOutput = Code("piro");

    /// <summary>kAudioDevicePropertyDeviceUID: the id of a device that lasts across restarts.</summary>
    public static readonly uint DeviceUid = Code("uid ");

    /// <summary>kAudioObjectPropertyName.</summary>
    public static readonly uint Name = Code("lnam");

    /// <summary>kAudioDevicePropertyStreams, in the input or the output scope.</summary>
    public static readonly uint Streams = Code("stm#");

    /// <summary>A four-character code, the first character in the highest byte.</summary>
    public static uint Code(string four)
    {
        ArgumentNullException.ThrowIfNull(four);
        if (four.Length != 4 || four.Any(c => c > 0x7F))
        {
            throw new ArgumentException("a four-character code is four ASCII characters", nameof(four));
        }

        return ((uint)four[0] << 24) | ((uint)four[1] << 16) | ((uint)four[2] << 8) | four[3];
    }
}

/// <summary>The calls of CoreAudio.framework this layer makes: reading properties of the HAL, and listening to them.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class CoreAudioHal
{
    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    // The listeners of each watch, by a number given to the HAL as its client data: a call that comes after the watch is
    // gone finds nothing, where a handle to a freed object would point anywhere.
    private static readonly ConcurrentDictionary<nint, Action> Listeners = new();
    private static long _nextListener;

    /// <summary>The ids an array property holds (AudioObjectID or AudioDeviceID), empty when the HAL has none or refuses.</summary>
    public static uint[] ReadIds(uint objectId, uint selector, uint scope)
    {
        var address = new AudioPropertyAddress(selector, scope);
        uint size = 0;
        if (AudioObjectGetPropertyDataSize(objectId, &address, 0, 0, &size) != 0 || size == 0)
        {
            return [];
        }

        var ids = new uint[size / sizeof(uint)];
        fixed (uint* buffer = ids)
        {
            if (AudioObjectGetPropertyData(objectId, &address, 0, 0, &size, buffer) != 0)
            {
                return [];
            }
        }

        return ids.AsSpan(0, (int)(size / sizeof(uint))).ToArray();
    }

    /// <summary>A 32-bit property (an id, a pid, a flag), or null when the HAL refuses.</summary>
    public static uint? ReadUInt(uint objectId, uint selector, uint scope)
    {
        var address = new AudioPropertyAddress(selector, scope);
        uint value = 0;
        var size = (uint)sizeof(uint);
        return AudioObjectGetPropertyData(objectId, &address, 0, 0, &size, &value) == 0 && size == sizeof(uint) ? value : null;
    }

    /// <summary>A CFString property, released after it is read; null when the HAL refuses or gives none.</summary>
    public static string? ReadString(uint objectId, uint selector, uint scope)
    {
        var address = new AudioPropertyAddress(selector, scope);
        nint text = 0;
        var size = (uint)sizeof(nint);
        if (AudioObjectGetPropertyData(objectId, &address, 0, 0, &size, &text) != 0 || text == 0)
        {
            return null;
        }

        try
        {
            return ObjC.String(text);
        }
        finally
        {
            ObjC.Release(text);
        }
    }

    /// <summary>The number of streams of a device in the input or the output scope.</summary>
    public static int StreamCount(uint deviceId, uint scope) => ReadIds(deviceId, HalCodes.Streams, scope).Length;

    /// <summary>
    /// Calls <paramref name="changed"/> when one of <paramref name="selectors"/> of the HAL itself changes, on a thread of the
    /// HAL (since macOS 10.6 the HAL notifies on its own thread, with no run loop to turn). The listeners stay until the
    /// result is disposed.
    /// </summary>
    /// <returns>What removes them, and the status the HAL gave for each selector (0 when it took it).</returns>
    public static (IDisposable Remove, int[] Statuses) Listen(IReadOnlyList<uint> selectors, Action changed)
    {
        var key = (nint)Interlocked.Increment(ref _nextListener);
        Listeners[key] = changed;
        var statuses = new int[selectors.Count];
        var added = new List<uint>();
        for (var i = 0; i < selectors.Count; i++)
        {
            var address = new AudioPropertyAddress(selectors[i], HalCodes.ScopeGlobal);
            statuses[i] = AudioObjectAddPropertyListener(HalCodes.SystemObject, &address, &OnChanged, key);
            if (statuses[i] == 0)
            {
                added.Add(selectors[i]);
            }
        }

        return (new Removal(key, added), statuses);
    }

    [UnmanagedCallersOnly]
    private static int OnChanged(uint objectId, uint count, AudioPropertyAddress* addresses, nint clientData)
    {
        if (Listeners.TryGetValue(clientData, out var changed))
        {
            try
            {
                changed();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Never into the HAL's thread: the watch only arms a timer, and a failure there has nowhere to go.
            }
        }

        return 0;
    }

    [LibraryImport(CoreAudio)]
    private static partial int AudioObjectGetPropertyDataSize(uint objectId, AudioPropertyAddress* address, uint qualifierSize, nint qualifier, uint* size);

    [LibraryImport(CoreAudio)]
    private static partial int AudioObjectGetPropertyData(uint objectId, AudioPropertyAddress* address, uint qualifierSize, nint qualifier, uint* size, void* data);

    [LibraryImport(CoreAudio)]
    private static partial int AudioObjectAddPropertyListener(uint objectId, AudioPropertyAddress* address, delegate* unmanaged<uint, uint, AudioPropertyAddress*, nint, int> listener, nint clientData);

    [LibraryImport(CoreAudio)]
    private static partial int AudioObjectRemovePropertyListener(uint objectId, AudioPropertyAddress* address, delegate* unmanaged<uint, uint, AudioPropertyAddress*, nint, int> listener, nint clientData);

    private sealed class Removal(nint key, List<uint> selectors) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
            {
                return;
            }

            foreach (var selector in selectors)
            {
                var address = new AudioPropertyAddress(selector, HalCodes.ScopeGlobal);
                _ = AudioObjectRemovePropertyListener(HalCodes.SystemObject, &address, &OnChanged, key);
            }

            Listeners.TryRemove(key, out _);
        }
    }
}
