using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

public class StartupSwitchTests
{
    private const string Exe = @"C:\Apps\Hindsound.exe";
    private const string Command = "\"" + Exe + "\"";

    private sealed class FakeKey : IRunKey
    {
        public string? Value { get; set; }

        public bool RefusesWrites { get; init; }

        public bool RefusesReads { get; init; }

        public int Writes { get; private set; }

        public string? GetValue(string name) => RefusesReads ? throw new UnauthorizedAccessException() : Value;

        public void SetValue(string name, string value)
        {
            Writes++;
            if (RefusesWrites)
            {
                throw new UnauthorizedAccessException();
            }

            Value = value;
        }

        public void DeleteValue(string name)
        {
            Writes++;
            if (RefusesWrites)
            {
                throw new UnauthorizedAccessException();
            }

            Value = null;
        }
    }

    [Fact]
    public void On_writes_the_entry()
    {
        var key = new FakeKey();
        var general = new GeneralSettings { StartWithWindows = true };

        Assert.True(StartupSwitch.Apply(general, key, Exe));

        Assert.Equal(Command, key.Value);
        Assert.True(general.StartWithWindows);
    }

    [Fact]
    public void Off_removes_the_entry()
    {
        var key = new FakeKey { Value = Command };
        var general = new GeneralSettings { StartWithWindows = false };

        Assert.True(StartupSwitch.Apply(general, key, Exe));

        Assert.Null(key.Value);
    }

    [Fact]
    public void Off_and_never_on_writes_nothing()
    {
        var key = new FakeKey();

        Assert.True(StartupSwitch.Apply(new GeneralSettings { StartWithWindows = false }, key, Exe));

        Assert.Equal(0, key.Writes);
    }

    [Fact]
    public void A_refused_on_puts_the_switch_back_to_off()
    {
        var general = new GeneralSettings { StartWithWindows = true };

        Assert.False(StartupSwitch.Apply(general, new FakeKey { RefusesWrites = true }, Exe));

        Assert.False(general.StartWithWindows);
    }

    [Fact]
    public void A_refused_off_puts_the_switch_back_to_on()
    {
        var general = new GeneralSettings { StartWithWindows = false };

        Assert.False(StartupSwitch.Apply(general, new FakeKey { RefusesWrites = true, Value = Command }, Exe));

        Assert.True(general.StartWithWindows);
    }

    [Fact]
    public void A_key_that_cannot_even_be_read_counts_as_unchanged()
    {
        var general = new GeneralSettings { StartWithWindows = true };

        Assert.False(StartupSwitch.Apply(general, new FakeKey { RefusesReads = true }, Exe));

        Assert.False(general.StartWithWindows);
    }
}

public class SettingsFileStateTests
{
    private static readonly DateTime Monday = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

    private static SettingsFileState Running()
    {
        var file = new SettingsFileState();
        file.Started(SettingsLoadStatus.Loaded, null);
        file.Note(Monday);
        return file;
    }

    private static SettingsFileState StartedUnreadable()
    {
        var file = new SettingsFileState();
        file.Started(SettingsLoadStatus.Unreadable, null);
        return file;
    }

    // ---------------------------------------------------------------- the start

    [Fact]
    public void A_good_file_is_simply_used()
    {
        Assert.Equal(
            new StartDecision(MakeSafe: false, TouchRunKey: true, Write: false, FirstRun: false, SettingsNotice.None),
            new SettingsFileState().Started(SettingsLoadStatus.Loaded, null));
    }

    [Fact]
    public void No_file_is_a_first_run()
    {
        Assert.Equal(
            new StartDecision(MakeSafe: false, TouchRunKey: true, Write: true, FirstRun: true, SettingsNotice.None),
            new SettingsFileState().Started(SettingsLoadStatus.Missing, null));
    }

    [Fact]
    public void A_damaged_file_gives_safe_settings_written_to_a_new_file_and_says_so()
    {
        Assert.Equal(
            new StartDecision(MakeSafe: true, TouchRunKey: false, Write: true, FirstRun: false, SettingsNotice.Damaged),
            new SettingsFileState().Started(SettingsLoadStatus.Corrupt, "settings.json.bak"));
    }

    [Fact]
    public void An_unreadable_file_gives_safe_settings_and_writes_nothing()
    {
        Assert.Equal(
            new StartDecision(MakeSafe: true, TouchRunKey: false, Write: false, FirstRun: false, SettingsNotice.Unreadable),
            new SettingsFileState().Started(SettingsLoadStatus.Unreadable, null));
    }

    [Fact]
    public void Safe_settings_have_no_microphone_and_follow_the_run_key()
    {
        var settings = new AppSettings();

        SettingsFileState.MakeSafe(settings, runKeyEnabled: true);

        Assert.False(settings.Audio.MicEnabled);
        Assert.True(settings.General.StartWithWindows);
    }

    [Fact]
    public void Safe_settings_of_a_test_run_never_got_the_run_key()
    {
        var settings = new AppSettings();

        SettingsFileState.MakeSafe(settings, runKeyEnabled: null);

        Assert.False(settings.Audio.MicEnabled);
        Assert.False(settings.General.StartWithWindows);
    }

