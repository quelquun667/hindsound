using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;
using Hindsound.Unix.App;

namespace Hindsound.Mac.App;

/// <summary>
/// The wait of the loop between windows on macOS. On the main thread it is the event queue of NSApplication: it waits in
/// <c>nextEventMatchingMask:untilDate:inMode:dequeue:</c>, hands every event to <c>sendEvent:</c> (the clicks on the icon
/// of the menu bar, the Carbon shortcuts), and <see cref="Wake"/> posts an application-defined event of its own. On any
/// other thread, where AppKit may not run, it is the CFRunLoop of that thread, run in its default mode until a source is
/// handled or the time is up, with a source of its own that <see cref="Wake"/> signals. Either way, a wake that came before
/// the wait ends the next one at once, as <see cref="ILoopPump"/> asks.
/// </summary>
/// <remarks>
/// While a window is open, SDL's pump takes over and empties the same queue of NSApplication: a wake event of ours it eats is
/// harmless, and <see cref="Wait"/> treats a wake still marked as pending as one that came. <see cref="Wake"/> and
/// <see cref="Dispose"/> share a lock: a wake from another thread never touches a source already freed.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacRunLoopPump : ILoopPump, IDisposable
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // A wait with no end: CFRunLoopRunInMode takes seconds as a double, and this is more than three centuries.
    private const double Forever = 1e10;

    // data1 of the wake event, so that it is never taken for an event of SDL or of the system ("HSwk").
    private const nint WakeMark = 0x4853776B;

    // The most events handed out after a wait before it returns, so that a flood never starves the loop's actions.
    private const int MostEventsAtOnce = 64;

    private static bool s_launched;

    private readonly object _gate = new();
    private readonly nint _runLoop;
    private readonly nint _source;
    private readonly nint _mode;
    private readonly nint _app;
    private int _wakePending;
    private bool _disposed;

    /// <summary>
    /// Attaches to the calling thread, which is the only one that may call <see cref="Wait"/>: the events of NSApplication
    /// on the main thread, the CFRunLoop elsewhere.
    /// </summary>
    public MacRunLoopPump()
        : this(Cocoa.OnMainThread)
    {
    }

    /// <param name="appEvents">Waits on the events of NSApplication; only on the main thread.</param>
    internal MacRunLoopPump(bool appEvents)
    {
        _mode = DefaultMode();
        if (appEvents)
        {
            if (!Cocoa.OnMainThread)
            {
                throw new InvalidOperationException("the events of NSApplication are read on the main thread only");
            }

            using var pool = new ObjC.Pool();
            _app = Cocoa.SharedApplication();
            if (_app == 0)
            {
                throw new InvalidOperationException("NSApplication sharedApplication gave nil");
            }

            // An app of the menu bar: no Dock icon until a window says otherwise. finishLaunching once, as -run would.
            Cocoa.SendInt(_app, "setActivationPolicy:", Cocoa.AccessoryPolicy);
            if (!s_launched)
            {
                ObjC.Send(_app, "finishLaunching");
                s_launched = true;
            }

            return;
        }

        _runLoop = CFRunLoopGetCurrent();
        CFRetain(_runLoop);
        var context = new SourceContext { Perform = &Perform };
        _source = CFRunLoopSourceCreate(0, 0, &context);
        if (_source == 0)
        {
            throw new InvalidOperationException("CFRunLoopSourceCreate failed");
        }

        CFRunLoopAddSource(_runLoop, _source, _mode);
    }

    /// <summary>Whether this pump waits on the events of NSApplication (the main thread) rather than on the bare run loop.</summary>
    public bool AppEvents => _app != 0;

    /// <summary>Whether <see cref="Dispose"/> has run: every wake since is a no-op.</summary>
    public bool Disposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>How many events of NSApplication the pump has handed to <c>sendEvent:</c>, its own wakes left out.</summary>
    public long EventsSent { get; private set; }

    /// <summary>What the last <see cref="Wait"/> got from CFRunLoopRunInMode (1 finished, 2 stopped, 3 timed out, 4 handled a source).</summary>
    public int LastResult { get; private set; }

    public void Wait(int timeoutMs)
    {
        if (_app == 0)
        {
            var seconds = timeoutMs == Timeout.Infinite ? Forever : Math.Max(0, timeoutMs) / 1000.0;
            LastResult = CFRunLoopRunInMode(_mode, seconds, 1);
            return;
        }

        using var pool = new ObjC.Pool();

        // A wake still pending: its event may have been eaten by SDL's pump while a window was open.
        if (Interlocked.Exchange(ref _wakePending, 0) != 0)
        {
            HandOut(Cocoa.ReadInt(Cocoa.Class("NSDate"), "distantPast"));
            return;
        }

        var until = timeoutMs == Timeout.Infinite
            ? Cocoa.ReadInt(Cocoa.Class("NSDate"), "distantFuture")
            : Cocoa.Send(Cocoa.Class("NSDate"), "dateWithTimeIntervalSinceNow:", Math.Max(0, timeoutMs) / 1000.0);
        if (Cocoa.NextEvent(_app, until, _mode) is var first and not 0)
        {
            Dispatch(first);
            HandOut(Cocoa.ReadInt(Cocoa.Class("NSDate"), "distantPast"));
        }
    }

    public void Wake()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_app == 0)
            {
                CFRunLoopSourceSignal(_source);
                CFRunLoopWakeUp(_runLoop);
                return;
            }

            if (Interlocked.Exchange(ref _wakePending, 1) == 0)
            {
                using var pool = new ObjC.Pool();
                Cocoa.PostEvent(_app, Cocoa.ApplicationEvent(WakeMark));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_app == 0)
            {
                CFRunLoopSourceInvalidate(_source);
                CFRelease(_source);
                CFRelease(_runLoop);
            }
        }
    }

    // The events already queued, without waiting.
    private void HandOut(nint now)
    {
        for (var i = 0; i < MostEventsAtOnce && Cocoa.NextEvent(_app, now, _mode) is var next and not 0; i++)
        {
            Dispatch(next);
        }
    }

    private void Dispatch(nint nsEvent)
    {
        if (IsWake(nsEvent))
        {
            Interlocked.Exchange(ref _wakePending, 0);
            return;
        }

        ObjC.Send(_app, "sendEvent:", nsEvent);
        EventsSent++;
    }

    private static bool IsWake(nint nsEvent) =>
        ObjC.SendUInt(nsEvent, "type") == Cocoa.ApplicationDefined && Cocoa.ReadInt(nsEvent, "data1") == WakeMark;

    // The wake carries nothing: being handled is what ends the wait.
    [UnmanagedCallersOnly]
    private static void Perform(void* info)
    {
    }

    private static nint DefaultMode() =>
        *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFRunLoopDefaultMode");

    // CFRunLoopSourceContext, version 0: ten pointer-sized fields, perform last.
    [StructLayout(LayoutKind.Sequential)]
    private struct SourceContext
    {
        public nint Version;
        public nint Info;
        public nint Retain;
        public nint Release;
        public nint CopyDescription;
        public nint Equal;
        public nint Hash;
        public nint Schedule;
        public nint Cancel;
        public delegate* unmanaged<void*, void> Perform;
    }

    [LibraryImport(CoreFoundation)]
    private static partial nint CFRunLoopGetCurrent();

    [LibraryImport(CoreFoundation)]
    private static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFRunLoopSourceCreate(nint allocator, nint order, SourceContext* context);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopSourceSignal(nint source);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopSourceInvalidate(nint source);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopWakeUp(nint runLoop);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFRetain(nint value);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
