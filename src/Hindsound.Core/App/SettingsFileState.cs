using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>Something about the settings file the user has to be told.</summary>
public enum SettingsNotice
{
    None,
    Damaged,
    Unreadable,
    HasError,
    NotSaved,
    StartupNotChanged,
}

/// <summary>What the start does about what it found in the file.</summary>
/// <param name="MakeSafe">The real settings are unknown: the defaults, without the microphone, see <see cref="SettingsFileState.MakeSafe"/>.</param>
/// <param name="TouchRunKey">The settings are known, so the Windows startup entry may follow them.</param>
/// <param name="Write">Write the settings now.</param>
/// <param name="FirstRun">There was no file: open the first-launch card.</param>
/// <param name="Notice">What to tell the user.</param>
internal readonly record struct StartDecision(bool MakeSafe, bool TouchRunKey, bool Write, bool FirstRun, SettingsNotice Notice);

/// <summary>What a later read of the file changes.</summary>
/// <param name="Apply">The file was read: its settings replace the current ones.</param>
/// <param name="Notice">What to tell the user.</param>
/// <param name="JournalLine">What to write in the log, if anything.</param>
internal readonly record struct ReloadDecision(bool Apply, SettingsNotice Notice, string? JournalLine);

/// <summary>How the next write of the settings must go.</summary>
public enum WriteStep
{
    Write,

    /// <summary>The file in place is a broken one, which only exists on disk: copy it to a .bak first.</summary>
    KeepBrokenFirst,

    /// <summary>The file has not been read yet, so its content is not known: writing would destroy it.</summary>
    Refuse,
}

/// <summary>Makes the Windows startup entry follow the setting, or the setting follow the entry when Windows refuses.</summary>
internal static class StartupSwitch
{
    /// <summary>A change from the window: the entry first, so that when Windows refuses, the file gets what the entry really is.</summary>
    public static void ApplyThenSave(bool startupChanged, Action applyEntry, Action save)
    {
        if (startupChanged)
        {
            applyEntry();
        }

        save();
    }

    /// <summary>False if the entry could not change: the setting then says what the entry really is. Faked key in tests.</summary>
    /// <param name="general">The settings, changed in place on a refusal.</param>
    /// <param name="key">The Run key.</param>
    /// <param name="exePath">The exe, by default this one.</param>
    public static bool Apply(GeneralSettings general, IRunKey key, string? exePath = null)
    {
        var wanted = general.StartWithWindows;
        var registration = new StartupRegistration(key, exePath);
        try
        {
            // A setting that is off and was never on needs no write.
            if ((!wanted && key.GetValue(StartupRegistration.DefaultValueName) is null) || registration.Apply(wanted))
            {
                return true;
            }

            general.StartWithWindows = registration.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Not even readable: nothing changed, so it is still the opposite of what was asked.
            general.StartWithWindows = !wanted;
        }

        return false;
    }
}

/// <summary>
/// A file the start could not read gets read as soon as it can be, without waiting for the user: a shortcut of the file is
/// not registered yet, so its press would never come. The pace drops, it never stops, and it ends with the first real read:
/// a lock that falls on a broken file changes nothing, as the defaults are still running with the microphone off.
/// </summary>
/// <param name="stillToRead">The app still waits for the settings of the file.</param>
/// <param name="nowMs">The time.</param>
/// <param name="after">Runs an action later, on the loop's thread.</param>
/// <param name="read">Reads the file. Never throws.</param>
internal sealed class SettingsRetry(Func<bool> stillToRead, Func<long> nowMs, Action<int, Action> after, Action read)
{
    private readonly NoticeRetry _pace = new();

    /// <summary>The look the app keeps on its own file.</summary>
    public static SettingsRetry Watching(SettingsFileState file, Func<long> nowMs, Action<int, Action> after, Action read) =>
        new(() => file.StillOnDefaults, nowMs, after, read);

    public void Arm()
    {
        if (!stillToRead() || _pace.Arm(nowMs()) is not { } delay)
        {
            return;
        }

        after(delay, () =>
        {
            _pace.Fired();
            read();
            Arm();
        });
    }
}

/// <summary>
/// What the app knows about the settings file between two looks at it: an app that records never turns
/// back on a microphone that may have been turned off. Decisions only, no file and no window: tested without either.
/// </summary>
internal sealed class SettingsFileState
{
    private bool _saysUnreadable;
    private bool _saveFailing;

