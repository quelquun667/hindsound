using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Audio.Capture;

/// <summary>
/// The two Objective-C pieces of a tap, made from C# through objc_msgSend cast to each call's exact C signature: the
/// CATapDescription (macOS 14.2 or later) and the NSDictionary that describes the private aggregate device reading it. The
/// probe of M0 made the same in Objective-C (tools/mac-probe/native/capture.m, commit 6b480c9). The keys are those of
/// AudioHardware.h.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacTapParts
{
    public const string NameKey = "name";
    public const string UidKey = "uid";
    public const string PrivateKey = "private";
    public const string StackedKey = "stacked";
    public const string TapAutoStartKey = "tapautostart";
    public const string TapListKey = "taps";
    public const string MainSubDeviceKey = "master";
    public const string SubDeviceListKey = "subdevices";
    public const string DriftCompensationKey = "drift";

    private static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend");

    /// <summary>
    /// A new tap description, owned by the caller (<see cref="ObjC.Release"/>): stereo, unmuted (the person still hears
    /// everything), named for Activity Monitor.
    /// </summary>
    /// <param name="global">The whole PC but <paramref name="processes"/>; otherwise only <paramref name="processes"/>, mixed down.</param>
    /// <param name="processes">Audio objects of processes, from <see cref="CoreAudio.ProcessObjectOf"/>.</param>
    /// <param name="deviceUid">For the whole PC only: what one output plays, rather than every output.</param>
    public static nint Description(bool global, IReadOnlyList<uint> processes, string? deviceUid = null)
    {
        CoreAudio.Load();
        var type = ObjC.Class("CATapDescription");
        if (type == 0)
        {
            throw new PlatformNotSupportedException("This macOS has no process taps (they came with macOS 14.2).");
        }

        using var pool = new ObjC.Pool();
        var array = NumberArray(processes);
        var allocated = ObjC.Send(type, "alloc");
        nint description;
        if (!global)
        {
            description = ObjC.Send(allocated, "initStereoMixdownOfProcesses:", array);
        }
        else if (deviceUid is null)
        {
            description = ObjC.Send(allocated, "initStereoGlobalTapButExcludeProcesses:", array);
        }
        else
        {
            var uid = ObjC.NSString(deviceUid);
            description = ((delegate* unmanaged<nint, nint, nint, nint, nint, nint>)MsgSend)(
                allocated, ObjC.Selector("initExcludingProcesses:andDeviceUID:withStream:"), array, uid, 0);
            ObjC.Release(uid);
            SetBool(description, "setMixdown:", true);
            SetBool(description, "setMono:", false);
        }

        if (description == 0)
        {
            throw new IOException("CATapDescription refused the processes given.");
        }

        // CATapUnmuted: the tap listens, the person still hears.
        ((delegate* unmanaged<nint, nint, nint, void>)MsgSend)(description, ObjC.Selector("setMuteBehavior:"), 0);
        var name = ObjC.NSString("Hindsound");
        ObjC.Send(description, "setName:", name);
        ObjC.Release(name);
        return description;
    }

    /// <summary>Replaces the processes of a description (the tap's own copy, read from it).</summary>
    public static void SetProcesses(nint description, IReadOnlyList<uint> processes)
    {
        using var pool = new ObjC.Pool();
        ObjC.Send(description, "setProcesses:", NumberArray(processes));
    }

    /// <summary>The processes of a description, as given to it.</summary>
    public static uint[] ProcessesOf(nint description)
    {
        using var pool = new ObjC.Pool();
        var array = ObjC.Send(description, "processes");
        var count = (int)ObjC.SendUInt(array, "count");
        var ids = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var number = ((delegate* unmanaged<nint, nint, nuint, nint>)MsgSend)(array, ObjC.Selector("objectAtIndex:"), (nuint)i);
            ids[i] = ((delegate* unmanaged<nint, nint, uint>)MsgSend)(number, ObjC.Selector("unsignedIntValue"));
        }

        return ids;
    }

    public static bool IsExclusive(nint description) => ObjC.SendBool(description, "isExclusive");

    public static bool IsMixdown(nint description) => ObjC.SendBool(description, "isMixdown");

    public static string? DeviceUidOf(nint description)
    {
        using var pool = new ObjC.Pool();
        var uid = ObjC.Send(description, "deviceUID");
        return uid == 0 ? null : ObjC.String(uid);
    }

    public static string? UuidOf(nint description)
    {
        using var pool = new ObjC.Pool();
        var uuid = ObjC.Send(description, "UUID");
        return uuid == 0 ? null : ObjC.String(ObjC.Send(uuid, "UUIDString"));
    }

    /// <summary>
    /// The description of a private aggregate that reads the tap <paramref name="tapUid"/>, with <paramref name="mainUid"/>
    /// as its main device (null for none), owned by the caller.
    /// </summary>
    public static nint Aggregate(string tapUid, string? mainUid)
    {
        using var pool = new ObjC.Pool();
        var tap = NewDictionary();
        Set(tap, DriftCompensationKey, Bool(true));
        Set(tap, UidKey, Text(tapUid));

        var aggregate = NewDictionary();
        Set(aggregate, NameKey, Text("Hindsound capture"));
        Set(aggregate, UidKey, Text("io.github.quelquun667.hindsound.tap." + Guid.NewGuid().ToString("N")));
        Set(aggregate, PrivateKey, Bool(true));
        Set(aggregate, StackedKey, Bool(false));
        Set(aggregate, TapAutoStartKey, Bool(true));
        Set(aggregate, TapListKey, Array([tap]));
        if (mainUid is not null)
        {
            var sub = NewDictionary();
            Set(sub, UidKey, Text(mainUid));
            Set(aggregate, MainSubDeviceKey, Text(mainUid));
            Set(aggregate, SubDeviceListKey, Array([sub]));
        }

        return ObjC.Send(aggregate, "retain");
    }

    /// <summary>A value of a dictionary, as text when it is one, for the tests.</summary>
    public static nint ValueOf(nint dictionary, string key)
    {
        var name = ObjC.NSString(key);
        try
        {
            return ObjC.Send(dictionary, "objectForKey:", name);
        }
        finally
        {
            ObjC.Release(name);
        }
    }

    private static nint NewDictionary() => ObjC.Send(ObjC.Class("NSMutableDictionary"), "dictionary");

    private static nint Text(string text) => ObjC.Send(ObjC.NSString(text), "autorelease");

    private static nint Bool(bool value) =>
        ((delegate* unmanaged<nint, nint, byte, nint>)MsgSend)(ObjC.Class("NSNumber"), ObjC.Selector("numberWithBool:"), value ? (byte)1 : (byte)0);

    private static void SetBool(nint receiver, string selector, bool value) =>
        ((delegate* unmanaged<nint, nint, byte, void>)MsgSend)(receiver, ObjC.Selector(selector), value ? (byte)1 : (byte)0);

    private static void Set(nint dictionary, string key, nint value) =>
        ((delegate* unmanaged<nint, nint, nint, nint, void>)MsgSend)(dictionary, ObjC.Selector("setObject:forKey:"), value, Text(key));

    private static nint Array(IEnumerable<nint> items)
    {
        var array = ObjC.Send(ObjC.Class("NSMutableArray"), "array");
        foreach (var item in items)
        {
            ObjC.Send(array, "addObject:", item);
        }

        return array;
    }

    private static nint NumberArray(IReadOnlyList<uint> numbers) => Array(numbers.Select(number =>
        ((delegate* unmanaged<nint, nint, uint, nint>)MsgSend)(ObjC.Class("NSNumber"), ObjC.Selector("numberWithUnsignedInt:"), number)));
}
