using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The answers that close a box or change nothing on the line must still be said somewhere: the line reads the file, and after a
/// failed write the file is as it was, so its status cannot tell what happened.
/// </summary>
public sealed class OutcomeMessageTests
{
    [Theory]
    [InlineData(LibraryOutcome.NotWritten, "Couldn't save that. Nothing changed.")]
    [InlineData(LibraryOutcome.ReadOnly, "Collections can't be changed right now. Your clips are safe.")]
    [InlineData(LibraryOutcome.UnknownCollection, "That collection no longer exists.")]
    public void An_answer_that_leaves_the_box_with_nothing_changed_has_its_sentence(LibraryOutcome outcome, string text)
    {
        Assert.Equal(text, ChipLine.OutcomeMessage(outcome));
    }

    [Fact]
    public void Each_of_the_three_says_something_else()
    {
        var texts = new[] { LibraryOutcome.NotWritten, LibraryOutcome.ReadOnly, LibraryOutcome.UnknownCollection }.Select(ChipLine.OutcomeMessage).ToList();

        Assert.Equal(3, texts.Distinct().Count());
        Assert.All(texts, Assert.NotNull);
    }

    [Fact]
    public void Every_other_answer_has_nothing_to_say_here()
    {
        var said = new[]
        {
            LibraryOutcome.NotWritten, LibraryOutcome.ReadOnly, LibraryOutcome.UnknownCollection, LibraryOutcome.NameTaken, LibraryOutcome.NameTooLong,
            LibraryOutcome.NoFile, LibraryOutcome.ClipGone, LibraryOutcome.FolderNotRead,
        };

        foreach (var outcome in Enum.GetValues<LibraryOutcome>().Except(said))
        {
            Assert.Null(ChipLine.OutcomeMessage(outcome));
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_new_sentences_fit_the_width_of_the_line(double scale)
    {
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var texts = new[]
        {
            ChipLine.ReadOnlyMessage(LibraryStatus.TooNew)!,
            ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!,
            ChipLine.OutcomeMessage(LibraryOutcome.ReadOnly)!,
            ChipLine.OutcomeMessage(LibraryOutcome.UnknownCollection)!,
        };

        foreach (var text in texts)
        {
            Assert.Equal(text, canvas.Fit(text, Face.Body, 13, 728 - 200 - 18));
        }
    }

    [Fact]
    public void A_status_that_forbids_writing_always_has_its_sentence_and_one_that_allows_it_has_none()
    {
        foreach (var status in Enum.GetValues<LibraryStatus>())
        {
            var canWrite = new LibraryRead(status, new LibraryData()).CanWrite;

            Assert.Equal(canWrite, ChipLine.ReadOnlyMessage(status) is null);
        }
    }

    [Theory]
    [InlineData(LibraryOutcome.NotWritten)]
    [InlineData(LibraryOutcome.ReadOnly)]
    [InlineData(LibraryOutcome.UnknownCollection)]
    public void The_sentence_goes_on_the_amber_line(LibraryOutcome outcome)
    {
        var text = ChipLine.OutcomeMessage(outcome)!;
        var log = new ShapeLog();

        ChipLine.Notice(text, 200, 528).Paint(log, new UiState(0xD4F53C, null, null, null, -1, -1, 0, true, null));

        Assert.Equal(1, log.Warnings);
        Assert.Contains(text, log.Texts);
    }
}