    [Fact]
    public void Unreadable_at_the_start_keeps_the_date_at_its_default_and_refuses_to_write()
    {
        var file = StartedUnreadable();

        file.Note(Monday);

        Assert.Equal(default, file.Noted);
        Assert.Equal(WriteStep.Refuse, file.NextWrite());
    }

    [Fact]
    public void Unreadable_at_the_start_tries_again_even_with_an_unchanged_date()
    {
        Assert.True(StartedUnreadable().ShouldRead(default));
    }

    [Fact]
    public void Once_read_after_the_start_it_applies_and_writing_is_allowed()
    {
        var file = StartedUnreadable();

        var decision = file.Reread(SettingsLoadStatus.Loaded, Monday);

        Assert.True(decision.Apply);
        Assert.Equal(WriteStep.Write, file.NextWrite());
        Assert.Equal(Monday, file.Noted);
        Assert.False(file.ShouldRead(Monday));
    }

    [Fact]
    public void A_damaged_file_that_could_not_be_moved_gets_a_copy_before_being_replaced()
    {
        var file = new SettingsFileState();

        file.Started(SettingsLoadStatus.Corrupt, backupPath: null);

        Assert.Equal(WriteStep.KeepBrokenFirst, file.NextWrite());
    }

    [Fact]
    public void A_damaged_file_that_was_moved_needs_no_copy()
    {
        var file = new SettingsFileState();

        file.Started(SettingsLoadStatus.Corrupt, "settings.json.bak");

        Assert.Equal(WriteStep.Write, file.NextWrite());
    }

    // ---------------------------------------------------------------- reading it again

    [Fact]
    public void Only_a_new_date_is_worth_reading()
    {
        var file = Running();

        Assert.False(file.ShouldRead(Monday));
        Assert.True(file.ShouldRead(Tuesday));
    }

    [Fact]
    public void Loaded_applies_and_notes_the_date()
    {
        var file = Running();

        var decision = file.Reread(SettingsLoadStatus.Loaded, Tuesday);

        Assert.True(decision.Apply);
        Assert.Equal(SettingsNotice.None, decision.Notice);
        Assert.NotNull(decision.JournalLine);
        Assert.False(file.ShouldRead(Tuesday));
    }

    [Fact]
    public void Missing_keeps_the_settings_notes_the_date_and_logs_a_line()
    {
        var file = Running();

        var decision = file.Reread(SettingsLoadStatus.Missing, Tuesday);

        Assert.False(decision.Apply);
        Assert.Equal(SettingsNotice.None, decision.Notice);
        Assert.NotNull(decision.JournalLine);
        Assert.False(file.ShouldRead(Tuesday));
    }

    [Fact]
    public void Corrupt_keeps_the_settings_says_so_and_that_version_is_not_read_again()
    {
        var file = Running();

        var decision = file.Reread(SettingsLoadStatus.Corrupt, Tuesday);

        Assert.False(decision.Apply);
        Assert.Equal(SettingsNotice.HasError, decision.Notice);
        Assert.NotNull(decision.JournalLine);
        Assert.False(file.ShouldRead(Tuesday));
    }

    [Fact]
    public void Corrupt_is_kept_as_a_bak_before_the_next_write_once()
    {
        var file = Running();
        file.Reread(SettingsLoadStatus.Corrupt, Tuesday);

        Assert.Equal(WriteStep.KeepBrokenFirst, file.NextWrite());
        file.BrokenKept();
        Assert.Equal(WriteStep.Write, file.NextWrite());
    }

    [Fact]
    public void A_new_version_of_a_broken_file_says_it_again()
    {
        var file = Running();
        file.Reread(SettingsLoadStatus.Corrupt, Monday);

        Assert.True(file.ShouldRead(Tuesday));
        Assert.Equal(SettingsNotice.HasError, file.Reread(SettingsLoadStatus.Corrupt, Tuesday).Notice);
    }

    [Fact]
    public void A_repaired_file_needs_no_bak()
    {
        var file = Running();
        file.Reread(SettingsLoadStatus.Corrupt, Monday);

        file.Reread(SettingsLoadStatus.Loaded, Tuesday);

        Assert.Equal(WriteStep.Write, file.NextWrite());
    }

    [Fact]
    public void Unreadable_keeps_the_settings_and_the_old_date_so_the_next_press_tries_again()
    {
        var file = Running();

        var decision = file.Reread(SettingsLoadStatus.Unreadable, Tuesday);

        Assert.False(decision.Apply);
        Assert.Equal(SettingsNotice.None, decision.Notice);
        Assert.Equal(Monday, file.Noted);
        Assert.True(file.ShouldRead(Tuesday));
    }

    [Fact]
    public void A_run_of_unreadable_reads_logs_one_line()
    {
        var file = Running();

        Assert.NotNull(file.Reread(SettingsLoadStatus.Unreadable, Tuesday).JournalLine);
        Assert.Null(file.Reread(SettingsLoadStatus.Unreadable, Tuesday).JournalLine);

        file.Reread(SettingsLoadStatus.Loaded, Tuesday);
        Assert.NotNull(file.Reread(SettingsLoadStatus.Unreadable, Tuesday).JournalLine);
    }

