using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>"Mic off" waits for the desktop, the file gets read during the game: on the way back, it must not say that any more.</summary>
public class NoticeWithdrawTests
{
    private static readonly NotificationContent Unreadable = NotificationStates.SettingsUnreadable();
    private static readonly NotificationContent HasError = NotificationStates.SettingsHasError();
    private static readonly NotificationContent NotSaved = NotificationStates.SettingsNotSaved();
    private static readonly NotificationContent Answer = NotificationStates.NothingToSave();

    private static List<NotificationKind> Line(NoticeQueue queue)
    {
        var kinds = new List<NotificationKind>();
        while (queue.Next() is { } next)
        {
            kinds.Add(next.Kind);
            queue.Remove(next);
        }

        return kinds;
    }

    [Fact]
    public void A_file_read_at_last_takes_back_what_said_it_could_not_be_and_nothing_else()
    {
        var queue = new NoticeQueue();
        queue.Add(Unreadable);
        queue.Add(NotSaved);
        queue.Add(HasError);

        queue.Withdraw(SettingsFileState.CeasedBy(SettingsLoadStatus.Loaded));

        Assert.Equal([NotificationKind.SettingsNotSaved], Line(queue));
    }

    [Fact]
    public void A_missing_file_keeps_the_mic_off_news_and_takes_back_the_error_only()
    {
        var queue = new NoticeQueue();
        queue.Add(Unreadable);
        queue.Add(HasError);

        queue.Withdraw(SettingsFileState.CeasedBy(SettingsLoadStatus.Missing));

        Assert.Equal([NotificationKind.SettingsUnreadable], Line(queue));
    }

    [Theory]
    [InlineData(SettingsLoadStatus.Unreadable)]
    [InlineData(SettingsLoadStatus.Corrupt)]
    public void A_read_that_changes_nothing_takes_nothing_back(SettingsLoadStatus status)
    {
        var queue = new NoticeQueue();
        queue.Add(Unreadable);

        queue.Withdraw(SettingsFileState.CeasedBy(status));

        Assert.Equal([NotificationKind.SettingsUnreadable], Line(queue));
    }

    [Fact]
    public void A_notice_pushed_aside_by_an_answer_is_taken_back_too()
    {
        var queue = new NoticeQueue();
        queue.Shown(Unreadable);
        queue.ShowsNow(Answer);
        queue.Shown(Answer);

        queue.Withdraw(SettingsFileState.CeasedBy(SettingsLoadStatus.Loaded));
        queue.Finished();

        Assert.Empty(Line(queue));
    }
}
