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
    public const string InUseMessage = "Hotkey already in use by another app";
    public const string InvalidMessage = "Invalid hotkey";
    public const string DuplicateMessage = "Same hotkey as another action";
    public const string UnavailableMessage = "Hotkeys did not respond in time; try again";

    public bool Ok => Status is HotkeyStatus.Registered or HotkeyStatus.NotSet or HotkeyStatus.Suspended;
}
