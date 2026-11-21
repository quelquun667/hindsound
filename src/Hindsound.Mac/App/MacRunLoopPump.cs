using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Unix.App;

namespace Hindsound.Mac.App;

/// <summary>
/// The wait of the loop between windows on macOS: the CFRunLoop of the thread that made it, run in its default mode until
/// something is handled or the time is up. A source of its own, signalled by <see cref="Wake"/>, ends the wait; a wake
/// that came before the wait stays signalled and ends the next one at once, as <see cref="ILoopPump"/> asks.
/// </summary>
/// <remarks>
/// The run loop also needs a source to wait at all: with none, CFRunLoopRunInMode returns at once. The wake source is that
/// source. What the next parts add to the same run loop (the Carbon event handler of the shortcuts, the status item of
/// the menu bar through NSApplication) is handled inside <see cref="Wait"/>, on the loop thread.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacRunLoopPump : ILoopPump, IDisposable
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // A wait with no end: CFRunLoopRunInMode takes seconds as a double, and this is more than three centuries.
    private const double Forever = 1e10;

    private readonly nint _runLoop;
    private readonly nint _source;
    private readonly nint _mode;
    private int _disposed;

    /// <summary>Attaches to the run loop of the calling thread, which is the only one that may call <see cref="Wait"/>.</summary>
    public MacRunLoopPump()
    {
        _mode = DefaultMode();
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

    /// <summary>What the last <see cref="Wait"/> got from CFRunLoopRunInMode (1 finished, 2 stopped, 3 timed out, 4 handled a source).</summary>
    public int LastResult { get; private set; }

    public void Wait(int timeoutMs)
    {
        var seconds = timeoutMs == Timeout.Infinite ? Forever : Math.Max(0, timeoutMs) / 1000.0;
        LastResult = CFRunLoopRunInMode(_mode, seconds, 1);
    }

    public void Wake()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        CFRunLoopSourceSignal(_source);
        CFRunLoopWakeUp(_runLoop);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CFRunLoopSourceInvalidate(_source);
        CFRelease(_source);
        CFRelease(_runLoop);
    }

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
