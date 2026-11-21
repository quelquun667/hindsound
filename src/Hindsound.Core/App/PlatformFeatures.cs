using Hindsound.Input;

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
    /// macOS, as far as the Mac app goes today: its pages, its folders, the MP3 and the capture, of one application too (a
    /// Core Audio tap of its processes and their children). What it does not do yet is off, never shown as working: no
    /// confirmation sound. The notification is drawn by the app, as
    /// on Windows, with its look and its test. The shortcut is a command to bind (<c>hindsound --save</c> inside the bundle),
    /// as on a Linux desktop without a portal, unless <paramref name="shortcuts"/> says the Carbon shortcuts are there. The
    /// words are those of macOS.
    /// </summary>
    /// <param name="saveCommand">The command that saves a clip, or null when the path of the program cannot be written as one.</param>
    /// <param name="shortcuts">How the shortcuts are set now; <see cref="ShortcutMode.Command"/> when not given.</param>
    public static PlatformFeatures Mac(Func<string?> saveCommand, Func<ShortcutMode>? shortcuts = null) => new()
    {
        SingleApp = true,
        NotificationLook = true,
        TestNotification = true,
        ConfirmSound = false,
        StartupLabel = "Open at login",
        TrashName = "Trash",
        ReservedNameText = "This name is reserved",
        Shortcuts = shortcuts ?? (static () => ShortcutMode.Command),
        SaveCommand = saveCommand,
        ModifierNames = ["Control", "Option", "Shift", "Cmd"],
        PasteGesture = "Win+V",
        PasteKeys = ["Cmd", "V"],
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

    /// <summary>
    /// What this system calls the modifier keys of a gesture, in the order of <see cref="HotkeyGesture.ToString"/>: Ctrl, Alt,
    /// Shift, Win. On a Mac, the key that holds the Win flag is Command, as Carbon and SDL read it.
    /// </summary>
    public IReadOnlyList<string> ModifierNames { get; init; } = WindowsModifierNames;

    /// <summary>The keys that paste, as a gesture in the app's own text (its modifier read through <see cref="KeyText"/>).</summary>
    public string PasteGesture { get; init; } = "Ctrl+V";

    /// <summary>The keys that paste, in this system's words, as the notification draws them.</summary>
    public IReadOnlyList<string> PasteKeys { get; init; } = WindowsPasteKeys;

    private static readonly string[] WindowsModifierNames = ["Ctrl", "Alt", "Shift", "Win"];
    private static readonly string[] WindowsPasteKeys = ["Ctrl", "V"];

    /// <summary>
    /// A gesture in the app's own text (<c>Ctrl+Shift+F10</c>, as the settings keep it) in this system's words, for the eye only:
    /// each modifier renamed, the key kept. Windows and Linux get the text back as it is.
    /// </summary>
    public string KeyText(string gesture)
    {
        if (ReferenceEquals(ModifierNames, WindowsModifierNames))
        {
            return gesture;
        }

        var parts = gesture.Split('+');
        for (var i = 0; i < parts.Length; i++)
        {
            var at = Array.IndexOf(WindowsModifierNames, parts[i]);
            if (at >= 0 && at < ModifierNames.Count)
            {
                parts[i] = ModifierNames[at];
            }
        }

        return string.Join('+', parts);
    }

    /// <summary>A sentence written with the Windows words, in the words of this system: the Recycle Bin is its own name for the place.</summary>
    public string Words(string windowsText) =>
        TrashName == "Recycle Bin" ? windowsText : windowsText.Replace("Recycle Bin", TrashName, StringComparison.Ordinal);
}
