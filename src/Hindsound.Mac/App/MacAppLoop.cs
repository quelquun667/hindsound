using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Unix.App;

namespace Hindsound.Mac.App;

/// <summary>
/// The loop of the Mac app: <see cref="UnixLoop"/> waiting in <see cref="MacRunLoopPump"/> between windows. Disposing it
/// ends the loop first, then frees the pump, so nothing waits on a pump already gone.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAppLoop(UnixLoop inner, MacRunLoopPump pump) : IAppLoop
{
    /// <summary>The loop itself, which the settings window of SDL takes over while it is open.</summary>
    public UnixLoop Inner => inner;

    /// <summary>The wait between windows.</summary>
    public MacRunLoopPump Pump => pump;

    public Action? CloseAsked
    {
        get => inner.CloseAsked;
        set => inner.CloseAsked = value;
    }

    public Action? ScaleMayHaveChanged
    {
        get => inner.ScaleMayHaveChanged;
        set => inner.ScaleMayHaveChanged = value;
    }

    public void Post(Action action) => inner.Post(action);

    public void After(int milliseconds, Action action) => inner.After(milliseconds, action);

    public void Run() => inner.Run();

    public void Quit() => inner.Quit();

    public bool InFront() => inner.InFront();

    public void Dispose()
    {
        inner.Dispose();
        pump.Dispose();
    }
}
