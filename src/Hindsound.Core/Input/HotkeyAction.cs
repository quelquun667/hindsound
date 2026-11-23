namespace Hindsound.Input;

/// <summary>Every action that can have a global shortcut.</summary>
public enum HotkeyAction
{
    SaveDefault,
    Save15s,
    Save1m,
    Save5m,
    TogglePause,
    CopyLast,

    /// <summary>Saves like <see cref="SaveDefault"/>, then opens the editor on the clip. Last, so that the numbers of the others never move.</summary>
    SaveAndEdit,
}

public enum HotkeyStatus
{
    /// <summary>The shortcut is registered with Windows.</summary>
    Registered,

    /// <summary>No shortcut is configured for this action.</summary>
    NotSet,

    /// <summary>Windows refused it: another app holds this combination.</summary>
    InUse,

    /// <summary>The text is not a valid shortcut.</summary>
    Invalid,

    /// <summary>Registration is paused (shortcut being typed in the settings).</summary>
    Suspended,

    /// <summary>Another action of the same settings asks for the same combination.</summary>
    Duplicate,

    /// <summary>The hotkey thread did not answer in time. Nothing was registered; trying again may work.</summary>
    Unavailable,
}

/// <summary>Outcome of registering one action's shortcut. <see cref="Message"/> is the text for the interface.</summary>
public sealed record HotkeyResult(HotkeyAction Action, string? Gesture, HotkeyStatus Status, string? Message)
{
    public static string InUseMessage => Tr.Of("Hotkey already in use by another app", "Raccourci déjà utilisé par une autre application");
    public static string InvalidMessage => Tr.Of("Invalid hotkey", "Raccourci invalide");
    public static string DuplicateMessage => Tr.Of("Same hotkey as another action", "Même raccourci qu'une autre action");
    public static string UnavailableMessage => Tr.Of("Hotkeys did not respond in time; try again", "Les raccourcis n'ont pas répondu à temps\u00A0; réessaie");

    /// <summary>
    /// The words for a refusal, in the language of the moment. What a screen keeps is the status, not this text, so that a
    /// message already on screen follows a change of language.
    /// </summary>
    public static string? MessageOf(HotkeyStatus status) => status switch
    {
        HotkeyStatus.InUse => InUseMessage,
        HotkeyStatus.Invalid => InvalidMessage,
        HotkeyStatus.Duplicate => DuplicateMessage,
        HotkeyStatus.Unavailable => UnavailableMessage,
        _ => null,
    };

    public bool Ok => Status is HotkeyStatus.Registered or HotkeyStatus.NotSet or HotkeyStatus.Suspended;

    /// <summary>
    /// The journal line. In a <c>--no-hotkeys</c> run the layer says yes without registering anything, so a bare
    /// "Registered" would read as a shortcut Windows really holds.
    /// </summary>
    public string JournalLine(bool nothingRegistered) =>
        $"hotkey {Action} {Gesture}: {Status}"
        + (nothingRegistered && Status == HotkeyStatus.Registered ? " (accepted, nothing registered with Windows in this run)" : "");
}
