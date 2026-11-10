using Hindsound.App;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The Linux app plays nothing: then the capture has nothing of its own to leave out. The notification asks the server for
/// no sound either.
/// </summary>
internal sealed class SilentConfirmSound : IConfirmSound
{
    public bool Play() => false;
}
