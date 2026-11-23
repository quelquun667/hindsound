namespace Hindsound.Ui.Window;

/// <summary>
/// A small menu of actions over the page: where it lies, which line is lit, and how it is drawn. The menu of a clip and the one of a
/// collection share it. It only remembers; the screen does what is picked.
/// </summary>
internal abstract class ActionMenu
{
    public const float RowHeight = 28, Pad = 4, SeparatorHeight = 9, Margin = 4, ButtonGap = 4;

    /// <param name="width">The width of the menu.</param>
    /// <param name="height">Its height: see <see cref="HeightOf"/>.</param>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="index">The line lit, or -1 for none.</param>
    protected ActionMenu(float width, float height, float x, float y, int index)
    {
        Bounds = new Box(x, y, width, height);
        Index = index;
    }

    public Box Bounds { get; }

    /// <summary>The lit line. None until the mouse or an arrow lights one, when the mouse opened the menu.</summary>
    public int Index { get; private set; }

    public abstract int Count { get; }

    /// <summary>The height of a menu of this many lines, with this many thin rules between them.</summary>
    protected static float HeightOf(int lines, int rules) => lines * RowHeight + 2 * Pad + rules * SeparatorHeight;

    protected abstract string LabelAt(int index);

    /// <summary>The key that does the same from elsewhere, shown at the right of the line. Null for none.</summary>
    protected virtual string? KeyAt(int index) => null;

    /// <summary>The line is drawn in the colour of a warning.</summary>
    protected virtual bool IsDangerAt(int index) => false;

    /// <summary>A thin rule comes before this line.</summary>
    protected virtual bool RuleBefore(int index) => false;

    /// <summary>At the point of a right click, turned to the left or upwards where it would leave the window.</summary>
    protected static (float X, float Y) PlaceAtPoint(float width, float height, float x, float y)
    {
        var left = x + width > SettingsScreen.WindowWidth - Margin ? x - width : x;
        var top = y + height > SettingsScreen.WindowHeight - Margin ? y - height : y;
        return (Math.Max(Margin, left), Math.Max(Margin, top));
    }

    /// <summary>Over a button, its right edge on the button's.</summary>
    protected static (float X, float Y) PlaceAbove(float width, float height, Box button) =>
        (Math.Clamp(button.Right - width, Margin, SettingsScreen.WindowWidth - Margin - width), Math.Max(Margin, button.Y - ButtonGap - height));

    /// <summary>Under a button, its left edge on the button's; over it where the window has no room below.</summary>
    protected static (float X, float Y) PlaceBelow(float width, float height, Box button)
    {
        var below = button.Bottom + ButtonGap;
        var top = below + height > SettingsScreen.WindowHeight - Margin ? Math.Max(Margin, button.Y - ButtonGap - height) : below;
        return (Math.Clamp(button.X, Margin, SettingsScreen.WindowWidth - Margin - width), top);
    }

    /// <summary>The line of an item on the window.</summary>
    public Box RowBox(int index)
    {
        var top = Bounds.Y + Pad;
        for (var i = 0; i <= index; i++)
        {
            if (i > 0 && RuleBefore(i))
            {
                top += SeparatorHeight;
            }

            if (i < index)
            {
                top += RowHeight;
            }
        }

        return new Box(Bounds.X + 1, top, Bounds.W - 2, RowHeight);
    }

    /// <summary>The item under the point, or -1: outside, or on a rule.</summary>
    public int ItemAt(float x, float y)
    {
        if (!Bounds.Contains(x, y))
        {
            return -1;
        }

        for (var i = 0; i < Count; i++)
        {
            if (RowBox(i).Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The mouse moved. True if another line is lit, the only moves worth a repaint.</summary>
    public bool Hover(float x, float y)
    {
        var item = ItemAt(x, y);
        if (item < 0 || item == Index)
        {
            return false;
        }

        Index = item;
        return true;
    }

    /// <summary>An arrow: the next line, round from the last to the first. From no line lit, Down lights the first and Up the last.</summary>
    public void Move(int by)
    {
        Index = Index < 0 ? (by > 0 ? 0 : Count - 1) : ((Index + by) % Count + Count) % Count;
    }

    /// <summary>Home or End.</summary>
    public void MoveTo(bool first) => Index = first ? 0 : Count - 1;

    public void Paint(ICanvas c, uint accent)
    {
        c.Glow(Bounds, 8, 0.35f, 0x000000);
        c.Frame(Bounds, 4, Theme.Raised, Theme.Control);
        for (var i = 0; i < Count; i++)
        {
            var row = RowBox(i);
            if (i > 0 && RuleBefore(i))
            {
                c.FillRect(new Box(Bounds.X + 10, row.Y - SeparatorHeight / 2 - 0.5f, Bounds.W - 20, 1), Theme.Control);
            }

            if (i == Index)
            {
                c.FillRect(row, Theme.Hover);
                c.FillRect(new Box(row.X, row.Y + 5, 3, RowHeight - 10), accent);
            }

            var warn = IsDangerAt(i);
            c.TextMid(LabelAt(i), Face.Body, 13, row.X + 14, row.MidY, warn ? Theme.Hot : Theme.Text);
            if (KeyAt(i) is { } key)
            {
                c.TextRight(key, Face.Mono, 12, row.Right - 14, row.MidY, warn ? Theme.Hot : Theme.Faded);
            }
        }
    }
}