    /// <summary>The date of the file as last read or written. Stays at its default until the file has been read.</summary>
    public DateTime Noted { get; private set; }

    /// <summary>The start could not read the file: nothing is written until it could.</summary>
    public bool AwaitingRead { get; private set; }

    /// <summary>
    /// The start could not read the file, so the defaults run with the microphone off, and nothing has replaced them yet.
    /// Longer than <see cref="AwaitingRead"/>: a file found broken or gone can be looked at, but it gave no settings.
    /// </summary>
    public bool StillOnDefaults { get; private set; }

    /// <summary>The file in place is broken, and no copy of it exists yet.</summary>
    public bool BrokenOnDisk { get; private set; }

    /// <summary>The unknown settings of a damaged or unreadable file: defaults, no microphone, no updates, and the startup entry as it is.</summary>
    /// <param name="settings">The defaults, changed in place.</param>
    /// <param name="runKeyEnabled">What the Run key says; null if it was not read (a test run with its own settings file).</param>
    public static void MakeSafe(AppSettings settings, bool? runKeyEnabled)
    {
        settings.Audio.MicDeviceId = AudioSettings.DisabledMic;

        // The user may have turned updates off, and nothing says so any more: like the microphone, the doubt turns them off.
        settings.General.Updates = new UpdateSettings { Mode = UpdateSettings.Off };
        if (runKeyEnabled is { } enabled)
        {
            settings.General.StartWithWindows = enabled;
        }
    }

    /// <summary>The words of a notice; null for none.</summary>
    /// <param name="notice">The notice.</param>
    /// <param name="copyKept">A damaged file has a copy on disk.</param>
    public static NotificationContent? ContentOf(SettingsNotice notice, bool copyKept) => notice switch
    {
        SettingsNotice.Damaged => NotificationStates.SettingsDamaged(copyKept),
        SettingsNotice.Unreadable => NotificationStates.SettingsUnreadable(),
        SettingsNotice.HasError => NotificationStates.SettingsHasError(),
        SettingsNotice.NotSaved => NotificationStates.SettingsNotSaved(),
        SettingsNotice.StartupNotChanged => NotificationStates.StartupNotChanged(),
        _ => null,
    };

    /// <summary>The first look at the file, when the app starts: the settings to run on, and what else the start has to do.</summary>
    /// <param name="store">The file.</param>
    /// <param name="tries">How many reads a locked file gets.</param>
    /// <param name="wait">What happens between two of them.</param>
    /// <param name="readRunKey">What the Run key says, if it may be read. Only asked when the real settings are unknown.</param>
    public (AppSettings Settings, StartDecision Start) Load(SettingsStore store, int tries, Action wait, Func<bool?> readRunKey)
    {
        var settings = store.LoadPatiently(tries, wait);
        var start = Started(store.LastStatus, store.LastBackupPath);
        if (start.MakeSafe)
        {
            MakeSafe(settings, readRunKey());
        }

        return (settings, start);
    }

    /// <summary>The first read of the file, by <see cref="SettingsStore.LoadPatiently"/>.</summary>
    /// <param name="status">What it found.</param>
    /// <param name="backupPath">Where a damaged file was moved; null if it could not be, so it is still in its place.</param>
    public StartDecision Started(SettingsLoadStatus status, string? backupPath)
    {
        switch (status)
        {
            case SettingsLoadStatus.Missing:
                return new StartDecision(MakeSafe: false, TouchRunKey: true, Write: true, FirstRun: true, SettingsNotice.None);

            case SettingsLoadStatus.Corrupt:
                BrokenOnDisk = backupPath is null;
                return new StartDecision(MakeSafe: true, TouchRunKey: false, Write: true, FirstRun: false, SettingsNotice.Damaged);

            case SettingsLoadStatus.Unreadable:
                AwaitingRead = true;
                StillOnDefaults = true;
                _saysUnreadable = true;
                return new StartDecision(MakeSafe: true, TouchRunKey: false, Write: false, FirstRun: false, SettingsNotice.Unreadable);

            default:
                return new StartDecision(MakeSafe: false, TouchRunKey: true, Write: false, FirstRun: false, SettingsNotice.None);
        }
    }

    /// <summary>Takes the date of the file as known. Not while the file is still to be read: the next press must try again.</summary>
    public void Note(DateTime written)
    {
        if (!AwaitingRead)
        {
            Noted = written;
        }
    }

