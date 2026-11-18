using Hindsound.App;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The Linux app plays no confirmation sound: the only sound it plays is a clip listened to, which the capture of the PC's
/// sound reads as silence while it plays. The notification asks the server for no sound either.
/// </summary>
internal sealed class SilentConfirmSound : IConfirmSound
{
    public bool Play() => false;
}
