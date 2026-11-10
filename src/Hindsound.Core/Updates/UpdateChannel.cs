namespace Hindsound.Updates;

/// <summary>What the app does with a newer version, beyond the daily check of its manifest.</summary>
internal enum UpdateDelivery
{
    /// <summary>Downloads the Setup the manifest names and runs it: the installed Windows app.</summary>
    Setup,

    /// <summary>
    /// Tells the user once per version, and a click opens the page of the release. Nothing is downloaded or installed: for a
    /// platform where the app cannot replace itself (Linux, and later a portable Windows copy).
    /// </summary>
    AnnounceOnly,
}

/// <summary>Which manifest a platform reads, and what it does with what the manifest names. Given by the platform layer.</summary>
/// <param name="ManifestName">The file name of the manifest among the assets of the latest release.</param>
/// <param name="Delivery">Install, or only tell.</param>
internal sealed record UpdateChannel(string ManifestName, UpdateDelivery Delivery)
{
    /// <summary>The installed Windows app: its manifest names a Setup.</summary>
    public static UpdateChannel WindowsSetup { get; } = new("hindsound-update-win-x64.json", UpdateDelivery.Setup);

    /// <summary>The Linux app: its manifest names the page of the release, which the user downloads from.</summary>
    public static UpdateChannel LinuxAnnounce { get; } = new("hindsound-update-linux-x64.json", UpdateDelivery.AnnounceOnly);

    /// <summary>Throws for a name that is not a plain .json file name: the names come from the code, never from the network.</summary>
    public string ManifestUrl => UpdateUrlPolicy.ManifestUrlFor(ManifestName);

    public bool AnnouncesOnly => Delivery == UpdateDelivery.AnnounceOnly;
}

/// <summary>The words of a new version on a platform that does not install it: the click opens the release page.</summary>
internal static class ReleaseNotice
{
    public const string Body = "Click to download it.";

    public static string Title(UpdateVersion version) => $"Hindsound {version} is out";

    /// <summary>
    /// Of the kind of the other announcements, so it waits for the desktop, is shown once, and counts as made when on screen.
    /// It opens no window: the app routes its click to the release page.
    /// </summary>
    public static Ui.NotificationContent Content(UpdateVersion version, uint accent) =>
        new(Ui.NotificationKind.UpdateReady, Title(version), Body, [], accent, Ui.NotificationVisual.Pending, accent, []);
}
