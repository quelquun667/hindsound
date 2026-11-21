using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Unix.Desktop;

namespace Hindsound.Mac.Desktop;

/// <summary>The desktop parts of a macOS run.</summary>
[SupportedOSPlatform("macos")]
internal static class MacDesktop
{
    public static DesktopParts Parts(Action<string> log) =>
        new(new MacScreens(), new MacQuietState(), new SilentConfirmSound(), new MacOpen(log), new MacProcessMeter());
}

/// <summary>
/// Focus (do not disturb) of macOS: no public interface tells an app whether it is on, so the app always takes
/// notifications as welcome, as it does where a Linux desktop says nothing.
/// </summary>
internal sealed class MacQuietState : IQuietState
{
    public UserNotificationState Read() => UserNotificationState.AcceptsNotifications;
}

/// <summary>
/// Memory and processor time of this process, from what the runtime reads of macOS: the resident size is what Windows calls
/// the working set; the private bytes, which macOS does not give the runtime (it answers 0), are said to be unmeasured.
/// </summary>
internal sealed class MacProcessMeter : IProcessMeter
{
    public string Memory()
    {
        using var process = Process.GetCurrentProcess();
        return MemoryLine(process.WorkingSet64, process.PrivateMemorySize64);
    }

    public double CpuMilliseconds()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime.TotalMilliseconds;
    }

    /// <summary>The line of the log, in the words of the Windows meter.</summary>
    public static string MemoryLine(long workingSet, long privateBytes) =>
        workingSet <= 0
            ? "memory not measured (no resident size)"
            : privateBytes > 0
                ? string.Create(CultureInfo.InvariantCulture, $"working set {workingSet / 1048576.0:F1} MB, private {privateBytes / 1048576.0:F1} MB")
                : string.Create(CultureInfo.InvariantCulture, $"working set {workingSet / 1048576.0:F1} MB, private not measured");
}

/// <summary>What a signal asks of the app.</summary>
internal enum MacSignalStep
{
    /// <summary>The runtime's own way: the process ends now.</summary>
    Default,

    /// <summary>The session ends: the app writes what it may and goes.</summary>
    EndSession,

    /// <summary>Asked to quit, as Quit in the menu: clips being written are waited for.</summary>
    AskToQuit,
}

/// <summary>
/// The end of the session as this app hears it on macOS: SIGTERM, which launchd sends to the processes of the user at
/// logout (and which <c>kill</c> sends), and SIGINT, Ctrl+C in a terminal; a second Ctrl+C ends the process at once. The
/// app has no NSApplication delegate to ask the session to wait (applicationShouldTerminate:), so it never asks: Block
/// says no.
/// </summary>
internal sealed class MacSessionEnd : ISessionEnd
{
    private readonly IAppLoop _loop;
    private readonly Action<string> _log;
    private readonly List<PosixSignalRegistration> _registrations = [];
    private int _interrupts;

    [SupportedOSPlatform("macos")]
    public MacSessionEnd(IAppLoop loop, Action<string> log)
        : this(loop, log, register: true)
    {
    }

    internal MacSessionEnd(IAppLoop loop, Action<string> log, bool register)
    {
        _loop = loop;
        _log = log;
        if (!register)
        {
            return;
        }

        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT })
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, context => context.Cancel = Handle(context.Signal)));
        }
    }

    public Action? Ending { get; set; }

    public Action? Asked { get; set; }

    public Action? Kept { get; set; }

    public bool Block(string reason) => false;

    public bool Unblock() => false;

    /// <summary>What <paramref name="signal"/> asks, when this is the <paramref name="interrupts"/>th SIGINT so far.</summary>
    public static MacSignalStep StepFor(PosixSignal signal, int interrupts) => signal switch
    {
        PosixSignal.SIGTERM => MacSignalStep.EndSession,
        PosixSignal.SIGINT when interrupts <= 1 => MacSignalStep.AskToQuit,
        _ => MacSignalStep.Default,
    };

    /// <summary>Takes <paramref name="signal"/>, from the thread the runtime gives it. Whether the runtime's own way is cancelled.</summary>
    internal bool Handle(PosixSignal signal)
    {
        var interrupts = signal == PosixSignal.SIGINT ? Interlocked.Increment(ref _interrupts) : Volatile.Read(ref _interrupts);
        switch (StepFor(signal, interrupts))
        {
            case MacSignalStep.EndSession:
                _loop.Post(() =>
                {
                    _log("SIGTERM: the session ends");
                    Ending?.Invoke();
                });
                return true;
            case MacSignalStep.AskToQuit:
                _loop.Post(() =>
                {
                    _log("SIGINT: asked to quit");
                    if (_loop.CloseAsked is { } close)
                    {
                        close();
                    }
                    else
                    {
                        Ending?.Invoke();
                    }
                });
                return true;
            default:
                return false;
        }
    }
}