    [Fact]
    public void An_unreadable_start_that_stays_unreadable_logs_nothing_more()
    {
        Assert.Null(StartedUnreadable().Reread(SettingsLoadStatus.Unreadable, Monday).JournalLine);
    }

    [Fact]
    public void Still_unreadable_after_the_start_keeps_refusing_to_write()
    {
        var file = StartedUnreadable();

        file.Reread(SettingsLoadStatus.Unreadable, Monday);

        Assert.Equal(WriteStep.Refuse, file.NextWrite());
        Assert.True(file.ShouldRead(Monday));
    }

    [Fact]
    public void Broken_after_an_unreadable_start_may_be_replaced_once_copied()
    {
        var file = StartedUnreadable();

        var decision = file.Reread(SettingsLoadStatus.Corrupt, Monday);

        Assert.False(decision.Apply);
        Assert.Equal(SettingsNotice.HasError, decision.Notice);
        Assert.Equal(WriteStep.KeepBrokenFirst, file.NextWrite());
    }

    [Fact]
    public void Gone_after_an_unreadable_start_may_be_written_again()
    {
        var file = StartedUnreadable();

        file.Reread(SettingsLoadStatus.Missing, Monday);

        Assert.Equal(WriteStep.Write, file.NextWrite());
    }

    // ---------------------------------------------------------------- writing

    [Fact]
    public void A_successful_write_notes_the_date()
    {
        var file = Running();

        Assert.Equal(SettingsNotice.None, file.WriteEnded(true, Tuesday));

        Assert.Equal(Tuesday, file.Noted);
    }

    [Fact]
    public void A_failed_write_leaves_the_date_alone()
    {
        var file = Running();

        file.WriteEnded(false, Tuesday);

        Assert.Equal(Monday, file.Noted);
    }

    [Fact]
    public void A_run_of_failed_writes_says_it_once()
    {
        var file = Running();

        Assert.Equal(SettingsNotice.NotSaved, file.WriteEnded(false, Monday));
        Assert.Equal(SettingsNotice.None, file.WriteEnded(false, Monday));

        file.WriteEnded(true, Tuesday);
        Assert.Equal(SettingsNotice.NotSaved, file.WriteEnded(false, Tuesday));
    }

    [Fact]
    public void A_change_nobody_read_is_noticed_before_writing_over_it()
    {
        var file = Running();

        Assert.False(file.ChangedUnread(Monday));
        Assert.True(file.ChangedUnread(Tuesday));
    }

    [Fact]
    public void A_file_still_to_be_read_does_not_count_as_a_change_nobody_read()
    {
        Assert.False(StartedUnreadable().ChangedUnread(Monday));
    }

    [Theory]
    [InlineData(SettingsLoadStatus.Corrupt, WriteStep.KeepBrokenFirst)]
    [InlineData(SettingsLoadStatus.Loaded, WriteStep.Write)]
    [InlineData(SettingsLoadStatus.Missing, WriteStep.Write)]
    [InlineData(SettingsLoadStatus.Unreadable, WriteStep.Write)]
    public void A_look_just_before_writing_decides_about_the_copy(SettingsLoadStatus status, WriteStep expected)
    {
        var file = Running();

        file.SawBeforeWriting(status);

        Assert.Equal(expected, file.NextWrite());
    }

    [Fact]
    public void A_locked_file_stays_known_as_broken()
    {
        var file = Running();
        file.Reread(SettingsLoadStatus.Corrupt, Tuesday);

        file.SawBeforeWriting(SettingsLoadStatus.Unreadable);

        Assert.Equal(WriteStep.KeepBrokenFirst, file.NextWrite());
    }

    // ---------------------------------------------------------------- the words

    [Theory]
    [InlineData(SettingsNotice.Damaged, NotificationKind.SettingsDamaged)]
    [InlineData(SettingsNotice.Unreadable, NotificationKind.SettingsUnreadable)]
    [InlineData(SettingsNotice.HasError, NotificationKind.SettingsHasError)]
    [InlineData(SettingsNotice.NotSaved, NotificationKind.SettingsNotSaved)]
    [InlineData(SettingsNotice.StartupNotChanged, NotificationKind.StartupNotChanged)]
    public void Each_notice_has_its_notification(SettingsNotice notice, NotificationKind kind)
    {
        Assert.Equal(kind, SettingsFileState.ContentOf(notice, copyKept: true)!.Kind);
    }

    [Fact]
    public void No_notice_shows_nothing()
    {
        Assert.Null(SettingsFileState.ContentOf(SettingsNotice.None, copyKept: true));
    }

    [Fact]
    public void A_damaged_file_only_speaks_of_a_copy_that_exists()
    {
        Assert.Contains(".bak", SettingsFileState.ContentOf(SettingsNotice.Damaged, copyKept: true)!.Body);
        Assert.DoesNotContain(".bak", SettingsFileState.ContentOf(SettingsNotice.Damaged, copyKept: false)!.Body);
    }
}
