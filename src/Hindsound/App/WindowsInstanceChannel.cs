using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>
/// The single instance of Windows: a named mutex, held for as long as the channel lives. A second launch posts the window
/// message of <see cref="UiLoop.WakeRunningInstance"/>, and the running instance hears it on its loop.
/// </summary>
/// <param name="mutex">The mutex, already taken by <see cref="Program.TakeInstance"/>.</param>
/// <param name="first">Whether nobody held it before.</param>
internal sealed class WindowsInstanceChannel(Mutex mutex, bool first) : IInstanceChannel
{
    public bool First => first;

    public bool Tell(InstanceMessage message) => message == InstanceMessage.Show && UiLoop.WakeRunningInstance();

    // The message lands on the window of the loop, which only WindowsShell makes.
    public void Listen(IAppLoop loop, Action<InstanceMessage> received) => ((UiLoop)loop).ShowAsked = () => received(InstanceMessage.Show);

    public void Dispose() => mutex.Dispose();
}
