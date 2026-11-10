using System.Runtime.InteropServices;
using Hindsound.App;

namespace Hindsound.Linux.Desktop;

/// <summary>What a signal asks of the app.</summary>
internal enum SignalStep
{
    /// <summary>The runtime's own way: the process ends now.</summary>
    Default,

    /// <summary>The session ends: the app writes what it may and goes, as at a Windows logoff.</summary>
    EndSession,

    /// <summary>Asked to quit, as Quit in the menu: clips being written are waited for.</summary>
    AskToQuit,
}

/// <summary>
/// The end of the session as Linux tells it: SIGTERM, which the session manager sends to every process at logout (and which
/// <c>kill</c> sends), and SIGINT, which Ctrl+C sends in a terminal. A second Ctrl+C ends the process at once, for whoever
/// does not want to wait for a clip. Linux has no way here for the app to ask the session to wait, so it never does.
/// </summary>
internal sealed class LinuxSessionEnd : ISessionEnd
{
    private readonly IAppLoop _loop;
    private readonly Action<string> _log;
    private readonly List<PosixSignalRegistration> _registrations = [];
    private int _interrupts;

    public LinuxSessionEnd(IAppLoop loop, Action<string> log)
        : this(loop, log, register: true)
    {
    }

    internal LinuxSessionEnd(IAppLoop loop, Action<string> log, bool register)
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
    public static SignalStep StepFor(PosixSignal signal, int interrupts) => signal switch
    {
        PosixSignal.SIGTERM => SignalStep.EndSession,
        PosixSignal.SIGINT when interrupts <= 1 => SignalStep.AskToQuit,
        _ => SignalStep.Default,
    };

    /// <summary>Takes <paramref name="signal"/>, from the thread the runtime gives it. Whether the runtime's own way is cancelled.</summary>
    internal bool Handle(PosixSignal signal)
    {
        var interrupts = signal == PosixSignal.SIGINT ? Interlocked.Increment(ref _interrupts) : Volatile.Read(ref _interrupts);
        var step = StepFor(signal, interrupts);
        switch (step)
        {
            case SignalStep.EndSession:
                _loop.Post(() =>
                {
                    _log("SIGTERM: the session ends");
                    Ending?.Invoke();
                });
                return true;

            case SignalStep.AskToQuit:
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
