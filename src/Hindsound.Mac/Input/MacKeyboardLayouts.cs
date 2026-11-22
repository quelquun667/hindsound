using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Hindsound.Mac.Input;

/// <summary>
/// The keyboard layouts of macOS (Text Input Sources): the current one, or one named by its id, read into a
/// <see cref="MacKeyLayout"/> through UCKeyTranslate; and the selection of a layout, for the proof of the CI. On the main
/// thread: the Text Input Sources belong to it.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacKeyboardLayouts
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const ushort KeyActionDown = 0; // kUCKeyActionDown
    private const uint NoDeadKeys = 1; // kUCKeyTranslateNoDeadKeysMask: a dead key gives its own character
    private const uint ShiftState = 0x02; // (shiftKey >> 8) & 0xFF
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    private static readonly nint CarbonHandle = NativeLibrary.Load(Carbon);
    private static readonly nint CoreFoundationHandle = NativeLibrary.Load(CoreFoundation);

    /// <summary>The name of the distributed notification posted when the selected input source changes.</summary>
    public static nint SelectedChanged => Constant(CarbonHandle, "kTISNotifySelectedKeyboardInputSourceChanged");

    /// <summary>
    /// The layout the keyboard types with now (TISCopyCurrentKeyboardLayoutInputSource), or the current ASCII-capable one
    /// when it has no Unicode table; null, with a line in the log, when neither can be read.
    /// </summary>
    public static MacKeyLayout? Current(Action<string> log)
    {
        foreach (var (copy, what) in new (Func<nint>, string)[]
        {
            (() => TISCopyCurrentKeyboardLayoutInputSource(), "current"),
            (() => TISCopyCurrentASCIICapableKeyboardLayoutInputSource(), "current ASCII-capable"),
        })
        {
            var source = copy();
            if (source == 0)
            {
                continue;
            }

            try
            {
                if (Read(source) is { } layout)
                {
                    return layout;
                }

                log($"hotkeys: the {what} keyboard layout {IdOf(source)} has no Unicode table");
            }
            finally
            {
                CFRelease(source);
            }
        }

        log("hotkeys: no keyboard layout could be read: letters and digits at their places on a US keyboard");
        return null;
    }

    /// <summary>The id of the layout the keyboard types with now (com.apple.keylayout.US), or null.</summary>
    public static string? CurrentId()
    {
        var source = TISCopyCurrentKeyboardLayoutInputSource();
        if (source == 0)
        {
            return null;
        }

        try
        {
            return IdOf(source);
        }
        finally
        {
            CFRelease(source);
        }
    }

    /// <summary>The installed layout of id <paramref name="id"/> read as it types, whichever is selected; null when there is none.</summary>
    public static MacKeyLayout? ById(string id) => WithSource(id, source => Read(source));

    /// <summary>Enables and selects the layout of id <paramref name="id"/>: the status of TISSelectInputSource, or -1 when there is none.</summary>
    public static int Select(string id) => WithSource(id, source =>
    {
        TISEnableInputSource(source);
        return TISSelectInputSource(source);
    }, -1);

    /// <summary>Whether the layout of id <paramref name="id"/> is enabled in the input sources of the user (kTISPropertyInputSourceIsEnabled); false when there is none.</summary>
    public static bool IsEnabled(string id) => WithSource(id, source =>
    {
        var value = TISGetInputSourceProperty(source, Constant(CarbonHandle, "kTISPropertyInputSourceIsEnabled"));
        return value != 0 && CFBooleanGetValue(value) != 0;
    }, false);

    /// <summary>Disables the layout of id <paramref name="id"/>, which <see cref="Select"/> had enabled: the status of TISDisableInputSource, or -1 when there is none. A selected layout cannot be disabled.</summary>
    public static int Disable(string id) => WithSource(id, source => TISDisableInputSource(source), -1);

    private static T WithSource<T>(string id, Func<nint, T> use, T none = default!)
    {
        var key = Constant(CarbonHandle, "kTISPropertyInputSourceID");
        var value = NewString(id);
        var keyCallbacks = NativeLibrary.GetExport(CoreFoundationHandle, "kCFTypeDictionaryKeyCallBacks");
        var valueCallbacks = NativeLibrary.GetExport(CoreFoundationHandle, "kCFTypeDictionaryValueCallBacks");
        var properties = CFDictionaryCreate(0, &key, &value, 1, keyCallbacks, valueCallbacks);
        CFRelease(value);
        var list = TISCreateInputSourceList(properties, 1);
        CFRelease(properties);
        if (list == 0)
        {
            return none;
        }

        try
        {
            return CFArrayGetCount(list) > 0 ? use(CFArrayGetValueAtIndex(list, 0)) : none;
        }
        finally
        {
            CFRelease(list);
        }
    }

    // The Unicode table of a source, read key by key; null when it has none (an input method rather than a layout).
    private static MacKeyLayout? Read(nint source)
    {
        var data = TISGetInputSourceProperty(source, Constant(CarbonHandle, "kTISPropertyUnicodeKeyLayoutData"));
        if (data == 0)
        {
            return null;
        }

        var table = CFDataGetBytePtr(data);
        var keyboardType = (uint)LMGetKbdType();
        return MacKeyLayout.From(IdOf(source) ?? "?", (code, shift) =>
        {
            uint deadKeys = 0;
            nuint length = 0;
            var chars = stackalloc char[4];
            var status = UCKeyTranslate(table, code, KeyActionDown, shift ? ShiftState : 0, keyboardType, NoDeadKeys, &deadKeys, 4, &length, chars);
            return status == 0 && length > 0 ? new string(chars, 0, (int)length) : null;
        });
    }

    private static string? IdOf(nint source)
    {
        var id = TISGetInputSourceProperty(source, Constant(CarbonHandle, "kTISPropertyInputSourceID"));
        if (id == 0)
        {
            return null;
        }

        var buffer = stackalloc byte[256];
        return CFStringGetCString(id, buffer, 256, Utf8) != 0 ? Marshal.PtrToStringUTF8((nint)buffer) : null;
    }

    private static nint NewString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* pointer = bytes)
        {
            return CFStringCreateWithCString(0, pointer, Utf8);
        }
    }

    private static nint Constant(nint library, string name) => *(nint*)NativeLibrary.GetExport(library, name);

    [LibraryImport(Carbon)]
    private static partial nint TISCopyCurrentKeyboardLayoutInputSource();

    [LibraryImport(Carbon)]
    private static partial nint TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

    [LibraryImport(Carbon)]
    private static partial nint TISGetInputSourceProperty(nint source, nint key);

    [LibraryImport(Carbon)]
    private static partial nint TISCreateInputSourceList(nint properties, byte includeAllInstalled);

    [LibraryImport(Carbon)]
    private static partial int TISEnableInputSource(nint source);

    [LibraryImport(Carbon)]
    private static partial int TISSelectInputSource(nint source);

    [LibraryImport(Carbon)]
    private static partial int TISDisableInputSource(nint source);

    [LibraryImport(Carbon)]
    private static partial byte LMGetKbdType();

    [LibraryImport(Carbon)]
    private static partial int UCKeyTranslate(nint layout, ushort keyCode, ushort keyAction, uint modifierKeyState, uint keyboardType, uint options, uint* deadKeyState, nuint maxLength, nuint* actualLength, char* chars);

    [LibraryImport(CoreFoundation)]
    private static partial byte CFBooleanGetValue(nint value);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallbacks, nint valueCallbacks);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringCreateWithCString(nint allocator, byte* text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial byte CFStringGetCString(nint text, byte* buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
