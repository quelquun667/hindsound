using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Mac.App;

namespace Hindsound.Mac.Input;

/// <summary>
/// The global shortcuts of macOS: Carbon's RegisterEventHotKey, which needs no permission, one combination per id, and one
/// handler of kEventHotKeyPressed on the application target. Everything runs on the main thread, the loop's: the handler is
/// called while <see cref="MacRunLoopPump"/> hands the events of NSApplication to <c>sendEvent:</c>.
/// <see cref="KeyByKeyHotkeys"/> turns it into the batch of the Core.
/// <para>
/// A letter or a digit is registered on the key that types it in the current keyboard layout (<see cref="MacKeyCodes.Resolve"/>),
/// as Windows reads a virtual key: Control+Option+A is the key that types A on AZERTY too. When the user picks another
/// layout, macOS posts kTISNotifySelectedKeyboardInputSourceChanged, and every shortcut whose key moved is registered again
/// (<see cref="MacHotkeyTable"/>). One that no key can hold any more stays in waiting, tried again at each change, and
/// <see cref="HeldChanged"/> tells the Core, which then shows it as in use.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class CarbonHotkeys : IHotkeyRegistrar
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // 'HdSd': the signature of the ids of this app, so that a hot key of another part of the process is never taken for ours.
    private const uint Signature = 0x48645364;
    private const uint KeyboardClass = 0x6B657962; // 'keyb'
    private const uint HotKeyPressedKind = 5; // kEventHotKeyPressed
    private const uint DirectObject = 0x2D2D2D2D; // kEventParamDirectObject, '----'
    private const uint HotKeyIdType = 0x686B6964; // typeEventHotKeyID, 'hkid'
    private const int EventNotHandled = -9874; // eventNotHandledErr
    private const nint DeliverImmediately = 4; // CFNotificationSuspensionBehaviorDeliverImmediately

    // kCGEventFlagMask*: Shift, Control, Option (Alternate), Command.
    private const ulong FlagShift = 0x20000;
    private const ulong FlagControl = 0x40000;
    private const ulong FlagOption = 0x80000;
    private const ulong FlagCommand = 0x100000;

    private static CarbonHotkeys? s_current;
    private static int s_ready;

    private readonly Action<string> _log;
    private readonly MacHotkeyTable _table;
    private nint _handler;
    private bool _observing;
    private int _relayouts;
    private int _changeThread; // 0: no change taken yet, 1: the last came on the main thread, 2: off it

    private CarbonHotkeys(Action<string> log, nint handler)
    {
        _log = log;
        _handler = handler;
        _table = new MacHotkeyTable((code, modifiers, id) => Hold(code, modifiers, id, log), Release, log);
    }

    public event Action<int>? HotkeyPressed;

    /// <summary>Raised on the main thread when a change of layout made a shortcut stop working (its key is held by another app) or work again.</summary>
    public event Action? HeldChanged;

    /// <summary>Whether a handler of the shortcuts is installed in this process now: the Hotkeys page then listens to the keys.</summary>
    public static bool Ready => Volatile.Read(ref s_ready) != 0;

    /// <summary>The ids registered now, the ones in waiting included.</summary>
    internal IReadOnlyCollection<int> Ids => _table.Ids;

    /// <summary>The layout the letters and digits are registered in; null when it could not be read (US places).</summary>
    internal MacKeyLayout? Layout => _table.Layout;

    /// <summary>How many times the layout was read again after a change macOS posted.</summary>
    internal int Relayouts => Volatile.Read(ref _relayouts);

    /// <summary>Whether the last change of layout came on the main thread (true), off it (false), or none came yet (null).</summary>
    internal bool? ChangeOnMainThread => Volatile.Read(ref _changeThread) switch { 1 => true, 2 => false, _ => null };

    /// <summary>The key code an id is registered on now (the one it waits for when it is in waiting), or null.</summary>
    internal ushort? CodeOf(int id) => _table.CodeOf(id);

    /// <summary>The shortcuts of this process, or null where Carbon cannot run them (off the main thread, or refused): the log says which. It does not throw.</summary>
    public static CarbonHotkeys? TryOpen(Action<string> log)
    {
        if (!Cocoa.OnMainThread)
        {
            log("hotkeys: Carbon runs on the main thread only");
            return null;
        }

        if (s_current is not null)
        {
            throw new InvalidOperationException("the shortcuts of the app are already open");
        }

        CarbonHotkeys? keys = null;
        try
        {
            var spec = new EventTypeSpec(KeyboardClass, HotKeyPressedKind);
            nint handler;
            var status = InstallEventHandler(GetApplicationEventTarget(), &OnEvent, 1, &spec, 0, &handler);
            if (status != 0)
            {
                log($"hotkeys: InstallEventHandler failed ({status})");
                return null;
            }

            log("hotkeys: Carbon, handler on the application target");
            keys = new CarbonHotkeys(log, handler);
            s_current = keys;

            // The observer first, then the layout: a change between the two is taken once the loop runs, not missed.
            keys.Watch();
            keys._table.Use(keys.ReadLayout());
            log($"hotkeys: keyboard layout {keys.Layout?.Name ?? "none"}");
            Volatile.Write(ref s_ready, 1);
            return keys;
        }
        catch (Exception ex)
        {
            // Never leave the handler installed behind a failure.
            log("hotkeys: could not be opened: " + ex.Message);
            keys?.Dispose();
            return null;
        }
    }

    public bool Register(int id, uint modifiers, uint virtualKey) => TryRegister(id, modifiers, virtualKey) == HotkeyRegistration.Registered;

    public HotkeyRegistration TryRegister(int id, uint modifiers, uint virtualKey)
    {
        if (_handler == 0 || !Cocoa.OnMainThread)
        {
            _log($"hotkeys: {id} not registered: closed, or off the main thread");
            return HotkeyRegistration.Refused;
        }

        if (_table.CodeFor(virtualKey) is not { } code)
        {
            _log($"hotkeys: no key of a Mac for the virtual key 0x{virtualKey:X2}");
            return HotkeyRegistration.Refused;
        }

        Unregister(id);
        return _table.Add(id, modifiers, virtualKey, code) ? HotkeyRegistration.Registered : HotkeyRegistration.Refused;
    }

    public void Unregister(int id) => _table.Remove(id);

    /// <summary>Whether a press of the id reaches the app: false for a shortcut in waiting, which a change of layout lost to another app.</summary>
    public bool IsHeld(int id) => _table.IsHeld(id);

    // The layout of the system now, or null (the US places) when it cannot be read: the line says why. Never throws.
    private MacKeyLayout? ReadLayout()
    {
        try
        {
            return MacKeyboardLayouts.Current(_log);
        }
        catch (Exception ex)
        {
            _log($"hotkeys: the keyboard layout could not be read ({ex.Message}): letters and digits at their places on a US keyboard");
            return null;
        }
    }

    // Follows the changes of layout; without the observer the shortcuts still work, on the keys of the layout read at the start.
    private void Watch()
    {
        try
        {
            CFNotificationCenterAddObserver(CFNotificationCenterGetDistributedCenter(), _handler, &OnLayoutChanged, MacKeyboardLayouts.SelectedChanged, 0, DeliverImmediately);
            _observing = true;
            _log("hotkeys: watching the keyboard layout");
        }
        catch (Exception ex)
        {
            _log($"hotkeys: the changes of keyboard layout cannot be followed ({ex.Message})");
        }
    }

    private static nint? Hold(ushort code, uint modifiers, int id, Action<string> log)
    {
        nint reference;
        var status = RegisterEventHotKey(code, MacKeyCodes.ToModifiers(modifiers), new EventHotKeyId(Signature, (uint)id), GetApplicationEventTarget(), 0, &reference);
        if (status != 0)
        {
            // eventHotKeyExistsErr (-9878): another app holds it; eventHotKeyInvalidErr (-9868): macOS refuses the combination.
            log($"hotkeys: RegisterEventHotKey refused key code 0x{code:X2} with modifiers 0x{MacKeyCodes.ToModifiers(modifiers):X4} ({status})");
            return null;
        }

        return reference;
    }

    private static void Release(nint reference) => UnregisterEventHotKey(reference);

    /// <summary>
    /// Holds a combination as another app would, for the proof: a registration of this process under another signature, which
    /// the handler of the shortcuts ignores. Its reference, or null when it is refused. <see cref="ReleaseOutside"/> frees it.
    /// </summary>
    internal static nint? HoldOutside(ushort code, uint modifiers)
    {
        nint reference;
        var status = RegisterEventHotKey(code, MacKeyCodes.ToModifiers(modifiers), new EventHotKeyId(0x4F757473, 1), GetApplicationEventTarget(), 0, &reference); // 'Outs'
        return status == 0 ? reference : null;
    }

    internal static void ReleaseOutside(nint reference) => UnregisterEventHotKey(reference);

    public void Dispose()
    {
        if (_handler == 0)
        {
            return;
        }

        _table.Clear();
        if (_observing)
        {
            try
            {
                CFNotificationCenterRemoveObserver(CFNotificationCenterGetDistributedCenter(), _handler, MacKeyboardLayouts.SelectedChanged, 0);
            }
            catch (Exception ex)
            {
                _log($"hotkeys: the observer of the keyboard layout could not be removed ({ex.Message})");
            }

            _observing = false;
        }

        RemoveEventHandler(_handler);
        _handler = 0;
        s_current = null;
        Volatile.Write(ref s_ready, 0);
    }

    /// <summary>
    /// Sends the application target the event Carbon sends for a press of <paramref name="id"/>, built by hand
    /// (CreateEvent, SendEventToEventTarget): the handler and the id it reads are proved, not the press of a key.
    /// </summary>
    internal bool SendPressed(int id)
    {
        nint pressed;
        if (CreateEvent(0, KeyboardClass, HotKeyPressedKind, 0, 0, &pressed) != 0)
        {
            return false;
        }

        try
        {
            var hotKey = new EventHotKeyId(Signature, (uint)id);
            return SetEventParameter(pressed, DirectObject, HotKeyIdType, (nuint)sizeof(EventHotKeyId), &hotKey) == 0
                && SendEventToEventTarget(pressed, GetApplicationEventTarget()) == 0;
        }
        finally
        {
            ReleaseEvent(pressed);
        }
    }

    /// <summary>Whether this process may post keyboard events (the PostEvent right of TCC).</summary>
    internal static bool MayPostKeys() => CGPreflightPostEventAccess() != 0;

    /// <summary>
    /// Posts a press and a release of the key at the place of the HID system, as a keyboard would: the window server matches
    /// it against the registered shortcuts. Whether both events were posted, not whether they reached anyone.
    /// </summary>
    internal static bool PostKey(uint modifiers, uint virtualKey)
    {
        if (MacKeyCodes.Resolve(virtualKey, s_current?.Layout) is not ({ } code, _))
        {
            return false;
        }

        var flags = ((modifiers & HotkeyGesture.ModShift) != 0 ? FlagShift : 0)
            | ((modifiers & HotkeyGesture.ModControl) != 0 ? FlagControl : 0)
            | ((modifiers & HotkeyGesture.ModAlt) != 0 ? FlagOption : 0)
            | ((modifiers & HotkeyGesture.ModWin) != 0 ? FlagCommand : 0);
        foreach (var down in new[] { true, false })
        {
            var key = CGEventCreateKeyboardEvent(0, code, down ? (byte)1 : (byte)0);
            if (key == 0)
            {
                return false;
            }

            CGEventSetFlags(key, flags);
            CGEventPost(0, key); // kCGHIDEventTap
            CFRelease(key);
        }

        return true;
    }

    // On the main thread, from sendEvent: or SendEventToEventTarget. Nothing may throw into Carbon.
    [UnmanagedCallersOnly]
    private static int OnEvent(nint callRef, nint carbonEvent, nint userData)
    {
        EventHotKeyId hotKey;
        if (GetEventParameter(carbonEvent, DirectObject, HotKeyIdType, 0, (nuint)sizeof(EventHotKeyId), 0, &hotKey) != 0
            || hotKey.Signature != Signature || s_current is not { } keys)
        {
            return EventNotHandled;
        }

        try
        {
            keys.HotkeyPressed?.Invoke((int)hotKey.Id);
        }
        catch (Exception ex)
        {
            keys._log("hotkeys: a press could not be handled: " + ex);
        }

        return 0;
    }

    // From the run loop of the main thread: the user picked another input source. Nothing may throw into CoreFoundation.
    [UnmanagedCallersOnly]
    private static void OnLayoutChanged(nint center, nint observer, nint name, nint obj, nint userInfo)
    {
        if (s_current is not { } keys || observer != keys._handler)
        {
            return;
        }

        try
        {
            var onMain = Cocoa.OnMainThread;
            Volatile.Write(ref keys._changeThread, onMain ? 1 : 2);
            if (keys._table.Retake(onMain, () => keys.ReadLayout(), out var heldChanged))
            {
                Interlocked.Increment(ref keys._relayouts);
                if (heldChanged)
                {
                    keys.HeldChanged?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            keys._log("hotkeys: the new keyboard layout could not be taken: " + ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EventTypeSpec(uint eventClass, uint eventKind)
    {
        public readonly uint EventClass = eventClass;
        public readonly uint EventKind = eventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EventHotKeyId(uint signature, uint id)
    {
        public readonly uint Signature = signature;
        public readonly uint Id = id;
    }

    [LibraryImport(Carbon)]
    private static partial nint GetApplicationEventTarget();

    [LibraryImport(Carbon)]
    private static partial int InstallEventHandler(nint target, delegate* unmanaged<nint, nint, nint, int> handler, nuint typeCount, EventTypeSpec* types, nint userData, nint* handlerRef);

    [LibraryImport(Carbon)]
    private static partial int RemoveEventHandler(nint handlerRef);

    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, nint target, uint options, nint* hotKeyRef);

    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(nint hotKeyRef);

    [LibraryImport(Carbon)]
    private static partial int GetEventParameter(nint carbonEvent, uint name, uint type, nint actualType, nuint size, nint actualSize, void* data);

    [LibraryImport(Carbon)]
    private static partial int CreateEvent(nint allocator, uint eventClass, uint kind, double when, uint attributes, nint* carbonEvent);

    [LibraryImport(Carbon)]
    private static partial int SetEventParameter(nint carbonEvent, uint name, uint type, nuint size, void* data);

    [LibraryImport(Carbon)]
    private static partial int SendEventToEventTarget(nint carbonEvent, nint target);

    [LibraryImport(Carbon)]
    private static partial void ReleaseEvent(nint carbonEvent);

    [LibraryImport(CoreGraphics)]
    private static partial byte CGPreflightPostEventAccess();

    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreateKeyboardEvent(nint source, ushort keyCode, byte keyDown);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetFlags(nint cgEvent, ulong flags);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventPost(uint tap, nint cgEvent);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFNotificationCenterGetDistributedCenter();

    [LibraryImport(CoreFoundation)]
    private static partial void CFNotificationCenterAddObserver(nint center, nint observer, delegate* unmanaged<nint, nint, nint, nint, nint, void> callback, nint name, nint obj, nint suspensionBehavior);

    [LibraryImport(CoreFoundation)]
    private static partial void CFNotificationCenterRemoveObserver(nint center, nint observer, nint name, nint obj);
}
