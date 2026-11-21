namespace Hindsound.App;

/// <summary>How the global shortcuts are set on this system, which decides what the Hotkeys page shows.</summary>
internal enum ShortcutMode
{
    /// <summary>The app grabs the key a person types: the field of each action listens to the keyboard. Windows, and X11.</summary>
    Capture,

    /// <summary>The desktop binds the keys itself and says which: the page shows its words for each action, and has no field to type in.</summary>
    Portal,

    /// <summary>The system has no global shortcut for an app: a key of the desktop's own keyboard settings must run a command.</summary>
    Command,
}

/// <summary>
/// What the system offers the pages of the window, and the words it has for its own things. The default is the installed Windows
/// app, exactly as the pages have always been; another system makes its own with <c>with</c> and fills
/// <see cref="PlatformDefaults.Features"/> once at startup.
/// </summary>
internal sealed record PlatformFeatures
{
    /// <summary>The command a desktop's keyboard settings are told to run when nothing grabs a key.</summary>
    public const string DefaultSaveCommand = "hindsound --save";

    /// <summary>The installed Windows app.</summary>
    public static PlatformFeatures Windows { get; } = new();

    /// <summary>
    /// A Linux desktop. The notification is the desktop's own (no look and no position to choose), nothing plays a confirmation
    /// sound, and the words are those of the desktop. One application can be captured alone, as on Windows.
    /// </summary>
    /// <param name="shortcuts">How the shortcuts are set now. Read at each build of a page: the portal answers after the window opens.</param>
    /// <param name="saveCommand">
    /// The command to bind in the keyboard settings, when <paramref name="shortcuts"/> says <see cref="ShortcutMode.Command"/>;
    /// it may give null, when the path of the program cannot be written as a command.
    /// </param>
    public static PlatformFeatures Linux(Func<ShortcutMode> shortcuts, Func<string?>? saveCommand = null) => new()
    {
        NotificationLook = false,
        ConfirmSound = false,
        StartupLabel = "Start with the session",
        TrashName = "Trash",
        ReservedNameText = "This name is reserved",
        Shortcuts = shortcuts,
        SaveCommand = saveCommand ?? (() => DefaultSaveCommand),
    };

    /// <summary>
    /// macOS, as far as the Mac app goes today: its pages, its folders and the MP3. What it does not do yet is off, never
    /// shown as working: no capture of one application, no notification of its own (so no look and no test of it), no
    /// confirmation sound, and the shortcut is a command to bind (<c>hindsound --save</c> inside the bundle), as on a Linux
    /// desktop without a portal, until the Carbon shortcuts come. The words are those of macOS.
    /// </summary>
    /// <param name="saveCommand">The command that saves a clip, or null when the path of the program cannot be written as one.</param>
    public static PlatformFeatures Mac(Func<string?> saveCommand) => new()
    {
        SingleApp = false,
        NotificationLook = false,
        TestNotification = false,
        ConfirmSound = false,
        StartupLabel = "Open at login",
        TrashName = "Trash",
        ReservedNameText = "This name is reserved",
        Shortcuts = static () => ShortcutMode.Command,
        SaveCommand = saveCommand,
    };

    /// <summary>The choice of one application to capture, in place of the whole PC sound.</summary>
    public bool SingleApp { get; init; } = true;

    /// <summary>
    /// The look of the drawn notification (its preview and the edge it sticks to). A system whose notification is the desktop's
    /// own has neither.
    /// </summary>
    public bool NotificationLook { get; init; } = true;

    /// <summary>The button that shows a test notification. A system whose notification is the desktop's own may keep it: it shows that one.</summary>
    public bool TestNotification { get; init; } = true;

    /// <summary>The choice of when the confirmation sound plays. A system that plays nothing has no choice to make.</summary>
    public bool ConfirmSound { get; init; } = true;

    /// <summary>The words of the toggle that starts the app with the user's session.</summary>
    public string StartupLabel { get; init; } = "Start with Windows";

    /// <summary>What the system calls the place deleted files go to, in the sentences that say a clip goes there.</summary>
    public string TrashName { get; init; } = "Recycle Bin";

    /// <summary>Why a name is refused when it is one the app keeps for itself, with no full stop.</summary>
    public string ReservedNameText { get; init; } = "Windows keeps this name for itself";

    /// <summary>How the shortcuts are set. A function: a system may only know after the window is open.</summary>
    public Func<ShortcutMode> Shortcuts { get; init; } = static () => ShortcutMode.Capture;

    /// <summary>
    /// The command to bind to a key, in <see cref="ShortcutMode.Command"/>. Null when no command can be written for this copy:
    /// the pages then say in words what to bind, with nothing to copy.
    /// </summary>
    public Func<string?> SaveCommand { get; init; } = static () => DefaultSaveCommand;

    /// <summary>A sentence written with the Windows words, in the words of this system: the Recycle Bin is its own name for the place.</summary>
    public string Words(string windowsText) =>
        TrashName == "Recycle Bin" ? windowsText : windowsText.Replace("Recycle Bin", TrashName, StringComparison.Ordinal);
}
