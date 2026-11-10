using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>"Using defaults, mic off" on screen when the file gets read: an answer pushes it aside, and it must not come back saying the mic is off.</summary>
public class NoticeWithdrawOnScreenTests
{
    private static readonly NotificationContent Unreadable = NotificationStates.SettingsUnreadable();
    private static readonly NotificationContent HasError = NotificationStates.SettingsHasError();
    private static readonly NotificationContent Answer = NotificationStates.NothingToSave();

    private readonly NoticeQueue _queue = new();

    private void AnswerComesAndGoes()
    {
        Assert.True(_queue.ShowsNow(Answer));
        _queue.Shown(Answer);
        _queue.Finished();
    }

    [Fact]
    public void A_notice_on_screen_that_stopped_being_true_does_not_come_back_after_an_answer()
    {
        _queue.Shown(Unreadable);

        _queue.Withdraw(SettingsFileState.CeasedBy(SettingsLoadStatus.Loaded));
        AnswerComesAndGoes();

        Assert.Null(_queue.Next());
    }

    [Fact]
    public void A_notice_on_screen_that_is_still_true_comes_back_after_an_answer()
    {
        _queue.Shown(Unreadable);

        _queue.Withdraw(SettingsFileState.CeasedBy(SettingsLoadStatus.Missing));
        AnswerComesAndGoes();

        Assert.Same(Unreadable, _queue.Next());
    }

    [Fact]
    public void Withdrawing_also_takes_it_out_of_the_line()
    {
        _queue.Add(HasError);
        _queue.Shown(Unreadable);

        _queue.Withdraw(SettingsFileState.CeasedBy(SettingsLoadStatus.Loaded));
        AnswerComesAndGoes();

        Assert.Null(_queue.Next());
    }

    [Fact]
    public void What_is_shown_next_starts_afresh()
    {
        _queue.Shown(Unreadable);
        _queue.Withdraw(NotificationKind.SettingsUnreadable);
        _queue.Finished();

        _queue.Shown(HasError);
        AnswerComesAndGoes();

        Assert.Same(HasError, _queue.Next());
    }

    [Fact]
    public void With_nothing_on_screen_a_withdrawal_marks_nothing()
    {
        _queue.Withdraw(NotificationKind.SettingsHasError);

        _queue.Shown(HasError);
        AnswerComesAndGoes();

        Assert.Same(HasError, _queue.Next());
    }
}
