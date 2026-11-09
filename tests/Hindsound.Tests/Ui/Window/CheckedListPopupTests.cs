using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public sealed class CheckedListPopupTests
{
    private const uint Accent = 0xD4F53C;
    private const float Right = 728;

    private static readonly Box Plus = new(500, 370, 40, 28);

    private sealed class Rig
    {
        public Rig(int count, Box? anchor = null)
        {
            Items = [.. Enumerable.Range(0, count).Select(i => new ChipEntry("c" + i, "collection " + i, false))];
            List = new CheckedListPopup(
                Items,
                anchor ?? Plus,
                Right,
                id => Ticked.Contains(id),
                (id, on) =>
                {
                    Set.Add((id, on));
                    if (on)
                    {
                        Ticked.Add(id);
                    }
                    else
                    {
                        Ticked.Remove(id);
                    }
                },
                () => Closed++);
        }

        public ChipEntry[] Items { get; }

        public CheckedListPopup List { get; }

        public HashSet<string> Ticked { get; } = [];

        public List<(string Id, bool On)> Set { get; } = [];

        public int Closed { get; private set; }

        /// <summary>The middle of a row on screen, by its place from the top of what is shown.</summary>
        public (float X, float Y) RowCentre(int place) =>
            (List.Bounds.MidX, List.Bounds.Y + CheckedListPopup.Pad + (place + 0.5f) * CheckedListPopup.RowHeight);
    }

    [Theory]
    [InlineData(6, 176)]
    [InlineData(8, 232)]
    [InlineData(30, 232)]
    public void A_list_with_no_room_below_opens_above_the_plus(int count, float height)
    {
        var box = new Rig(count).List.Bounds;

        Assert.Equal(height, box.H);
        Assert.Equal(Plus.Y - 4 - height, box.Y);
    }

    [Fact]
    public void A_short_list_with_room_below_opens_below_like_the_list_of_a_dropdown()
    {
        var box = new Rig(1).List.Bounds;

        Assert.Equal(Plus.Bottom + 4, box.Y);
        Assert.True(box.Bottom <= SettingsScreen.WindowHeight - 4);
    }

    [Fact]
    public void The_list_never_passes_the_right_of_the_page()
    {
        var box = new Rig(4, new Box(680, 370, 40, 28)).List.Bounds;

        Assert.Equal(Right, box.Right);
    }

    [Fact]
    public void The_list_starts_at_its_plus_when_it_has_the_room()
    {
        Assert.Equal(Plus.X, new Rig(4).List.Bounds.X);
    }

    [Fact]
    public void The_arrows_move_inside_and_stop_at_the_ends()
    {
        var list = new Rig(3).List;

        Assert.Equal(CheckedListKey.Handled, list.Key(Vk.Up));
        Assert.Equal(0, list.Index);
        list.Key(Vk.Down);
        list.Key(Vk.Down);
        list.Key(Vk.Down);
        Assert.Equal(2, list.Index);
        list.Key(Vk.Home);
        Assert.Equal(0, list.Index);
        list.Key(Vk.End);
        Assert.Equal(2, list.Index);
        list.Key(Vk.PageUp);
        Assert.Equal(0, list.Index);
        list.Key(Vk.PageDown);
        Assert.Equal(2, list.Index);
    }

    [Fact]
    public void Moving_past_what_is_shown_scrolls_it()
    {
        var list = new Rig(20).List;

        list.Key(Vk.PageDown);
        list.Key(Vk.Down);
        Assert.Equal(9, list.Index);
        Assert.Equal(2, list.Top);

        list.Key(Vk.Home);
        Assert.Equal(0, list.Top);
    }

    [Theory]
    [InlineData(Vk.Space)]
    [InlineData(Vk.Enter)]
    public void Space_and_Enter_tick_and_untick_and_the_list_stays_open(uint key)
    {
        var rig = new Rig(3);
        rig.List.Key(Vk.Down);

        rig.List.Key(key);
        rig.List.Key(key);

        Assert.Equal([("c1", true), ("c1", false)], rig.Set);
        Assert.Equal(0, rig.Closed);
    }

    [Fact]
    public void Escape_closes_and_stays_on_the_line()
    {
        var rig = new Rig(3);

        Assert.Equal(CheckedListKey.Handled, rig.List.Key(Vk.Escape));

        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void Tab_closes_and_asks_for_the_next_stop()
    {
        var rig = new Rig(3);

        Assert.Equal(CheckedListKey.ClosedMoveOn, rig.List.Key(Vk.Tab));

        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void Other_keys_are_not_for_the_list()
    {
        var rig = new Rig(3);

        Assert.Equal(CheckedListKey.Ignored, rig.List.Key(Vk.Left));
        Assert.Empty(rig.Set);
        Assert.Equal(0, rig.Closed);
    }

    [Fact]
    public void Closing_twice_tells_once()
    {
        var rig = new Rig(3);

        rig.List.Close();
        rig.List.Close();

        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void A_click_ticks_its_row_and_the_list_stays_open()
    {
        var rig = new Rig(3);
        var (x, y) = rig.RowCentre(2);

        rig.List.Press(x, y);

        Assert.Equal([("c2", true)], rig.Set);
        Assert.Equal(2, rig.List.Index);
        Assert.Equal(0, rig.Closed);
    }

    [Fact]
    public void A_click_outside_closes_and_ticks_nothing()
    {
        var rig = new Rig(3);

        rig.List.Press(10, 10);

        Assert.Empty(rig.Set);
        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void A_click_on_the_edge_inside_the_list_does_nothing_at_all()
    {
        var rig = new Rig(3);
        var box = rig.List.Bounds;

        rig.List.Press(box.MidX, box.Y + 1);
        rig.List.Press(box.MidX, box.Bottom - 1);

        Assert.Empty(rig.Set);
        Assert.Equal(0, rig.Closed);

        // Longer than what shows: the margin below the last visible row must not reach the ninth, which is hidden.
        var long12 = new Rig(12);
        long12.List.Press(long12.List.Bounds.MidX, long12.List.Bounds.Bottom - 1);

        Assert.Empty(long12.Set);
        Assert.Equal(0, long12.Closed);
    }

    [Fact]
    public void A_click_after_the_wheel_ticks_the_row_it_is_on()
    {
        var rig = new Rig(20);
        rig.List.Wheel(-5);
        Assert.Equal(5, rig.List.Top);
        var (x, y) = rig.RowCentre(1);

        rig.List.Press(x, y);

        Assert.Equal([("c6", true)], rig.Set);
    }

    [Fact]
    public void The_wheel_stops_at_the_ends()
    {
        var list = new Rig(20).List;

        list.Wheel(3);
        Assert.Equal(0, list.Top);
        list.Wheel(-100);
        Assert.Equal(12, list.Top);
    }

    [Fact]
    public void The_mouse_lights_a_row_and_says_so_once()
    {
        var rig = new Rig(3);
        var (x, y) = rig.RowCentre(1);

        Assert.True(rig.List.Hover(x, y));
        Assert.Equal(1, rig.List.Index);
        Assert.False(rig.List.Hover(x, y));
        Assert.False(rig.List.Hover(10, 10));
        Assert.Equal(1, rig.List.Index);
    }

    [Fact]
    public void An_empty_list_breaks_nothing()
    {
        var rig = new Rig(0);

        rig.List.Key(Vk.Down);
        rig.List.Key(Vk.Space);
        rig.List.Paint(new FakeCanvas(), Accent);

        Assert.Empty(rig.Set);
    }

    [Fact]
    public void Only_what_is_shown_is_painted()
    {
        var rig = new Rig(20);
        var canvas = new FakeCanvas();

        rig.List.Paint(canvas, Accent);

        Assert.Equal(CheckedListPopup.MaxVisible, canvas.Drawn.Count);
        Assert.Contains("collection 7", canvas.Drawn);
        Assert.DoesNotContain("collection 8", canvas.Drawn);
    }

    [Fact]
    public void A_box_shows_its_mark_as_of_now_not_as_of_the_opening()
    {
        var rig = new Rig(2);
        var before = new FakeCanvas();
        rig.List.Paint(before, Accent);
        Assert.Empty(before.Strokes);

        rig.Ticked.Add("c1");
        var after = new FakeCanvas();
        rig.List.Paint(after, Accent);

        Assert.Equal(2, after.Strokes.Count);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void A_long_name_is_cut_inside_its_row(double scale)
    {
        using var bitmap = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);
        var longest = new string('W', 40);
        var drawn = new List<string>();
        var list = new CheckedListPopup([new ChipEntry("c1", longest, true)], Plus, Right, _ => true, (_, _) => { }, () => { });

        list.Paint(new TextWatch(bitmap, drawn), Accent);

        var text = Assert.Single(drawn);
        Assert.EndsWith("...", text);
        Assert.True(bitmap.Measure(text, Face.Body, 13) <= CheckedListPopup.Width - 46);
    }

    /// <summary>The real canvas, and a note of each text it was given.</summary>
    private sealed class TextWatch(ICanvas inner, List<string> drawn) : ICanvas
    {
        public void SetOrigin(float dy) => inner.SetOrigin(dy);
        public void FillRect(Box box, uint rgb) => inner.FillRect(box, rgb);
        public void FillRound(Box box, float radius, uint rgb) => inner.FillRound(box, radius, rgb);
        public void Frame(Box box, float radius, uint fill, uint border) => inner.Frame(box, radius, fill, border);
        public void Ring(Box box, float radius, float thickness, uint rgb) => inner.Ring(box, radius, thickness, rgb);
        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => inner.Stroke(ax, ay, bx, by, width, rgb);
        public void Glow(Box box, float sigma, float opacity, uint rgb) => inner.Glow(box, sigma, opacity, rgb);

        public void Text(string text, Face face, float size, float x, float top, uint rgb)
        {
            drawn.Add(text);
            inner.Text(text, face, size, x, top, rgb);
        }

        public float Measure(string text, Face face, float size) => inner.Measure(text, face, size);
        public float LineHeight(Face face, float size) => inner.LineHeight(face, size);
        public void WarningIcon(float x, float y, float size, uint rgb) => inner.WarningIcon(x, y, size, rgb);
        public void Notification(Hindsound.Ui.NotificationContent content, float x, float y, uint accent, bool mirrored) => inner.Notification(content, x, y, accent, mirrored);
    }
}
