using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.App;

/// <summary>The words of a new version, by channel: a copy that installs nothing says where to download it.</summary>
internal static class UpdateNotice
{
    /// <summary>
    /// <see cref="ReleaseNotice"/> on a channel that only tells, whatever the mode: nothing there installs by itself. Otherwise
    /// the installed app's words: ready when it installs by itself, available when it waits for the user. Without a flow, the
    /// installed app's, as before.
    /// </summary>
    public static NotificationContent For(UpdateChannel? channel, UpdateVersion version, UpdateMode mode, uint accent) =>
        channel?.AnnouncesOnly == true
            ? ReleaseNotice.Content(version, accent)
            : mode == UpdateMode.Auto
                ? NotificationStates.UpdateReady(version.ToString(), accent)
                : NotificationStates.UpdateAvailable(version.ToString(), accent);
}
