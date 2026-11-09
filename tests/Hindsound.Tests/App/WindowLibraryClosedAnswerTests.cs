using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// An action that cannot even be asked answers through the loop, not through a look. If the window closes before that answer
/// passes, it must not reach the screen of a window that has gone: the check on the panel is the only thing that stops it.
/// </summary>
public sealed class WindowLibraryClosedAnswerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly WindowLibrary _panel;
    private bool _foldersThrow;
    private bool _postThrowsOnce;

    public WindowLibraryClosedAnswerTests()
    {
        Directory.CreateDirectory(Clips);
        File.WriteAllBytes(Path.Combine(Clips, "dogs.mp3"), [1, 2, 3, 4, 5]);
        _panel = new WindowLibrary(
            _rig.Screen,
            new LibraryStore(_temp.Combine("settings.json"), _ => { }),
            () =>
            {
                if (_foldersThrow)
                {
                    // Once: the rows ask where the rescue folder is at each paint.
                    _foldersThrow = false;
                    throw new IOException("a disk that does not answer");
                }

                return new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") };
            },
            work =>
            {
                if (_postThrowsOnce)
                {
                    _postThrowsOnce = false;
                    throw new IOException("a loop that does not take the reading");
                }

                _loop.Enqueue(work);
            },
            new NoShell(),
            File.Exists,
            _ => true,
            (attempt, done) => done(attempt()),
            _ => { });
        _rig.Screen.SetLibrary(_panel);
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);
        Settle();
    }

    public void Dispose()
    {
        _panel.Close();
        _rig.Dispose();
        _temp.Dispose();
    }

    private sealed class NoShell : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }

    private string Clips => _temp.Combine("clips");

    private SettingsScreen Screen => _rig.Screen;

    private void Settle()
    {
        var end = Environment.TickCount64 + 20_000;
        while (_panel.IsWorking || !_loop.IsEmpty)
        {
            if (_loop.TryDequeue(out var work))
            {
                work();
            }
            else if (Environment.TickCount64 > end)
            {
                Assert.Fail("the loop never got there");
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void DrainWhatIsPosted()
    {
        while (_loop.TryDequeue(out var work))
        {
            work();
        }
    }

    private string Painted()
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Canvas.Strokes.Clear();
        Screen.Paint(_rig.Canvas);
        return string.Join("\n", _rig.Canvas.Drawn);
    }

    [Fact]
    public void A_box_answer_that_passes_after_the_close_says_nothing_on_the_screen()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _foldersThrow = true;
        _panel.CreateCollection("pets");
        _panel.Close();

        DrainWhatIsPosted();

        Assert.DoesNotContain(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_box_answer_that_passes_before_the_close_is_said_as_the_control()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _foldersThrow = true;
        _panel.CreateCollection("pets");

        DrainWhatIsPosted();

        Assert.Contains(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_action_whose_folders_cannot_be_read_waits_for_nothing_after_its_answer()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _foldersThrow = true;
        _panel.CreateCollection("pets");
        DrainWhatIsPosted();

        Assert.Equal(0, _panel.ActionsWaiting);
    }

    // A star has no sentence of its own: all that reaches the screen is the new reading, which puts out the sentence on the line.
    private string SaidOnTheLine()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _foldersThrow = true;
        _panel.CreateCollection("pets");
        DrainWhatIsPosted();
        var said = Painted();
        Assert.Contains(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, said, StringComparison.Ordinal);
        return said;
    }

    [Fact]
    public void A_star_answer_that_passes_after_the_close_brings_no_reading_to_the_screen()
    {
        SaidOnTheLine();
        var line = _panel.Lines!.Lines.Single();
        _foldersThrow = true;
        _panel.SetFavorite(line, true);
        _panel.Close();

        DrainWhatIsPosted();

        Assert.Contains(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_star_answer_that_passes_before_the_close_brings_the_reading_as_the_control()
    {
        SaidOnTheLine();
        var line = _panel.Lines!.Lines.Single();
        _foldersThrow = true;
        _panel.SetFavorite(line, true);

        DrainWhatIsPosted();

        Assert.DoesNotContain(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_action_written_whose_reading_could_not_be_delivered_is_not_said_to_have_failed()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _postThrowsOnce = true;

        _panel.CreateCollection("pets");

        // Only the answer first: the look it asks for is left on the loop, since its reading would put out any sentence.
        Action? answer;
        var end = Environment.TickCount64 + 20_000;
        while (!_loop.TryDequeue(out answer))
        {
            Assert.True(Environment.TickCount64 < end, "the answer never came");
            Thread.Sleep(1);
        }

        answer();
        Assert.DoesNotContain(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
        Settle();

        Assert.Contains("pets", File.ReadAllText(_temp.Combine("library.json")), StringComparison.Ordinal);
        Assert.Equal(["pets"], _panel.Index!.Collections.Select(collection => collection.Name));
    }

    [Fact]
    public void A_star_asked_stays_lit_through_the_reading_of_an_action_before_it()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        Screen.TabTo(SettingsScreen.ClipListId);

        // On, then off again: the first is written and read while the second is still on its way.
        _rig.Press(RowList.StarKey);
        _rig.Press(RowList.StarKey);

        Action? firstReading;
        var end = Environment.TickCount64 + 20_000;
        while (!_loop.TryDequeue(out firstReading))
        {
            Assert.True(Environment.TickCount64 < end, "the reading never came");
            Thread.Sleep(1);
        }

        firstReading();
        Painted();

        // That reading has the star on, since the second action is not written yet. The star asked for last shows, empty.
        Assert.Equal(1, _rig.Canvas.Strokes.Count(s => s.Rgb == 0x8E95A3) / CanvasShapes.StarCorners);

        Settle();
        Painted();
        Assert.Equal(1, _rig.Canvas.Strokes.Count(s => s.Rgb == 0x8E95A3) / CanvasShapes.StarCorners);
    }

    [Fact]
    public void A_star_asked_stays_lit_through_a_look_made_before_it_was_written()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        Screen.TabTo(SettingsScreen.ClipListId);

        // A look over, its reading still on the loop, when the star is asked for.
        _panel.ReadAgain();
        var end = Environment.TickCount64 + 20_000;
        while (_panel.IsWorking)
        {
            Assert.True(Environment.TickCount64 < end, "the look never ended");
            Thread.Sleep(1);
        }

        _rig.Press(RowList.StarKey);
        Assert.True(_loop.TryDequeue(out var oldReading));
        oldReading();
        Painted();

        // That reading has no star on the clip yet: the one asked for shows until the reading of its own action.
        Assert.Equal(0, _rig.Canvas.Strokes.Count(s => s.Rgb == 0x8E95A3) / CanvasShapes.StarCorners);

        Settle();
        Painted();
        Assert.Equal(0, _rig.Canvas.Strokes.Count(s => s.Rgb == 0x8E95A3) / CanvasShapes.StarCorners);
        Assert.True(_panel.LabelsOf(_panel.Lines!.Lines.Single()).Favorite);
    }

    [Fact]
    public void The_sentence_of_an_action_written_stays_through_the_look_asked_to_show_its_result()
    {
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        var line = _panel.Lines!.Lines.Single();
        _postThrowsOnce = true;

        // A tick on a collection the file does not have: the clip stays on the list, and the answer has a sentence.
        _panel.SetIn(line, "no-such-collection", true);

        Action? answer;
        var end = Environment.TickCount64 + 20_000;
        while (!_loop.TryDequeue(out answer))
        {
            Assert.True(Environment.TickCount64 < end, "the answer never came");
            Thread.Sleep(1);
        }

        answer();
        var sentence = ChipLine.OutcomeMessage(LibraryOutcome.UnknownCollection)!;
        Assert.Contains(sentence, Painted(), StringComparison.Ordinal);

        // The look that answer asked for brings the file as it is: the sentence is read by now, and stays until the next one.
        Settle();
        Assert.Contains(sentence, Painted(), StringComparison.Ordinal);

        _panel.ReadAgain();
        Settle();
        Assert.DoesNotContain(sentence, Painted(), StringComparison.Ordinal);
    }
}