    /// <summary>Is there anything new to read? A file still to be read always counts.</summary>
    public bool ShouldRead(DateTime written) => AwaitingRead || written != Noted;

    /// <summary>A later read of the file, which moved nothing on disk. The current settings stay unless it says to apply.</summary>
    /// <param name="status">What it found.</param>
    /// <param name="written">The date of the file before the read.</param>
    public ReloadDecision Reread(SettingsLoadStatus status, DateTime written)
    {
        if (status == SettingsLoadStatus.Unreadable)
        {
            // The date is not noted, so the next press tries again. One line for a whole run of failures.
            var line = _saysUnreadable ? null : "the settings file can't be read: keeping the current settings, trying again later";
            _saysUnreadable = true;
            return new ReloadDecision(false, SettingsNotice.None, line);
        }

        _saysUnreadable = false;
        AwaitingRead = false;
        Noted = written;
        BrokenOnDisk = status == SettingsLoadStatus.Corrupt;
        StillOnDefaults &= status != SettingsLoadStatus.Loaded;
        return status switch
        {
            SettingsLoadStatus.Loaded => new ReloadDecision(true, SettingsNotice.None, "settings read again (Loaded)"),

            // Its date is noted now, so this version of the file says it once.
            SettingsLoadStatus.Corrupt => new ReloadDecision(false, SettingsNotice.HasError, "the settings file has an error: keeping the current settings, the file is left as it is"),
            _ => new ReloadDecision(false, SettingsNotice.None, "the settings file is gone: keeping the current settings"),
        };
    }

    /// <summary>The notices still waiting in line that a later read of the file makes false. A missing file still leaves the defaults running, with the microphone off.</summary>
    public static NotificationKind[] CeasedBy(SettingsLoadStatus status) => status switch
    {
        SettingsLoadStatus.Loaded => [NotificationKind.SettingsUnreadable, NotificationKind.SettingsHasError],
        SettingsLoadStatus.Missing => [NotificationKind.SettingsHasError],
        _ => [],
    };

    /// <summary>The file has a date the app never read, and the app is about to write over it.</summary>
    public bool ChangedUnread(DateTime written) => !AwaitingRead && written != Noted;

    /// <summary>A look at the file just before writing over a change that was never read.</summary>
    public void SawBeforeWriting(SettingsLoadStatus status)
    {
        if (status != SettingsLoadStatus.Unreadable)
        {
            BrokenOnDisk = status == SettingsLoadStatus.Corrupt;
        }
    }

    public WriteStep NextWrite()
    {
        if (AwaitingRead)
        {
            return WriteStep.Refuse;
        }

        return BrokenOnDisk ? WriteStep.KeepBrokenFirst : WriteStep.Write;
    }

    /// <summary>A write of the settings, if it may happen at all. What it returns is to be told to the user.</summary>
    /// <param name="keepBrokenFile">Copies the broken file in place. False if no copy could be made.</param>
    /// <param name="save">Writes the settings. False if that failed.</param>
    /// <param name="fileTime">The date of the file now.</param>
    /// <param name="journal">Where it says why nothing was written.</param>
    public SettingsNotice TrySave(Func<bool> keepBrokenFile, Func<bool> save, Func<DateTime> fileTime, Action<string> journal)
    {
        var saved = false;
        switch (NextWrite())
        {
            case WriteStep.Refuse:
                journal("settings not written: the file could not be read yet");
                break;
            case WriteStep.KeepBrokenFirst when !keepBrokenFile():
                journal("settings not written: no copy of the broken file could be made");
                break;
            default:
                saved = save();
                break;
        }

        return WriteEnded(saved, fileTime());
    }

    /// <summary>The copy of the broken file exists: it is not made a second time, whatever becomes of the write.</summary>
    public void BrokenKept() => BrokenOnDisk = false;

    /// <summary>A write was tried. A failure leaves the date as it was, and says so once for a whole run of failures.</summary>
    /// <param name="saved">The file now holds the settings.</param>
    /// <param name="written">The date of the file now.</param>
    public SettingsNotice WriteEnded(bool saved, DateTime written)
    {
        if (saved)
        {
            Noted = written;
            _saveFailing = false;
            StillOnDefaults = false;
            return SettingsNotice.None;
        }

        var first = !_saveFailing;
        _saveFailing = true;
        return first ? SettingsNotice.NotSaved : SettingsNotice.None;
    }
}
