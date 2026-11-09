using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// A click is answered later: the action waits in a queue and the screen only changes with the answer. A second click before it
/// must start from what the first one asked for, not from what the screen still shows.
/// </summary>
public sealed class PendingClicksTests
{
    private const float Left = 200, Right = 728;

    private static readonly ChipEntry[] Entries = [new("c1", "pets", false), new("c2", "Sam", false)];

    private static CollectionChips Line(ChipLineState state, List<(string Id, bool On)> set, IReadOnlyList<ChipEntry>? entries = null)
    {
        var canvas = new FakeCanvas();
        return new CollectionChips(canvas, state, state.Arrange("clip", entries ?? Entries), Left, ChipLine.ChipsLimit(canvas, Right), (id, on) => set.Add((id, on)), _ => { });
    }

    private static void Click(CollectionChips line, int stop)
    {
        var box = line.Fit.BoxOf(stop);
        line.OnPress(box.MidX, box.MidY);
        line.OnRelease(inside: true);
    }

    [Fact]
    public void Two_clicks_before_the_answer_tick_then_untick()
    {
        var state = new ChipLineState();
        var set = new List<(string Id, bool On)>();
        var line = Line(state, set);

        // The widgets are not made again between the two: the answer has not come.
        Click(line, 0);
        Click(line, 0);

        Assert.Equal([("c1", true), ("c1", false)], set);
    }

    [Fact]
    public void The_chip_shows_the_click_at_once()
    {
        var state = new ChipLineState();
        var set = new List<(string Id, bool On)>();
        var line = Line(state, set);
        Click(line, 0);

        var log = new ShapeLog();
        line.Paint(log, new UiState(0xD4F53C, null, null, null, -1, -1, 0, true, null));

        // A ticked chip has its border in the accent colour.
        Assert.Equal(0xD4F53Cu, log.Frames.Single(f => f.Box == line.Fit.Chips[0].Box).Border);
        Assert.Equal(Theme.Control, log.Frames.Single(f => f.Box == line.Fit.Chips[1].Box).Border);
    }

    [Fact]
    public void Once_answered_the_screen_speaks_again()
    {
        var state = new ChipLineState();
        var set = new List<(string Id, bool On)>();
        Click(Line(state, set), 0);
        state.Answered("c1");

        // The action was refused: the file still has the clip out of pets, and the next click asks for the same again.
        Click(Line(state, set), 0);

        Assert.Equal([("c1", true), ("c1", true)], set);
    }

    [Fact]
    public void Two_actions_in_the_queue_hold_the_wait_until_both_have_answered()
    {
        var state = new ChipLineState();
        var set = new List<(string Id, bool On)>();
        var line = Line(state, set);
        Click(line, 0);
        Click(line, 0);

        state.Answered("c1");

        Assert.False(state.IsTicked("c1", true));
        state.Answered("c1");
        Assert.True(state.IsTicked("c1", true));
    }

    [Fact]
    public void Another_clip_starts_from_what_the_file_says()
    {
        var state = new ChipLineState();
        var set = new List<(string Id, bool On)>();
        Click(Line(state, set), 0);

        var canvas = new FakeCanvas();
        var other = new CollectionChips(canvas, state, state.Arrange("other clip", Entries), Left, ChipLine.ChipsLimit(canvas, Right), (id, on) => set.Add((id, on)), _ => { });
        Click(other, 0);

        Assert.Equal([("c1", true), ("c1", true)], set);
    }

    [Fact]
    public void The_list_of_the_plus_starts_from_the_wait_too()
    {
        var state = new ChipLineState();
        var set = new List<(string Id, bool On)>();
        var popup = new CheckedListPopup(
            Entries,
            new Box(500, 370, 40, 28),
            Right,
            id => state.IsTicked(id, false),
            state.Asking((id, on) => set.Add((id, on))),
            () => { });
        var (x, y) = (popup.Bounds.MidX, popup.Bounds.Y + CheckedListPopup.Pad + 0.5f * CheckedListPopup.RowHeight);

        popup.Press(x, y);
        popup.Press(x, y);

        Assert.Equal([("c1", true), ("c1", false)], set);
    }
}
