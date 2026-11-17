using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>What the menu of a clip offers, in its order.</summary>
internal enum ClipMenuItem
{
    Rename,
    Edit,
    Soundboard,
    Copy,
    ShowInFolder,
    Delete,
}

/// <summary>The keys of the clip actions that <see cref="Vk"/> does not name.</summary>
internal static class ClipKeys
{
    public const uint Delete = 0x2E, F2 = 0x71;
}

/// <summary>
/// The menu of one clip, over the page: from a right click on its row, at the mouse, or from the "..." button of the foot, above it.
/// It only remembers where it is and which line is lit; the screen does what is picked.
/// </summary>
internal sealed class ClipMenu
{
    public const float RowHeight = 28, Pad = 4, SeparatorHeight = 9, Width = 236, Margin = 4, ButtonGap = 4;
    public const string RenameLabel = "Rename", EditLabel = "Edit...", SoundboardLabel = "Send to soundboard...", DeleteLabel = "Delete";

    private static readonly ClipMenuItem[] WithEditor =
        [ClipMenuItem.Rename, ClipMenuItem.Edit, ClipMenuItem.Soundboard, ClipMenuItem.Copy, ClipMenuItem.ShowInFolder, ClipMenuItem.Delete];

    private static readonly ClipMenuItem[] WithoutEditor =
        [ClipMenuItem.Rename, ClipMenuItem.Copy, ClipMenuItem.ShowInFolder, ClipMenuItem.Delete];

    private ClipMenu(ClipGroup clip, IReadOnlyList<ClipMenuItem> items, float x, float y, int index)
    {
        Clip = clip;
        Items = items;
        Bounds = new Box(x, y, Width, HeightOf(items));
        Index = index;
    }

    /// <summary>The clip the menu acts on: the one chosen when it opened, whatever is chosen since.</summary>
    public ClipGroup Clip { get; }

    public IReadOnlyList<ClipMenuItem> Items { get; }

    public Box Bounds { get; }

    /// <summary>The lit line. None until the mouse or an arrow lights one, when the mouse opened the menu.</summary>
    public int Index { get; private set; }

    /// <summary>The line lit, if any.</summary>
    public ClipMenuItem? Current => Index >= 0 && Index < Items.Count ? Items[Index] : null;

    /// <summary>The lines: Edit... and Send to soundboard... only when there is an editor to open.</summary>
    public static IReadOnlyList<ClipMenuItem> ItemsFor(bool editor) => editor ? WithEditor : WithoutEditor;

    /// <summary>At the point of a right click, turned to the left or upwards where it would leave the window.</summary>
    public static ClipMenu AtPoint(ClipGroup clip, IReadOnlyList<ClipMenuItem> items, float x, float y)
    {
        var height = HeightOf(items);
        var left = x + Width > SettingsScreen.WindowWidth - Margin ? x - Width : x;
        var top = y + height > SettingsScreen.WindowHeight - Margin ? y - height : y;
        return new ClipMenu(clip, items, Math.Max(Margin, left), Math.Max(Margin, top), -1);
    }

    /// <summary>Over the button that opened it, its right edge on the button's. The keyboard lights the first line; the mouse none.</summary>
    public static ClipMenu Above(ClipGroup clip, IReadOnlyList<ClipMenuItem> items, Box button, bool byKey)
    {
        var height = HeightOf(items);
        var left = Math.Clamp(button.Right - Width, Margin, SettingsScreen.WindowWidth - Margin - Width);
        return new ClipMenu(clip, items, left, Math.Max(Margin, button.Y - ButtonGap - height), byKey ? 0 : -1);
    }

    public static string LabelOf(ClipMenuItem item) => item switch
    {
        ClipMenuItem.Rename => RenameLabel,
        ClipMenuItem.Edit => EditLabel,
        ClipMenuItem.Soundboard => SoundboardLabel,
        ClipMenuItem.Copy => "Copy",
        ClipMenuItem.ShowInFolder => SettingsScreen.ShowInFolderLabel,
        _ => DeleteLabel,
    };

    /// <summary>The key that does the same from the list, shown at the right of the line.</summary>
    public static string? KeyOf(ClipMenuItem item) => item switch
    {
        ClipMenuItem.Rename => "F2",
        ClipMenuItem.Delete => "Del",
        _ => null,
    };

    /// <summary>The line of an item on the window. A thin rule comes before Delete.</summary>
    public Box RowBox(int index)
    {
        var top = Bounds.Y + Pad;
        for (var i = 0; i < index; i++)
        {
            top += RowHeight + (Items[i + 1] == ClipMenuItem.Delete ? SeparatorHeight : 0);
        }

        if (index == 0 && Items[0] == ClipMenuItem.Delete)
        {
            top += SeparatorHeight;
        }

        return new Box(Bounds.X + 1, top, Bounds.W - 2, RowHeight);
    }

    /// <summary>The item under the point, or -1: outside, or on the rule.</summary>
    public int ItemAt(float x, float y)
    {
        if (!Bounds.Contains(x, y))
        {
            return -1;
        }

        for (var i = 0; i < Items.Count; i++)
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
        Index = Index < 0 ? (by > 0 ? 0 : Items.Count - 1) : ((Index + by) % Items.Count + Items.Count) % Items.Count;
    }

    /// <summary>Home or End.</summary>
    public void MoveTo(bool first) => Index = first ? 0 : Items.Count - 1;

    public void Paint(ICanvas c, uint accent)
    {
        c.Glow(Bounds, 8, 0.35f, 0x000000);
        c.Frame(Bounds, 4, Theme.Raised, Theme.Control);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var row = RowBox(i);
            if (item == ClipMenuItem.Delete && i > 0)
            {
                c.FillRect(new Box(Bounds.X + 10, row.Y - SeparatorHeight / 2 - 0.5f, Bounds.W - 20, 1), Theme.Control);
            }

            if (i == Index)
            {
                c.FillRect(row, Theme.Hover);
                c.FillRect(new Box(row.X, row.Y + 5, 3, RowHeight - 10), accent);
            }

            var warn = item == ClipMenuItem.Delete;
            c.TextMid(LabelOf(item), Face.Body, 13, row.X + 14, row.MidY, warn ? Theme.Hot : Theme.Text);
            if (KeyOf(item) is { } key)
            {
                c.TextRight(key, Face.Mono, 12, row.Right - 14, row.MidY, warn ? Theme.Hot : Theme.Faded);
            }
        }
    }

    private static float HeightOf(IReadOnlyList<ClipMenuItem> items) =>
        items.Count * RowHeight + 2 * Pad + (items.Contains(ClipMenuItem.Delete) ? SeparatorHeight : 0);
}
